# Event Streams + Parking

> **v0 design — deepened from sketch via loop T16 (2026-05-20).**
> Grounded in main: `backend/src/LibExecution/Stream.fs` exists
> (~292 LoC) but it's for **data streams** (lazy, single-consumer,
> pull-based — HTTP body iter, file streaming, etc.) —
> *different* from the event-coordination substrate this doc
> describes. The two should not share a name. This doc renames
> the event substrate to `EventBus`.

The third substrate piece. **What `await` should look like** in a
Darklang runtime that wants to coordinate materialization, sync,
human review, and capability requests under one model — without
F#-level callback soup.

## What exists on main — and what doesn't

### `Stream.fs` already exists (but for data, not events)

Confirmed via `git show main:backend/src/LibExecution/Stream.fs`.
It's a lazy, single-consumer, non-persistable `DStream<'a>`
Dval type built on `StreamImpl` (`FromIO | Mapped | Filtered |
Take | Concat`). Pull-based; consumer calls `streamNext` to draw
one element. GC-backed disposer cleans up IO sources.

This is for **data**: HTTP response body iteration, file reads,
generator-shaped builtin returns. Single consumer because draining
is destructive — you can't replay a `Stream` because the source
is consumed.

The event-coordination substrate is *different*:

| Aspect | `Stream.fs` (exists) | EventBus (new, design below) |
|---|---|---|
| Direction | pull-based (consumer pulls) | push-based (producer publishes) |
| Consumers | single | many (broadcast) |
| Replayability | one-shot (consumed = gone) | append-only or replayable |
| Use case | streaming a request body | "materialization done", "cap denied" |
| Lives in | `Dval` (user value) | runtime substrate (system-level) |

**Naming:** the doc previously called this primitive `Stream<T>`,
which collides with main's `DStream`. Renaming to **`EventBus<T>`**.
The Dark-side wrapper (single-shot `Stream.first`-style semantics)
gets called `Promise<T>` per the original sketch.

### What main does NOT have (this doc designs)

- A push-based multi-subscriber bus
- A frame-parking scheduler beyond F#-level `Ply.Ply`
- The Dark-side `Promise<T>` user surface with explicit `!`
  dereference
- Connection between events and the conflict-resolution dispatch
  (T14)

`Ply` is the F# substrate concurrency primitive — every fn returns
`Ply.Ply<Dval>`. Parking is added on top of Ply, not replacing it.

## EventBus — design

Renamed from `Stream<T>` to avoid conflict with main's `Stream.fs`.

Read with `CONFLICTS-AND-RESOLUTIONS.md` (the dispatch model) and
`SYNC-AND-STABILITY.md` (the network application). Conflicts emit
events; parked frames subscribe; sync is just another producer.

## The framing — promises that compose

The vault's async-execution sketch says: **all values are promises;
all promises are dereferenced by an explicit operator** (something
like a trailing `!`). That's the Dark-level surface. Under it:

- A `Promise<T>` is a value that may not be ready yet.
- A frame that *needs* a not-yet-ready promise to make progress
  parks on it.
- The scheduler runs other ready frames in the meantime.
- When the promise resolves (typically because some event-stream
  emitted a value matching a waiter), the parked frame wakes.

**Event streams are the substrate this rests on.** A promise is
just a subscription to "this stream's next value" with one slot.
Streams compose into graphs; promises compose into chains. Same
underlying mechanism, different user-level shape.

## Event streams as a primitive

A `Stream<T>` is typed, append-only, multi-subscriber. Subscribers
register interest with a filter; the stream notifies on each
matching emit.

Concrete event kinds the system needs:

| Kind | Producers | Subscribers |
|---|---|---|
| `Materialized name body` | PDD materializer, LLM call, corpus search | Parked-on-name frames, viewer |
| `CapabilityResolved cap result` | Capability dispatcher, human approval | Parked-on-cap frames |
| `ConflictResolved id resolution` | The B2 dispatch | Parked-on-conflict frames, audit |
| `SyncOpArrived op` | Sync layer | LibMatter applier, viewer |
| `HumanResponded query answer` | User input | Parked-on-question frames, viewer |
| `BodyChanged hash` | Hot-reload, refine, sync | Parked frames using the old body, viewer |
| `FrameParked id reason` | Scheduler | Viewer, debugger |
| `FrameWoken id` | Scheduler | Viewer, debugger |
| `EvalStepped frame` | Interpreter | Trace recorder, viewer |
| `CostIncurred kind amount` | LLM caller, network | Budget enforcer |

These are *concrete event kinds*, not just generic Msgs. The type
system carries the payload; subscribers pattern-match.

## Composition — graphs, not just streams

Streams compose:

- **Map** — `Stream<A>.map(f) : Stream<B>` — transform each event
- **Filter** — `Stream<A>.filter(p) : Stream<A>` — drop non-matching
- **Merge** — `Stream<A> + Stream<A> : Stream<A>` — union
- **Join** — `Stream<A>.zip(Stream<B>) : Stream<(A,B)>` — pair
- **Until** — `Stream<A>.until(Stream<B>) : Stream<A>` — stop on B's emit
- **First** — `Stream<A>.first : Promise<A>` — take one and unsubscribe

Promises are `Stream.first`. That's the bridge between the
stream substrate and the Dark-user-facing promise model.

Graphs emerge naturally:
- The materializer subscribes to `ConflictResolved` filtered to
  conflicts of the kind it can retry
- The viewer subscribes to `Materialized + ConflictResolved +
  HumanResponded` filtered to the in-focus fn
- The budget enforcer subscribes to `CostIncurred` aggregated
  over a session
- The sync layer subscribes to "all PackageOp events" and forwards
  to the remote

## Parking semantics

A frame parks when it tries to read a value that isn't ready.
Concretely:

1. Frame `F` evaluates an expression that needs `Pending(handle)`'s body
2. Body isn't cached → emit `Conflict.PendingUnresolved`
3. Dispatch returns `Resolution.Park(waitOn: Materialized name=...)`
4. Scheduler subscribes `F` to the `Materialized` stream filtered to that name
5. `F` is removed from the ready set; scheduler picks another frame
6. Eventually materializer emits `Materialized name body`
7. Stream notifies subscriber → `F` re-enters ready set
8. Scheduler re-runs `F` from the parked instruction

The parking is **per-frame**, not per-VM. Other frames in the same
VM continue to run. The runtime is concurrent at the frame level.

What happens during the park:
- The frame's register state is preserved as a "continuation"
  (in F# terms, this is essentially the `Ply` continuation)
- The scheduler maintains a wait-list keyed by the event selector
- On wake, the frame resumes mid-instruction (or at instruction
  boundaries — depends on how we model atomicity)

## F# substrate sketch

```fsharp
// In LibExecution.Streams
type Stream<'T> = {
  subscribe : ('T -> Ply<unit>) -> Subscription
  emit : 'T -> Ply<unit>
}

