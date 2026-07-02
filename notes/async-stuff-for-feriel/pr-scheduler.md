# PR — Scheduler core (async Stage C)

Spine effort 6 **[vision]** — **NOT on the floor's critical path** (poll-based sync folds ops without
it). Design: [async.md](async.md), [event-bus.md](event-bus.md), [async-roadmap.md](async-roadmap.md)
(M2). The parked-frame scheduler: a frame can wait on an event and resume — the substrate **await,
hot-reload, and *push* sync** rest on.

**Goal.** A frame that needs a not-yet-ready value **parks** (registers a wait on the bus that will
carry its event) instead of blocking a thread; when the event publishes, the frame **resumes** where
it left off. Built on the one idea: *a parked frame is just an `EventBus.waitForOne` subscriber*.

**Prereqs.** EventBus (effort 1 — the buses + `waitForOne`) and async Stage A (effort 2 — child-VM
isolation + structured cancellation). **Unblocks:** cross-session `await`, hot-reload, and the **push**
upgrade to autosync (effort 9). Async stays *invisible* at the Dark surface — this is runtime plumbing.

## Already BUILT in prework (`compose-check`) — the park primitive

The selector→bus parking mechanism and the observable park-set exist; `Resolution.RPark` (conflict-
dispatch) already drives them. What's left is the eval-loop integration below.

```fsharp
// LibExecution/Scheduler.fs (prework)
and EventSelector =                          // which bus event a frame is waiting for
  | OnConflictResolved of conflictId : uuid
  | OnOpArrived of opId : uuid               // (grant / promise selectors extend this)

let awaitSelector (buses: RuntimeBuses) (selector: EventSelector) : Ply<unit>   // park on the named bus
type ParkedFrame = { frameId: uuid; selector: EventSelector; parkedAt: DateTime }
type ParkSet = ConcurrentDictionary<uuid, ParkedFrame>                          // the live, observable set
let park (buses) (parkSet) (frameId) (selector) : Ply<unit>                     // register → await → deregister (try/finally)
```

## What THIS PR adds — the ready/parked queues + the eval loop

```fsharp
type Scheduler =
  { ready  : ConcurrentQueue<Frame>      // runnable now
    parked : ParkSet                     // waiting on a selector (from prework)
    buses  : RuntimeBuses }

// the loop: pull a ready frame, run until it parks or finishes; on park, register + yield; on a bus
// publish matching a parked frame's selector, move it ready. Resume restores the frame to the Dval it
// was waiting for (the missing plumbing today: `awaitSelector` returns unit — resume-to-Dval threads
// the published value back into the interpreter's continuation).
let run (s: Scheduler) : Ply<unit> = ...
let wake (s: Scheduler) (e: 'T) : unit = ...   // bus subscriber: matching parked frames → ready
```

The hard part is **resume-to-Dval**: today `awaitSelector` parks and returns `unit`; Stage C threads
the published event's value back as the parked expression's result, so the interpreter continues as if
the value had been there all along (no author-visible suspension).

## Inspect surface — `dark debug park` (roadmap M5, rides this)

```
$ dark debug park
frame          waiting on             since
──────────────────────────────────────────────
fetchUser#3    onOpArrived(a1b2)      1.2s
render#7       onConflictResolved(c9) 0.4s
```

A direct read of `ParkSet` — high-trust, cheap, lands any time after the park primitive.

## Tests / status

- **Park primitive proven** (prework, `ConflictDispatch` RPark path): `awaitSelector` parks on the
  conflict bus — a non-matching publish leaves the frame parked, the matching publish resumes it; the
  `ParkSet` registers on park and clears on resume (try/finally).
- **This PR's bar** (async-roadmap M2): a frame is in `parked` while waiting and gone on resume; the
  ready/parked loop runs N frames where one parks on another's event without deadlock; resume-to-Dval
  returns the published value as the parked expression's result.
- **Keystone follow-on (M4, separate):** the *invisible planner* that auto-overlaps independent
  `AsyncRead` calls rides this scheduler + the `effects` field — the ambitious payoff, its own effort.

## Scope / deferred

Stage C is the **mechanism**; the invisible planner (M4) and `Promise<T>`/`force` (M3) are separate
efforts on top. The floor ships print-md sync **without** any of this (poll + fold) — build it when the
*live* features (push sync, await, hot-reload) are wanted.
