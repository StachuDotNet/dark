# Async / concurrency at the language level

One place for the Task/Ply-replacement decision. Everything async lands here so
the choice does not scatter across [event-bus.md](event-bus.md),
[cli-daemon.md](cli-daemon.md), [composable-mvu.md](composable-mvu.md), and the
research notes.

## The decision

**Kill Task/Ply and roll our own async.** Today every Dark fn returns
`Ply.Ply<Dval>` and the whole runtime is threaded through .NET `Task`. That gets
us host-level concurrency for free, but it means the *behavior* of async — when a
frame suspends, what it is waiting on, whether it can be parked, inspected, or
cancelled — is owned by the host, not by us. We want to own it, because the
features we care about are exactly the ones the host hides:

- **Park threads deliberately.** A frame that needs a not-yet-ready value should
  suspend onto our scheduler, not block a thread pool slot.
- **Manage nested processes.** Parent/child execution trees, sibling
  cancellation, structured cleanup — owned by us, not delegated to `Task`.
- **Inspect what is running.** Dark UXs should be able to ask "what frames are
  parked, on what, since when?" via **opt-in debug symbols**. That requires the
  suspension points to be *our* values, not host continuations we cannot see.

The cost is real (we give up a mature runtime), so the migration is staged and
narrow at first (see below). No heavy .NET-concurrency study for now — the design
is about the Dark-level model, not about out-reading the CLR.

## The model: explicit await on the event bus

Direct-style stays the default surface, but the await point is **explicit**, not
hidden:

```dark
let urls : List<String> = ["https://...", ...]
let results : List<Promise<HttpResult>> = urls |> List.map HttpClient.get
let first : HttpResult = (results |> List.head)!     // ! forces — frame parks here
first.body
```

- A `Promise<T>` is a value that may not be ready yet. Producing it does **not**
  block — the frame moves on holding the promise.
- The trailing `!` is the **only** park-point at the Dark level. It compiles to a
  wait on the promise's underlying event bus (`EventBus.waitForOne`), and the
  frame parks until the matching event fires.
- The scheduler runs other ready frames meanwhile; on resolution the parked frame
  re-enters the ready set and resumes from the same instruction.

This is the parking machinery already described in [event-bus.md](event-bus.md);
async is that machinery seen from the language surface. Because await points are
explicit, the debugger and traces can show *every* place a frame suspends — no
hidden suspension points. That is the inspectability we are paying for.

## Why this ties to event sourcing

In the [distributed event-sourcing](distributed-event-sourcing.md) frame, a
`Promise<T>` is "the next op/event matching this selector." So:

- **Waiting** is subscribing to a stream's first emission.
- **Playback** is re-running a frame against a recorded op stream — deterministic
  because the await points are explicit and the events are ordered.
- **Sync** is just another producer: an op arriving from a peer can be exactly the
  event a local frame is parked on.

Async, conflicts, sync, and materialization all become "a frame parked on an
event that some producer will emit." One coordination model, many producers.

## Concrete migration sketch

Staged so each step preserves existing sequential behavior:

1. **Introduce our continuation type** behind the `Ply` surface. Define
   `DarkAsync<'T>` (a resumable computation: either `Done of 'T` or
   `Parked of selector * (Dval -> DarkAsync<'T>)`). Initially it is a thin wrapper
   over `Ply` so nothing changes observably.
2. **Add the scheduler** (`ready` queue + `parked` map keyed by event selector,
   per [event-bus.md](event-bus.md)). The eval loop pulls from `ready`; a `Parked`
   result moves the frame to `parked`; a matching event moves it back.
3. **Make suspension points our values.** `Promise<T>` and the `!` force-builtin
   produce/consume `DarkAsync`, so park state is a Dark-visible value, not a host
   continuation.
4. **Thread cancellation** through the scheduler: a parked frame carries an
   optional timeout/cancel selector; waking with a `Cancelled` injection unwinds it.
5. **Peel Ply away** where we now own the suspension: builtins that only ever
   parked on our selectors stop returning `Ply` and return `DarkAsync`. Pure
   builtins need no change. This is incremental — Ply and `DarkAsync` coexist
   until the last host-level await is gone.
6. **Opt-in debug symbols.** When enabled, each park records `(frameId, selector,
   parkedAt)` so a UX can project the live park-set. Off by default for cost.

## Opinion on the coworker's "Dark Async Plan"

The coworker's plan (vault `Current Experiment/Design/Dark Async Plan.md`) solves
a **different, complementary** problem and reaches a **different** conclusion on
the surface question. Worth being precise about both.

**Where it agrees / is reusable:**

- Direct-style Dark source — function calls return values, no `Task`/`Ply` in user
  code. We keep that.
- Child VMs / fibers with branch-local mutable state and only safe
  `ExecutionState` shared. We need exactly this for parked-frame isolation.
- Structured concurrency: parent owns children, cancellation propagates, no orphan
  work. Adopt wholesale.
- The blob-lifetime and single-consumer-stream hazards it identifies are real and
  block any concurrent execution; fixing them is a prerequisite either way.
- The **effect model** (`Pure`, `AsyncRead`, `AsyncWrite`, `OrderedIO`,
  `ConcurrentSafe`, `Blocking`, `Resource(...)`, `Harmful`) is genuinely useful —
  and it dovetails with [capabilities.md](capabilities.md), where effects and caps
  are close cousins.

**Where it differs from us:**

- It keeps the await point **implicit** — a dependency planner + scheduler
  auto-parallelizes independent `ConcurrentSafe AsyncRead` calls; the user never
  sees suspension. We want await **explicit** (`!`), because inspectability and
  deterministic playback depend on the suspension points being visible Dark
  values. These are not contradictory: auto-parallelization of independent calls
  can sit *under* an explicit-await surface — the planner overlaps work between two
  explicit `!`s; the `!` is still where the program observably waits.
- It keeps `Ply` as the host substrate and layers a planner on top. We want to own
  the suspension primitive so we can park, inspect, and replay. The reconciliation:
  start with her effect-metadata + child-VM + cancellation work (which we need
  regardless), and replace `Ply` with `DarkAsync` only once our scheduler exists.

**Synthesis.** Take her effect model, child-VM isolation, structured concurrency,
and the blob/stream/cancellation hardening as the foundation. Put our explicit
`Promise<T>` + `!` + event-bus parking as the surface and the suspension primitive
on top. Effects then do double duty: they drive *her* auto-parallel planner *and*
feed *our* capability checks. The two plans are layers, not rivals.

## Open questions

- On one auto-parallel branch failing: cancel siblings eagerly, or finish and
  aggregate? (Lean: structured-concurrency cancel, with an opt-in aggregate mode.)
- How much effect metadata is persisted vs. inferred/cached at load?
- Are streams affine at the language level, or guarded by a runtime lock + clear
  failure on concurrent consumption?
- When does auto-parallelization become default-on vs. opt-in behind a flag?
