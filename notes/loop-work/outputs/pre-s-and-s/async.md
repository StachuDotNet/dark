# Async / concurrency at the language level

Where the Task/Ply-replacement decision lives. On `main` today every Dark fn returns
`Ply<Dval>` (`DvalTask = Ply<Dval>`, `RuntimeTypes.fs`) and execution is threaded through
.NET `Task` (`Execution.execute : Task<_>`). Ply is everywhere — interpreter, streams,
type-checker. So this is a large, staged migration, not a flip.

## Default surface: async is invisible

**A Dark user should not see async.** No `async`/`await` keywords, no colored functions,
no syntactic difference between code that does I/O and code that doesn't. You write
direct-style; the scheduler overlaps independent work for you.

```dark
// No async syntax anywhere. These three GETs have no data dependency between them,
// so the scheduler issues them concurrently and joins — the author did nothing special.
let [a; b; c] = [urlA; urlB; urlC] |> List.map HttpClient.get
combine a b c
```

What makes that safe is **effect metadata**, not annotations the user writes: each builtin
already declares its effect (and its capability — see [capabilities.md](capabilities.md)).
Independent `AsyncRead`/`ConcurrentSafe` calls can be planned to run together; anything
`OrderedIO` or `Blocking` stays sequential. The planner reads metadata the builtins already
carry, so the *surface* stays plain.

## The escape hatch: explicit control, only when you need it

Invisible covers the common case. When a user genuinely has scheduling needs — fan out a
thousand requests with a concurrency cap, race two sources, inspect what's parked — they
drop to an explicit primitive. This is opt-in, not the default path.

```dark
let ps : List<Promise<HttpResult>> = urls |> List.map HttpClient.get   // producing doesn't block
let first = Promise.race ps                                            // explicit: first to resolve
let capped = Promise.all (ps |> Promise.withLimit 20)                  // explicit: bounded fan-out
```

A `Promise<T>` is a value that may not be ready; forcing one parks the frame on the event
bus ([event-bus.md](event-bus.md)) until it resolves. The two surfaces are one mechanism:
the invisible planner *also* parks frames on the bus — it just inserts the wait for you.

```
 invisible default ──┐
                     ├──> scheduler: ready-queue + parked-map (keyed by event selector)
 explicit Promise ───┘        parked frame == a subscriber waiting for its event
```

## Is "build our own scheduler" actually worth it? — verdict

**Yes, but only because of three things we can't get from `Task`/`Ply`, and only
incrementally.** Owning the suspension primitive buys exactly:

1. **Inspectability** — "what frames are parked, on what, since when?" needs the suspension
   points to be *our* values, not opaque host continuations. **Prototyped (prework,
   `compose-check`):** `Scheduler.ParkSet` (a concurrent `frameId → {frameId; selector;
   parkedAt}` registry) makes exactly this observable — `Scheduler.park` registers a frame
   while it waits and deregisters on resume (via `try/finally`), and `Scheduler.parked` returns
   the live snapshot. Tested: a parked frame is visible while waiting and gone on resume;
   multiple frames are tracked with selective resume (only the matching selector wakes). This is
   the bookkeeping behind a `dark debug` park view (row F), built directly on `waitForOne` — so
   the inspectability claim is demonstrated, not just asserted.
2. **Deterministic playback** — replaying a frame against a recorded op stream requires the
   await points to be explicit, ordered, and ours (see next section).
3. **Capability/effect integration** — the same effect metadata drives the planner *and*
   the cap gate; one model, not two.

If we didn't want those, **keep `Ply`** — rolling our own concurrency to re-implement a
mature runtime would be a bad trade. We want all three, so it's justified — but the cost is
real, so the first syncing milestone should lean on the thin-wrapper stage (below), not a
finished scheduler.

## Relation to op-playback + EventBus — and sequencing

The scheduler and the event bus are **the same machinery**: a parked frame is a subscriber;
an event waking it is an op being applied. A `Promise<T>` is "the next op/event matching
this selector" ([distributed-event-sourcing.md](distributed-event-sourcing.md)). So async,
parking, and playback are one coordination model with many producers.

