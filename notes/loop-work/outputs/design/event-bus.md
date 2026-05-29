# Event Bus

**What `await` should look like** in a Darklang runtime that wants to coordinate
async I/O, sync, human review, and capability requests under one model — without
F#-level callback soup.

This lives in the syncing-and-stable substrate: the EventBus is how the runtime
stops being a synchronous-fail-on-missing model and becomes coordination-aware.

## Naming: EventBus vs the existing `Stream.fs`

Main already has `backend/src/LibExecution/Stream.fs` (~292 LoC), but it is a
**data stream**: a lazy, single-consumer, non-persistable `DStream<'a>` Dval built
on `StreamImpl` (`FromIO | Mapped | Filtered | Take | Concat`). It is pull-based —
a consumer calls `streamNext` to draw one element — and draining is destructive,
so it cannot be replayed. It exists to iterate HTTP response bodies, file reads,
and generator-shaped builtin returns.

The event-coordination substrate this doc designs is a different thing, and to
avoid colliding with that name we call it **`EventBus<T>`**:

| Aspect | `Stream.fs` / `DStream` (exists) | `EventBus<T>` (new, below) |
|---|---|---|
| Direction | pull (consumer pulls) | push (producer publishes) |
| Consumers | single | many (broadcast) |
| Replayability | one-shot (consumed = gone) | append-only / replayable |
| Use case | streaming a request body | "materialization done", "cap denied" |
| Lives in | `Dval` (user value) | runtime substrate (system-level) |

The Dark-side single-shot wrapper over an EventBus is called `Promise<T>`.

## What main does not yet have

- A push-based, multi-subscriber bus.
- A frame-parking scheduler above F#'s `Ply.Ply`.
- The Dark-side `Promise<T>` surface with an explicit `!` dereference.
- A link between events and the conflict-resolution dispatch.

`Ply` is the F# concurrency primitive — every fn returns `Ply.Ply<Dval>`. Parking
is added on top of Ply, not in place of it.

## Promises that compose

The async surface is: **all values are promises; every promise is dereferenced by
an explicit operator** (a trailing `!`). Under that surface:

- A `Promise<T>` is a value that may not be ready yet.
- A frame that *needs* a not-yet-ready promise to progress parks on it.
- The scheduler runs other ready frames meanwhile.
- When the promise resolves — typically because an EventBus emitted a matching
  value — the parked frame wakes.

A promise is just a one-slot subscription to "this bus's next matching value."
Buses compose into graphs; promises compose into chains. Same mechanism, two
user-level shapes.

## Event kinds

An `EventBus<T>` is typed, append-only, and multi-subscriber. Subscribers register
interest with a predicate; the bus notifies each on a matching emit.

| Kind | Producers | Subscribers |
|---|---|---|
| `Materialized name body` | a materializer of a value (LLM call, corpus search, computed result) | frames parked on the name, viewer |
| `CapabilityResolved cap result` | capability dispatcher, human approval | frames parked on the cap |
| `ConflictResolved id resolution` | conflict dispatch | frames parked on the conflict, audit |
| `SyncOpArrived op` | sync layer | op applier, viewer |
| `HumanResponded query answer` | user input | frames parked on the question, viewer |
| `BodyChanged hash` | hot-reload, refine, sync | frames using the old body, viewer |
| `FrameParked id reason` | scheduler | viewer, debugger |
| `FrameWoken id` | scheduler | viewer, debugger |
| `EvalStepped frame` | interpreter | trace recorder, viewer |
| `CostIncurred kind amount` | LLM caller, network | budget enforcer |

These are concrete typed event kinds, not generic Msgs: the payload rides the type
system and subscribers pattern-match.

Reminder of the ops/projections lens: the durable events (sync ops, conflicts,
capability grants) are the timestamped op stream. Everything a subscriber renders —
a viewer pane, a dep graph, a budget total — is a *projection* of those events, not
a separate mutable store.

## Composition — graphs, not just buses

Buses compose:

- **Map** — `EventBus<A>.map(f) : EventBus<B>` — transform each event
- **Filter** — `EventBus<A>.filter(p) : EventBus<A>` — drop non-matching
- **Merge** — `EventBus<A> + EventBus<A> : EventBus<A>` — union
- **Join** — `EventBus<A>.zip(EventBus<B>) : EventBus<(A,B)>` — pair
- **Until** — `EventBus<A>.until(EventBus<B>) : EventBus<A>` — stop on B's emit
- **First** — `EventBus<A>.first : Promise<A>` — take one, then unsubscribe