type EventSelector =
  | ByName of FQFnName.T
  | ByConflictId of ConflictId
  | ByCapability of Capability
  | ByCustom of (RTEvent -> bool)

// On ExecutionState
type ExecutionState with
  member val Streams : RuntimeStreams // a record of system streams
  member val Scheduler : Scheduler

type Scheduler = {
  ready : Queue<Frame>
  parked : Map<EventSelector, List<Frame>>
  // tick:
  //   run frame from ready
  //   if frame yields a Park(selector), move it to parked
  //   if any stream emits, scan parked for matching selectors;
  //     move matched frames back to ready
}
```

The F# surface is small. Dark-level code never touches `Stream`
directly — it uses `Promise<T>` (which is just `Stream.first`).
The streams below the promise are how the runtime gets the
coordination it needs.

## Tying back to the other docs

**To conflicts (B2):** a `Resolution.Park` parks a frame on an
event selector. The selector is satisfied by an event emission
that resolves the conflict. The dispatch and the stream substrate
are the same machinery: dispatch decides what event-selector to
park on; streams handle the wake.

**To sync (B3):** sync is a producer on the
`SyncEvent`/`SyncOpArrived` stream. Other parts of the system
(LibMatter applier, viewer) are subscribers. No special sync
plumbing; it's just a stream.

**To PDD:** every `Pending` materialization is a frame parked on
`Materialized name body` filtered to its name. The materializer
fires the event when done; parked frames wake.

**To capabilities (B5):** a cap-denied frame parks on
`CapabilityResolved` filtered to the requested capability.
Human grants the cap → event fires → frame resumes.

**To hot-reload (B6):** when a body changes (refine / sync /
edit), `BodyChanged hash` fires. Subscribers (frames using the
old body, viewer, dependency cache) react.

**To the viewer (B8):** the viewer is a giant subscriber to most
streams. Its Model updates on each event (composable-MVU style —
see B7).

## Compared to the spike's EventSink

The spike had `currentSink : PDDEvent -> unit` — single global,
single subscriber, swallows errors. Useful as a placeholder; the
real thing is what's sketched above:

- Multiple typed streams, not one untyped sink
- Multiple subscribers per stream
- Compositional (map/filter/merge/etc.)
- Backed by the scheduler's wait/wake model so events drive
  parking, not just observability
- Errors propagate (a subscriber's failure can opt-out cleanly;
  silently-swallowed errors were a footgun in the spike)

## Async execution + promises (the Dark-level user surface)

From the vault note: "all values are promises; all promises are
de-referenced by calling a function on them."

What that looks like at the Dark layer:

```dark
let urls : List<String> = ["https://...", ...]
let results : List<Promise<HttpResult>> = urls |> List.map HttpClient.get
let first : HttpResult = (results |> List.head)!     // ! forces
first.body
```

Internally: each `HttpClient.get` returns immediately with a
`Promise`. The frame doesn't park — the value is unresolved but
the frame moves on with the promise as the value. **Only when `!`
is used does the frame park** (on the promise's underlying
stream's first emission).

This makes await-points *explicit* in Dark. No hidden suspension
points. Helps debugging, helps tracing, helps materialization
("I park here, I unpark here").

## Concrete F# shape (the substrate)

### EventBus primitive

```fsharp
// In LibExecution.EventBus (new module)
module LibExecution.EventBus

