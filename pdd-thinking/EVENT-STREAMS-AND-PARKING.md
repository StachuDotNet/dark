# Event Streams + Parking

The third substrate piece. **What `await` should look like** in a
Darklang runtime that wants to coordinate materialization, sync,
human review, and capability requests under one model — without
F#-level callback soup.

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