`Promise` is `EventBus.first`: the bridge from the substrate to the Dark-facing
model. Graphs emerge naturally — the viewer subscribes to
`Materialized + ConflictResolved + HumanResponded` filtered to the in-focus fn; the
budget enforcer subscribes to `CostIncurred` aggregated over a session; the sync
layer subscribes to all op events and forwards them.

## Parking semantics + the wake protocol

A frame parks when it tries to read a value that is not ready. Parking is
**per-frame, not per-VM** — other frames in the same VM keep running, so the runtime
is concurrent at the frame level. The frame's register state is preserved as a
continuation (in F# terms, essentially the `Ply` continuation), and the scheduler
keeps a wait-list keyed by the event selector.

Step-by-step:

1. Frame `F` evaluates an expression needing a `Pending(handle)` body.
2. The body isn't cached → `Conflict.PendingUnresolved(handle)`.
3. Dispatch returns `Resolution.Park selector` where `selector = ByMaterializedName name`.
4. Scheduler records `parked[selector] += { frameId; continuation; ... }`, removes
   `F` from `ready`, and subscribes once to the materialization bus with the
   predicate `event.name = name`.
5. The eval loop picks the next `ready` frame.
6. Eventually a producer publishes `Materialized { name; body }`.
7. The subscription fires: look up `parked[ByMaterializedName name]`, find `F`,
   remove it, call `continuation body` to build the resumed Ply, push the frame
   back onto `ready`.
8. The eval loop picks `F` up and it resumes at the same call site with the body
   in hand.

The continuation is a closure over the frame's register state at park time. F#
closures plus the existing `CallFrame` machinery in `VMState` make this tractable.

## F# substrate — thin but tight

Design intent: the F# side is the **minimum** primitive that LibExecution needs —
a bus, a scheduler, and a wait-step inside a Ply. Everything ergonomic (named
subscriptions, composition operators, the `Promise` library) is built in Dark on
top. This keeps LibExecution small and matches the ProgramTypes vibe: typed
records, no hidden control flow, no new F# async machinery beyond Ply.

### The bus

```fsharp
module LibExecution.EventBus

open Prelude

/// One bus per event kind. Producers publish; subscribers register a predicate +
/// handler; a publish runs all matching handlers, each on its own Ply.
type EventBus<'T> =
  { subscribers : Concurrent.ConcurrentDictionary<Guid, Subscription<'T>>
    history : RingBuffer<'T> option       // for late subscribers + replay; None when not needed
    persistTo : EventPersistence }        // durable buses append on publish

and Subscription<'T> =
  { id : Guid
    predicate : 'T -> bool
    handler : 'T -> Ply.Ply<unit>
    oneShot : bool }                      // unsubscribe after first match

and EventPersistence =
  | NotPersisted
  | PersistedAsBlob of tableName : string

let publish (bus : EventBus<'T>) (event : 'T) : Ply.Ply<unit> = ...
let subscribe (bus : EventBus<'T>) (p : 'T -> bool) (h : 'T -> Ply.Ply<unit>) : Subscription<'T> = ...

/// The frame-parking primitive: completes on the next matching event, then unsubscribes.
let waitForOne (bus : EventBus<'T>) (p : 'T -> bool) : Ply.Ply<'T> = ...
```

### System buses + scheduler on ExecutionState

```fsharp
type RuntimeBuses =
  { materialization : EventBus<MaterializationEvent>
    bodyChanged     : EventBus<BodyChangedEvent>
    conflict        : EventBus<ConflictResolvedEvent>
    capability      : EventBus<CapabilityEvent>
    syncIn          : EventBus<SyncEvent>           // events arriving over the wire
    syncOut         : EventBus<SyncEvent>           // events to push out
    humanQuery      : EventBus<HumanQueryEvent>
    frameLifecycle  : EventBus<FrameLifecycleEvent> }

type Scheduler =
  { parked : Dictionary<EventSelector, List<ParkedFrame>>   // keyed by what each frame waits on
    ready : Queue<CallFrameId> }

and ParkedFrame =
  { frameId : CallFrameId
    continuation : Dval -> Ply.Ply<Dval>   // run on wake
    parkedAt : DateTime
    timeout : Option<DateTime> }           // fail the frame if it elapses

and EventSelector =
  | ByMaterializedName of FQFnName.T
  | ByConflictId of ConflictId
  | ByCapability of Capability * forAgent : AccountId
  | ByHumanQuery of QueryId
  | Custom of (RTEvent -> bool)            // escape hatch
```

`ExecutionState` gains `buses : RuntimeBuses` and `scheduler : Scheduler`. The
eval-loop tick reads from `ready` and, on `Resolution.Park`, writes to `parked`.
The conflict dispatch *produces* park decisions; the bus and scheduler *consume*
them. `Stream.fs` is untouched — a separate primitive.

### Coexisting with Ply

Park does not replace Ply; it inserts a wait-step *inside* a Ply. A parked frame's
continuation is a `Dval -> Ply.Ply<Dval>` closure — ordinary Ply shape. The
scheduler dispatches Ply runs from `ready`; the bus adds to `ready` on a match. No
F#-side async beyond what Ply already gives us.

## Dark-side `Promise<T>` and `!`

The user surface is simple. `!` is the only park-point at the Dark level:

```dark
let urls : List<String> = [ "http://...", ... ]
let results : List<Promise<HttpResult>> =
  urls |> List.map HttpClient.get        // returns a Promise, does not block
let first : HttpResult = (results |> List.head)!   // ! forces; the frame parks here
first.body
```

Each producing builtin returns immediately with a `DPromise` slot and publishes to
a per-promise `EventBus<Dval>` when done. `myPromise!` compiles to a builtin that
fast-paths if the slot is already resolved, otherwise parks the frame on the
promise's bus via `waitForOne`:

```fsharp
type PromiseSlot = { id : Guid; resolved : Dval option ref }   // None = pending, Some = ready
// `force`: check slot.resolved; else scheduler.park (ByPromise slot.id); Ply resumes on wake.
```

This keeps await-points **explicit**: no hidden suspension, the debugger sees where
each frame parks, and traces show every `!`.

## Persistence — durable vs not

Most events are ephemeral; a few are durable. The `persistTo` field controls it:
durable buses append a row on every `publish` and replay reads back from the table.

| Bus | Durable | Why |
|---|---|---|
| `materialization` | no | high frequency; replay would re-trigger expensive producers |
| `bodyChanged` | no (the `commits` table is the durable form) | hot-reload doesn't need replay |
| `conflict` | yes (`conflicts_v0`) | audit + sync |
| `capability` | yes (`capability_log_v0`) | audit + security |
| `syncIn` / `syncOut` | yes (op tables) | the canonical form of sync |
| `humanQuery` | yes | so a session restart resurfaces pending questions |
| `frameLifecycle` | no | volume too high; viewer subscribes in real time |

## Precedent — classic-dark's QueueWorker / EventQueueV2

This is incremental, not novel. Classic-dark (`~/code/classic-dark/`) shipped a
working event system: `LibBackend/EventQueueV2.fs` (~465 LoC) plus
`QueueWorker/QueueWorker.fs` (~381 LoC), documented in `docs/eventsV2.md` (May 2022).
Its shape largely carries over.

What it did, and what to borrow:

- **DB table as source of truth, signal as ephemeral.** The `events_v2` SQL table
  was canonical; PubSub only said "now would be a good time to check the table."
  This is exactly the `EventPersistence` design: durable buses write to SQLite, and
  the F# subscriber dispatch is the signal. Cross-instance, the SQL table is the
  synced op stream and the signal is HTTP/WS.
- **Tiny notifications** — `eventID + canvasID + deliveryAttempt`; data loaded from
  the table on receive. Cross-instance, ship the ID + minimal metadata and let the
  receiver load from its local op tables.
- **At-least-once with lock claims** (`claimLock`) so workers don't double-process.
  For durable buses (conflict, capability, syncIn) grab a `claimed_by + claimed_at`
  before invoking the handler.
- **Retry policy** — encode as a bus-level policy (re-enqueue with backoff), not a
  per-handler concern.
- **Named WORKER handlers** — users named a worker by the event name, so subscribing
  to a stream from Dark code was the original ergonomic move. Reuse it:
  `Stdlib.Bus.subscribe "materialization" (fun ev -> ...)`. Same for
  `Stdlib.Bus.pause` (block new invocations without losing queued events).
- **Crons through the same pipeline** — a cron tick is just `Bus.publish "cron-tick" ()`;
  no separate cron infrastructure.

What is genuinely new here: multiple typed buses instead of one global queue;
composition operators instead of name-only routing; **frame parking** (classic had
no concept of an in-flight expression pausing on an event); the `Promise<T>` + `!`
await surface; and cross-instance reach via the sync wire protocol.

The lesson: don't build a custom infra dependency (PubSub was GCP-specific and an
ops burden). SQLite-as-truth plus in-process notifications for v1; add cross-instance
HTTP/WS later. At-least-once plus lock-claim is the right delivery semantic, and the
named-subscriber UX is what users will reach for.