open Prelude

/// One bus per event kind. Producers publish; subscribers
/// register interest via filter; a publish runs all matching
/// subscribers synchronously (each on its own Ply continuation).
type EventBus<'T> = {
  // Active subscribers, keyed by subscription id.
  subscribers : System.Collections.Concurrent.ConcurrentDictionary<Guid, Subscription<'T>>

  // History buffer for late subscribers + replay. Capped at a
  // configurable size; older entries roll off. NULL for buses
  // that don't need replay (e.g. high-frequency materialization
  // events that the viewer subscribes-and-forgets to).
  history : RingBuffer<'T> option

  // For sync-out: events optionally append to package_ops-style
  // tables for cross-instance replication. Mostly NULL today.
  persistTo : EventPersistence option
}

and Subscription<'T> = {
  id : Guid
  predicate : 'T -> bool                            // selector
  handler : 'T -> Ply.Ply<unit>                     // run on match
  oneShot : bool                                    // unsubscribe after one match
}

and EventPersistence =
  | NotPersisted
  | PersistedAsBlob of tableName : string           // serialize + INSERT

/// Publish: synchronously notify all matching subscribers.
/// One-shot subscribers are removed after their handler runs.
let publish (bus : EventBus<'T>) (event : 'T) : Ply.Ply<unit> = ...

