# Event Bus

The runtime's **coordination substrate**: one typed, append-only, multi-subscriber bus per
event kind. Producers publish; subscribers register a predicate and get called on a match.
Frames that need a not-yet-ready value park *on a bus* — but the parking machinery itself
(the scheduler, `Promise<T>`, force) lives in [async.md](async.md). This doc is just the
bus: what events exist, how they're delivered, and which are durable.

`main` has no push-based multi-subscriber bus today (it has `Stream.fs`, a pull-based data
`DStream` for response bodies — a different, unrelated thing). So this is new substrate.

## What S&S needs vs. what's later

Keep the bus minimal for the syncing goal; the rich ergonomics come after.

| Needed for S&S | Deferred (later ergonomics) |
|---|---|
| Durable `syncIn` / `syncOut` / `conflict` buses | Composition algebra (`map`/`filter`/`zip`/`until`) |
| Persistence model: SQLite-is-truth, signal is ephemeral | `Stdlib.Bus` / `Stdlib.Promise` full Dark library |
| At-least-once delivery + lock-claim (no double-apply) | Tracing-as-events (`EvalStepped` projection) |
| Tiny notifications (ID + metadata; load body locally) | Frame-lifecycle viewer/debugger events |

## Event kinds

Typed, not generic Msgs — the payload rides the type system; subscribers pattern-match.

| Kind | Producers | Subscribers | Durable? |
|---|---|---|---|
| `SyncOpArrived op` | sync layer | op applier, viewer | yes (op tables) |
| `ConflictResolved id res` | conflict dispatch | parked frames, audit | yes (`conflicts_v0`) |
| `CapabilityResolved cap r` | cap dispatcher | parked frames | yes (`capability_log_v0`) |
| `Materialized name body` | a value materializer | parked frames, viewer | no (result is an op) |
| `HumanResponded q answer` | user input | parked frames, viewer | yes (survive restart) |
| `BodyChanged hash` | hot-reload, sync | frames using old body | no (`commits` is durable form) |
| `CostIncurred kind amt` | LLM/network | budget enforcer | no |
| `EvalStepped frame` | interpreter | trace recorder (later) | no |

The durable events (sync ops, conflicts, cap grants) *are* the timestamped op stream;
everything a subscriber renders is a projection of them, not a separate mutable store.

## The F# primitive — thin

The F# side is the minimum LibExecution needs: a bus + publish/subscribe + a wait-step.
Everything ergonomic (named subscriptions, composition) is built in Dark on top.

```fsharp
module LibExecution.EventBus

type EventBus<'T> =
  { subscribers : ConcurrentDictionary<Guid, Subscription<'T>>
    history     : RingBuffer<'T> option        // late subscribers + replay; None when unneeded
    persistTo   : EventPersistence }           // durable buses append on publish

and Subscription<'T> = { id : Guid; predicate : 'T -> bool; handler : 'T -> Ply<unit>; oneShot : bool }
and EventPersistence = NotPersisted | PersistedAsBlob of tableName : string

let publish    (bus : EventBus<'T>) (e : 'T) : Ply<unit> = ...
let subscribe  (bus : EventBus<'T>) (p : 'T -> bool) (h : 'T -> Ply<unit>) : Subscription<'T> = ...
let waitForOne (bus : EventBus<'T>) (p : 'T -> bool) : Ply<'T> = ...   // async.md's scheduler parks here
```

`ExecutionState` gains a `buses` record — one bus per system event kind:

```fsharp
type RuntimeBuses =
  { syncIn : EventBus<SyncEvent>; syncOut : EventBus<SyncEvent>   // the canonical sync wire
    conflict : EventBus<ConflictResolvedEvent>
    capability : EventBus<CapabilityEvent>
    materialization : EventBus<MaterializationEvent>
    humanQuery : EventBus<HumanQueryEvent>
    bodyChanged : EventBus<BodyChangedEvent> }
```

The conflict/cap dispatch *produces* events; the async scheduler *consumes* them by parking
frames via `waitForOne`. The bus knows nothing about frames — it just delivers.

## Persistence — durable vs ephemeral (the load-bearing part)

Most events are ephemeral signals; a few are durable and *are* the sync substrate. The rule
borrowed from classic-dark: **the SQL table is the source of truth; the in-process notify is
just "now would be a good time to check the table."**

```
producer ──publish──► [ durable bus ]──append row──► SQLite op table  ◄── the truth
                            │                              ▲
                            └──signal (ID only)──► subscriber ──loads body from table
```

A durable `publish` appends a row, then signals subscribers with a **tiny notification**
(event ID + metadata); the subscriber loads the body from its local table. Cross-instance,
the SQL table is the synced op stream and the signal is HTTP/WS — same shape, wider reach.

## Delivery semantics

- **At-least-once + lock-claim.** Before invoking a durable handler, grab `claimed_by +
  claimed_at` so two workers don't double-process the same op.
- **Retry is a bus-level policy** (re-enqueue with backoff), not a per-handler concern.
- **Crons fold in:** a tick is just `publish "cron-tick" ()` — no separate infrastructure.

## Precedent — classic-dark QueueWorker (don't reinvent)

Classic-dark shipped this: `LibBackend/EventQueueV2.fs` + `QueueWorker/QueueWorker.fs`
(`docs/eventsV2.md`, May 2022). Its shape carries over: DB-table-as-truth + ephemeral
PubSub signal, tiny notifications, at-least-once with lock claims, named worker handlers
(`Stdlib.Bus.subscribe "materialization" (fun ev -> ...)`). The lesson learned the hard way:
**don't take a custom-infra dependency** — PubSub was GCP-specific and an ops burden.
SQLite-as-truth + in-process notify for v1; cross-instance HTTP/WS later.

What's genuinely new vs. classic: multiple *typed* buses (not one global queue), and that
frames can **park** on an event mid-expression (async.md) — classic had no in-flight pause.

## Later: composition + tracing

Once the S&S core works, the bus grows ergonomics — kept here as a sketch, not built yet:

- **Composition algebra** — `map`, `filter`, `merge (+)`, `zip`, `until`, `first` — so a
  viewer subscribes to `materialization + conflict + humanQuery` filtered to the in-focus fn.
- **Tracing as a projection** — emit `EvalStepped`; a trace is then a *durable fold* of that
  stream, queryable/diffable/replayable like any value, with read/write exposed as
  `Stdlib.Trace` builtins instead of a bespoke `Tracing.fs` subsystem. (The PDD spike grew
  `Tracing.fs` heavily; the corrective is fewer F# changes, more builtin exposure.)

## Open questions

- **Backpressure** — producer outpaces consumers: bounded queue, drop, or a signal?
- **Ordering across producers** — global timestamp + sequence, tie-broken; enough for replay
  determinism in the common case.
- **GC** — a frame parked on an event that never fires leaks (see async.md's timeout).
- **Multi-emit vs single** — a bus is multi-emit; `Promise` is emit-once-then-done (async.md).