## The sync and stability path

This is the mechanism that eventually lets us **delete the `.dark` files from the
repo** — the least-total-code endpoint. The chain:

1. **Stream events.** Every durable change (a package op, a branch op, a conflict
   resolution) is published as an event and appended to its op table. State *is* the
   timestamped op stream.
2. **Replay them.** Because durable buses keep history, a fresh instance rebuilds its
   world by replaying the op stream. Bootstrapping from `.dark` source files becomes
   just one possible producer of those ops — and an unnecessary one once the op
   stream is itself the source of truth.
3. **Detect conflicts.** Concurrent ops produce conflicts. Conflicts surface as
   events on the `conflict` bus, so a conflict is itself a (sub)stream that frames
   can park on and that the dispatch resolves.
4. **Compose.** Sync is just another producer (`syncIn`) and consumer (`syncOut`).
   The applier, the viewer, and the budget enforcer are all subscribers. No special
   sync plumbing — it is the same bus machinery filtered differently.

Once events, replay, and conflict resolution all run through one bus, the repo's
`.dark` files are redundant: the synced op stream is the canonical state and views
are projections of it.

## Does async have to be solved in the core language first?

Open design tension. Parking sits on top of `Ply`, which is the current F#-level
concurrency primitive — and Ply is itself a candidate for replacement. Two readings:

