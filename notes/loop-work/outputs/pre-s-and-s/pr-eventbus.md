# PR: EventBus primitive

First concrete spec built from [event-bus.md](event-bus.md), in the PR-spec shape. This is
the foundational coordination substrate; the async scheduler (separate PR) parks frames *on*
these buses. Ships **in-process only** — durable tables come with the sync PR.

**Goal.** `ExecutionState` carries a set of typed, multi-subscriber buses; F# code can
`publish`/`subscribe`/`waitForOne`; nothing about the serialized program changes. After this
merges, a producer can emit an event and a subscriber handler runs — same runtime, one VM or
across VMs.

**Prereqs.** None — this is a leaf. It *unblocks* async Stage C (the scheduler keys its
`parked` map on bus selectors) and the sync PR (durable `syncIn`/`syncOut`).

## .fs changes — the important part

| File (on `main`) | Change |
|---|---|
| `LibExecution/EventBus.fs` *(new)* | The whole primitive: `EventBus<'T>`, `Subscription<'T>`, `EventPersistence`, `publish`, `subscribe`, `waitForOne`. ~120 LoC, no deps beyond `Prelude`. |
| `LibExecution/RuntimeTypes.fs` | `ExecutionState` gains `buses : RuntimeBuses` — placed with the **"set consistently across a runtime"** group (next to `lambdaInstrCache`), because like that cache it is **shared across every VM** spawned under the execution. Add the `RuntimeBuses` record + system event-kind DUs. |
| `LibExecution/Execution.fs` | `createState` constructs one `RuntimeBuses` (all `NotPersisted` for v1) and threads it into `ExecutionState`. No signature change to `execute`. |
| `LibExecution.fsproj` | Add `EventBus.fs` **before** `RuntimeTypes.fs` (RT references the bus types). |

```fsharp
// LibExecution/EventBus.fs
type EventBus<'T> =
  { subscribers : ConcurrentDictionary<Guid, Subscription<'T>>
    persistTo   : EventPersistence }
and Subscription<'T> = { id : Guid; predicate : 'T -> bool; handler : 'T -> Ply<unit>; oneShot : bool }
and EventPersistence = NotPersisted | PersistedAsBlob of tableName : string

let publish (bus : EventBus<'T>) (e : 'T) : Ply<unit> = uply {
  // snapshot matching subs; run each handler guarded (a throwing sub must not
  // break publish — report via ExecutionState.reportException, keep going);
  // remove oneShot subs after firing. (Durable append: deferred to the sync PR.)
  () }
let subscribe (bus : EventBus<'T>) p h : Subscription<'T> = ...    // returns handle to unsubscribe
let waitForOne (bus : EventBus<'T>) (p : 'T -> bool) : Ply<'T> = ...  // a oneShot sub that completes a Ply
```

## ProgramTypes changes — **none, deliberately**

PT is the *serialized AST/type surface*; the bus is pure runtime coordination, never part of
a saved program. So **no `ProgramTypes.fs` / `ProgramTypesToRuntimeTypes.fs` change, and no
package-ref-hash churn** (this PR skips the two-build dance entirely). Stating it explicitly
because it's the cheap, safe way to land the substrate before anything user-visible touches PT.

## SQL/schema — none this PR

All buses are `NotPersisted` for v1, so **no migration**. Durable buses (`conflict`,
`capability`, `syncIn`/`syncOut`) and their tables land with the PRs that need replay — don't
add tables before a consumer reads them.

## Test plan

| Step | Test | Done-signal |
|---|---|---|
| bus delivers | `EventBusTests.fs` (new): subscribe, publish, assert handler ran | handler observed the event |
| predicate filters | publish non-matching + matching; only matching fires | count == 1 |
| oneShot unsubscribes | `waitForOne`, publish twice; second is a no-op | sub gone from dict |
| bad subscriber isolated | a handler that throws; assert `publish` still fires the others + `reportException` called | siblings ran |
| cross-VM | publish in a spawned VM, subscribe in parent; shared `buses` delivers | handler ran (proves shared-state placement) |

## UX touchpoints

None yet — F#-internal. The Dark-facing `Stdlib.Bus` surface is a later PR. A debug peek:

```
$ dark debug buses
syncIn          subs:0   persist:no
conflict        subs:1   persist:no   (parked: 1 frame on ByConflictId 7f3a)
materialization subs:2   persist:no
```

## Risks / problems not yet raised

- **Handler ordering.** `publish` runs matching handlers in dict-iteration order = effectively
  unspecified. If any consumer needs ordered delivery, add a priority/seq field — don't rely on
  insertion order silently.
- **Re-entrancy.** A handler that `publish`es to the same bus during dispatch could recurse.
  v1 rule: snapshot the subscriber list before firing; queued re-publishes run after.
- **Replay safety.** During op-replay, the bus must **not** re-fire expensive producers — replay
  folds recorded results, it does not re-emit `materialization`. (Enforced by the sync PR; called
  out here so the durable-bus design doesn't violate it.)
- **Leak.** A `waitForOne` whose event never fires holds a sub forever → belongs to async's
  park-timeout, but the bus must expose unsubscribe so the scheduler can reap it.

## Above / below

- **Below:** nothing (leaf).
- **Above expects from this:** async Stage C parks on `waitForOne` + needs `unsubscribe`; the
  sync PR flips `syncIn`/`syncOut`/`conflict` to `PersistedAsBlob` and adds the migration +
  the tiny-notification wire format. Both depend only on the signatures frozen here.