**Do we do async and op-playback together, or async-first?** They share the suspension
primitive, so they can't be fully independent — but they *stage*:

```
Stage A  effect metadata + child-VM isolation + structured cancellation   ← needed either way
Stage B  DarkAsync behind Ply (thin wrapper, no behavior change)          ← async foundation
Stage C  scheduler: ready/parked on event-bus selectors                   ← async becomes real
   └─ op-playback rides Stage C: record selectors+events, replay deterministically
Stage D  peel Ply where we own suspension; planner overlaps independent calls
```

Op-playback is **not a separate effort** — it's what Stage C's deterministic suspension
points *give you* once they exist. So: async-first through Stage C, and playback falls out
of the same primitive rather than being a later "separate ops from playback" project.

> **Stage A substrate is built AND composes (prework, `loop-fun:prework/compose-check`).** The
> three things Stage C's scheduler consumes — `effects` metadata, `VMState.spawnChild` (child-VM
> isolation) + `cancel`/`throwIfCancelled` (now wired live into the interpreter eval loop), and the
> EventBus `waitForOne` parking primitive — were each built+tested on their own branch and then
> **merged onto one branch that builds clean (0 errors) with all 16 foundation tests passing
> together** (AsyncStageA 6, EventBus 7, ConflictDispatch 3). Async Stage A merged with **zero**
> conflicts; the only merge friction was test-registration lines. So the scheduler (Stage C / effort
> 6) starts from a *composition-proven* substrate, not three separately-validated pieces that might
> clash. The remaining Stage C work is the genuinely hard part the table marks "large — core
> interpreter change": the `ready`/`parked` queues + the eval-loop pull/park/wake, and an
> `EventSelector` tying a parked frame to a bus predicate (the same gap `Resolution.Park` in
> conflict-dispatch is waiting on).

## Migration sketch + rough effort

Each step preserves existing sequential behavior; sizing is relative (no metered units).

| Step | What | Effort |
|---|---|---|
| A | Effect metadata on builtins + child-VM isolation + cancellation (prereqs) | **large** — touches every builtin assembly |
| B | `DarkAsync<'T>` (`Done` \| `Parked of selector * (Dval -> DarkAsync<'T>)`) as a thin Ply wrapper | **medium** — type + plumbing, no behavior change |
| C | Scheduler: `ready` queue + `parked` map keyed by selector; eval loop pulls/parks/wakes | **large** — core interpreter change |
| D | `Promise<T>` + force as Dark-visible values; peel Ply off builtins that only park on our selectors | **medium**, incremental — Ply and DarkAsync coexist |
| E | Invisible planner: overlap independent `ConcurrentSafe AsyncRead` calls between waits | **medium** — sits *above* C, opt-in→default |
| F | Opt-in debug symbols: each park records `(frameId, selector, parkedAt)` for a live park-set view | **small** — off by default |

```fsharp
type DarkAsync<'T> =
  | Done   of 'T
  | Parked of selector : EventSelector * resume : (Dval -> DarkAsync<'T>)
// scheduler: pull from `ready`; a Parked result moves the frame to `parked`;
// a matching event moves it back to `ready` and resumes from the same instruction.
```

## Lineage: the coworker's "Dark Async Plan"

Complementary, not rival. **Take from it:** the effect model (`Pure`/`AsyncRead`/
`AsyncWrite`/`OrderedIO`/`ConcurrentSafe`/`Blocking`/`Resource`/`Harmful`), child-VM
isolation, structured concurrency, and its blob-lifetime + single-consumer-stream hardening
(real hazards, prerequisite either way). Its planner gives us the **invisible default**
above. **Where we go further:** we own the suspension primitive (`DarkAsync`) so we can
park, inspect, and replay — its planner sits *on top* of our scheduler. Effects then do
double duty: drive the planner *and* feed the capability gate.

## Open questions

- One auto-parallel branch fails: cancel siblings eagerly, or finish + aggregate?
  *(Lean: structured-concurrency cancel, opt-in aggregate mode.)*
- Effect metadata persisted vs. inferred/cached at load?
- Streams affine at the language level, or runtime-locked with a clear concurrent-use failure?
- When does the invisible planner flip from opt-in to default-on?