- **Build EventBus on Ply now.** Parking only needs a continuation closure and a
  ready/parked split, both of which Ply already supports. Nothing here forces a core
  language async overhaul, and the Dark-side `Promise<T>` + `!` surface is stable
  regardless of what sits underneath.
- **Settle the core async model first** so we don't bake Ply assumptions into the
  scheduler that a later replacement would have to unwind.

This doc does not decide it. The async/concurrency model — whether and how to
replace Ply, and what the canonical `Promise`/await semantics are at the language
level — belongs in its own design (`design/async.md`). EventBus should depend on the
*interface* that doc settles on, not on Ply specifically.

## Open questions

- **Backpressure** — what if a producer outpaces consumers? Bounded queue, drop, or
  a backpressure signal?
- **Cancellation** — a frame parked on a timeout vs. a resolving event is a race;
  the scheduler can wake a parked frame with a `Cancelled` injection.
- **Ordering** — one producer's events are ordered; across producers, order by global
  timestamp + sequence, with a tie-breaker for the rare collision. Enough for replay
  determinism in the common case.
- **GC** — a frame parked on an event that never fires leaks. Need a default timeout
  plus a way to express "this park may be abandoned at session end."
- **Multi-emit vs single-emit** — `EventBus<T>` is multi-emit; `Promise<T>` is
  single-emit-then-done. Same primitive, two consumption patterns; the type system
  should distinguish them.
- **Dark-side library** — what do `Stdlib.Bus` and `Stdlib.Promise` look like in
  full? Sketch later.

## Tracing as events on the substrate

Traces want to ride this same bus rather than being a parallel F# subsystem.
`Tracing.fs` got reworked heavily during the PDD spike; the corrective is **fewer
F# changes, more exposure via builtins.** Shrink the F# surface to the minimum —
the bus already emits `EvalStepped frame` — and expose trace read/write to Dark
code through `Stdlib.Trace` builtins. Then a trace is not a special artifact; it
is the recorded projection of the `EvalStepped` (and friends) event stream.

The ops-vs-projections lens makes the equivalence exact:

- **A trace is a Msg log.** Per the composable-MVU framing, the viewer's Msg
  stream and the recorded eval-event stream are the *same artifact* folded two
  ways — one into a rendered pane, one into a stored trace. There is no separate
  "trace store" to keep in sync with the bus; the trace *is* a durable fold of
  bus events.
- **Traces are values.** They live in the DB (the content-addressable store, not
  a sidecar), so they are queryable like any other value, diffable, and
  **replayable** — replay is just re-folding the event stream through the same
  scheduler. "Find traces that touched fn X" is a predicate query over those
  stored events.
- **Read and write from Dark.** Because the surface is builtins over the bus, the
  agent, the viewer, and the debugger all read/write traces without new F#
  plumbing — exactly the thin-substrate stance this doc takes for the bus itself.

This is why `EvalStepped` is a first-class event kind above and not an
afterthought: the trace recorder is just another subscriber, and the durable
trace is one more projection of the op stream.

## Related docs

The conflict-resolution dispatch produces `Resolution.Park selector`; the bus and
scheduler handle the wake. The sync layer is a producer/consumer pair on `syncIn` /
`syncOut`, hot-reload publishes on `bodyChanged`, and the composable-MVU viewer is a
large subscriber whose Model is a projection of bus events. The core async model is
deferred to `design/async.md`.