/// Subscribe with a predicate + handler. Returns the subscription
/// id for later unsubscribe.
let subscribe
  (bus : EventBus<'T>)
  (predicate : 'T -> bool)
  (handler : 'T -> Ply.Ply<unit>)
  : Subscription<'T> = ...

/// First-match-only: returns a Ply that completes on the next
/// matching event, then unsubscribes. The frame-parking primitive.
let waitForOne
  (bus : EventBus<'T>)
  (predicate : 'T -> bool)
  : Ply.Ply<'T> = ...
```

### System EventBuses (instances live on ExecutionState)

```fsharp
type RuntimeBuses = {
  materialization : EventBus<MaterializationEvent>
  bodyChanged     : EventBus<BodyChangedEvent>
  conflict        : EventBus<ConflictResolvedEvent>
  capability      : EventBus<CapabilityEvent>
  syncIn          : EventBus<SyncEvent>           // events arriving via /sync/events
  syncOut         : EventBus<SyncEvent>           // events to push out
  humanQuery      : EventBus<HumanQueryEvent>
  frameLifecycle  : EventBus<FrameLifecycleEvent> // for viewer + debugger
}

type ExecutionState = {
  // ...existing fields...
  buses : RuntimeBuses
  scheduler : Scheduler                           // see below
}
```

### Scheduler — the parked-frame wait list

```fsharp
type Scheduler = {
  // Frames currently parked, keyed by what they're waiting on.
  // The key is opaque - it's whatever EventSelector value the
  // dispatch used to park.
  parked : Dictionary<EventSelector, List<ParkedFrame>>

  // Frames ready to run. Picked up by the eval loop.
  ready : Queue<CallFrameId>
}

and ParkedFrame = {
  frameId : CallFrameId
  continuation : Dval -> Ply.Ply<Dval>  // what to do when an event wakes us
  parkedAt : DateTime
  timeout : Option<DateTime>             // if set, frame fails on timeout
}

and EventSelector =
  | ByMaterializedName of FQFnName.T
  | ByConflictId of ConflictId
  | ByCapability of Capability * forAgent: AccountId
  | ByHumanQuery of QueryId
  | Custom of (RTEvent -> bool)         // escape hatch
```

### How parking works (the wake protocol)

Step-by-step when a frame parks:

1. Frame `F` evaluates an expression needing `Pending(handle)` body.
2. Body isn't materialized → `Conflict.PendingUnresolved(handle)`.
3. Dispatch returns `Resolution.Park selector` where `selector =
   ByMaterializedName name`.
4. Scheduler:
   - Records `parked[selector] += { frameId; continuation; ... }`
   - Removes `F` from `ready`
   - Subscribes once to `buses.materialization` with predicate
     "event.name == name"
5. Eval loop picks the next `ready` frame.
6. Eventually materializer publishes `MaterializationEvent { name;
   body }` on `buses.materialization`.
7. The bus's subscription fires; its handler:
   - Looks up `parked[ByMaterializedName name]` → finds `F`
   - Removes `F` from `parked`
   - Calls `continuation body` to build the resumed Ply
   - Adds the resulting frame back to `ready`
8. Eval loop picks `F` up; it resumes from the same call site
   with the materialized body in hand.

The continuation is a closure over the frame's register state at
park time. F# closures + the existing `CallFrame` machinery in
`VMState` make this tractable.

### Dark-side `Promise<T>` and `!`

The user surface is much simpler. From the vault's async note:

```dark
let urls : List<String> = [ "http://...", ... ]
let results : List<Promise<HttpResult>> =
  urls |> List.map HttpClient.get      // returns Promise, doesn't block
let first : HttpResult = (results |> List.head)!   // ! forces; frame parks here
first.body
```

The `!` is the only park-point at the Dark level. It compiles to
`EventBus.waitForOne busForThisPromise (fun ev -> ev.id = thisPromise)`.

Promise internals (F# side):

```fsharp
// In Stdlib.Promise builtins:
type PromiseSlot = {
  id : Guid
  resolved : Dval option ref    // None = unresolved, Some = ready
}

// When a producing builtin returns a Promise:
// - Returns Dval = DPromise slot
// - Internally publishes to a per-promise EventBus<Dval> when done

// When user code writes `myPromise!`:
// - Compiles to a builtin call `Stdlib.Promise.force` that:
//   1. checks slot.resolved (fast-path if already ready)
//   2. otherwise: state.scheduler.park (ByPromise slot.id); ply continues on wake
```

This gives the substrate **explicit await points** in Dark code.
No hidden suspensions. Debugger can see where every frame parks.
Traces show every `!`.

## Persistence question

Some events want to be durable; most don't.

| Bus | Persistence | Why |
|---|---|---|
| `materialization` | not | high frequency; replay would re-trigger LLM calls |
| `bodyChanged` | not directly (the `commits` table is the durable form) | hot-reload doesn't need replay |
| `conflict` | YES (`conflicts_v0` table) | audit + sync |
| `capability` | YES (`capability_log_v0` table) | audit + security |
| `syncIn` / `syncOut` | YES (package_ops / branch_ops) | the canonical form of sync |
| `humanQuery` | YES (new table?) | so a session restart resurfaces pending questions |
| `frameLifecycle` | not | volume too high; viewer subscribes in real-time |

The `persistTo` field on each `EventBus<'T>` controls this. Buses
that persist append a row on every `publish`; replay reads back
from the table.

## Precedent — classic-dark's QueueWorker / EventQueueV2

Classic-dark (`~/code/classic-dark/`) had a real working event
system: `LibBackend/EventQueueV2.fs` (~465 LoC) + `QueueWorker/
QueueWorker.fs` (~381 LoC), per `docs/eventsV2.md` (May 2022).
Worth studying as we design EventBus — much of the shape applies.

### What classic-dark did

- **Two-part system**: durable `events_v2` SQL table is the
  source of truth; Google PubSub is **only** a notification
  channel that says "now would be a good time to check the
  table." DB row is canonical; PubSub message is ephemeral.
- **Notifications are tiny** — just `eventID` + `canvasID` +
  `deliveryAttempt`. Actual event data loaded from DB on
  receive.
- **At-least-once execution** with explicit lock claims
  (`claimLock`) so multiple workers don't double-process.
- **Retry policy**: 5 min later, up to 2 retries, then drop.
- **Worker handlers** are named by Dark users (`WORKER` handler
  with the matching event name) — so subscribing to an event
  stream from Dark code is the *original ergonomic move*.
- **Crons** flow through the same pipeline (`CronChecker`
  enqueues events; QueueWorker processes them uniformly).
- **Per-handler pause/block** without losing already-queued
  events.

### What to borrow for EventBus

The EventBus design above can absorb several of these patterns
**without complicating the F# surface**:

| Classic pattern | EventBus adoption |
|---|---|
| DB table as source-of-truth, ephemeral signal | exactly the `EventPersistence` design: durable buses write to SQLite; the F# subscriber dispatch is the "signal." When matter.darklang.com is the persistence target, the SQL table is shared (synced); the signal is HTTP/WS. |
| Tiny notification payload | When events go cross-instance via sync, ship the ID + minimal metadata; receiver loads from local op tables. Saves bandwidth. |
| Lock claim before processing | For durable buses (conflict, capability, syncIn), grab a row-level "claimed_by + claimed_at" before invoking the handler. Same shape: prevents double-processing in multi-peer setups. |
| Retry policy | Failed handlers get re-enqueued with backoff. Encode as a bus-level policy, not a per-handler concern. |
| Named WORKER handlers (Dark-side) | Dark code subscribes via `Stdlib.Bus.subscribe "materialization" (fun ev -> ...)`. The handler-name *is* the subscription. Reuse the ergonomic shape. |
| Per-handler pause/block | Same: `Stdlib.Bus.pause "materialization"` blocks new handler invocations without losing in-flight or queued events. |
| Crons enqueueing into the same pipeline | A cron tick = `Bus.publish "cron-tick" ()`. Subscribers run on each tick. No separate cron-specific infrastructure. |

### What's new in EventBus vs classic-dark

- **Multi-bus instead of one global queue.** Classic had a single
  pubsub topic per canvas; the new design has typed buses per
  event kind. Better type safety + observability.
- **Composition operators.** Classic had `name == 'foo'` routing
  via WORKER handler name; new design has filter/map/join/zip
  per-stream. (Selectors are richer.)
- **Frame parking.** Classic was for *user-emitted events*;
  it had no concept of an in-flight expression that pauses on
  an event. Parking is genuinely new.
- **Promise<T> + `!` user surface.** Classic exposed `emit` for
  producing + WORKER handlers for consuming. New design adds
  explicit await points at the Dark layer.
- **Cross-instance via sync.** Classic was per-canvas. New design
  spans peers via the wire protocol from SYNC-AND-STABILITY.

### Migration path implication

The Phase 2/3 work to build EventBus is *not* greenfield in
spirit — classic-dark's experience says:

- Don't build a custom infra dependency (PubSub is GCP-specific
  and a real ops burden). Use SQLite as truth + in-process
  notifications for v1; add cross-instance HTTP/WS later.
- At-least-once + lock-claim is the right delivery semantic for
  the durable buses.
- The named-subscriber UX is what users will reach for; keep it.

This precedent makes EventBus feel **incremental, not novel**.
The shape was working on classic-dark; we're translating it to
the new substrate with better typing and better composition.

## Connection to existing main code

### Where to plug in

- `LibExecution.RuntimeTypes` — add `RuntimeBuses` + `Scheduler`
  to `ExecutionState`. New module declarations.
- `LibExecution.Interpreter.fs` — eval-loop tick reads from
  `ready` + writes to `parked` on `Resolution.Park`.
- The conflict-resolution dispatch (T14) is the producer of park
  decisions; the bus + scheduler are the *consumers*.
- `LibExecution.Stream.fs` (existing!) stays unchanged. EventBus
  is a separate primitive.

### Coexisting with Ply

Every fn still returns `Ply.Ply<Dval>`. Park doesn't replace Ply;
it inserts a wait-step *inside* a Ply. A parked frame's
continuation is a `Dval -> Ply.Ply<Dval>` closure — standard Ply
shape. The scheduler dispatches Ply runs from `ready`; the bus
adds to `ready` on event match.

No async/await on the F# side beyond what Ply already provides.

## Connection to other substrate sketches

- **CONFLICTS-AND-RESOLUTIONS (T14)** — `Resolution.Park selector`
  uses an `EventSelector`. The dispatch decides what selector to
  park on; the scheduler handles the wake.
- **CAPABILITIES (T15)** — cap denials surface via the bus to the
  agent's owner (`buses.capability` → `buses.humanQuery`).
- **SYNC-AND-STABILITY** — `syncIn` + `syncOut` carry SyncEvents
  from share-3 / share-5 endpoints.
- **HOT-RELOAD** — `buses.bodyChanged` is the bus hot-reload
  publishes on.
- **COMPOSABLE-MVU** — apps subscribe to buses via the Effects
  channel. MVU Msgs derive from bus events.

## Open questions

- **Backpressure**: what if a producer outpaces consumers?
  Unbounded queue? Drop? Backpressure signal?
- **Cancellation**: does parking support cancel? E.g. a frame
  parked on a timeout vs. resolved-event race. Likely yes; the
  scheduler can wake a parked frame with a `Cancelled` injection.
- **Ordering**: events from one producer are ordered. Across
  producers, ordering is by global timestamp + sequence. Is that
  enough for replay determinism? (Mostly yes; the rare cases get
  a tie-breaker rule.)
- **Persistence**: are events durable (stored in DB) or
  in-memory? Different streams want different answers. SyncEvents
  are durable (we replay them on bootstrap). Eval-step events
  might be ephemeral (or written to trace asynchronously).
- **GC**: when does a parked frame get GC'd? If a frame parks on
  an event that will never fire, we leak. Need a timeout default
  + a way to express "this park can be abandoned at session-end."
- **Multiple-emit vs single-emit streams**: `Stream<T>` is the
  multi-emit shape; `Promise<T>` is single-emit-then-done. Same
  primitive, different consumption patterns. Type system should
  distinguish.
- **Dark-side library**: what's `Stdlib.Stream` look like? What's
  `Stdlib.Promise`? Sketch later.

## Why this is foundational (not just PDD)

Event streams + parking are how the runtime stops being a
synchronous-fail-on-missing model and starts being a
coordination-aware model. Once you have them:

- Async I/O is uniform with materialization is uniform with
  capability requests is uniform with sync is uniform with
  human review
- The runtime can host multiple in-flight workflows in parallel
  on a single VM (no F#-level threading needed at the user level)
- The viewer can subscribe to anything to render anything
- Trace replay is just re-emitting the recorded events into the
  streams

This is the substrate the entire recursive live-development
experience runs on.
