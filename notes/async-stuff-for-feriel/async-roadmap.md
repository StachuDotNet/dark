# Async roadmap — from here to "concurrency works great, tested"

`async.md` is the design (invisible scheduler, `Promise`/park escape hatch, the bus). This is the
**path**: ordered milestones, each with a falsifiable "done." Impl is on hold (Stachu) — this is the map.

## Where we are
- **M0 done, dormant:** the `async-foundation` branch — every builtin carries an `effects` field
  (`Pure | AsyncRead | OrderedIO | …`), its concurrency character. Nothing reads it yet.
- **In the spike (`compose-check`), un-extracted:** `EventBus.fs` (the `waitForOne` bus), `Scheduler.fs`
  (park + the `ParkSet` registry), and the capabilities `RPark` integration.
- **Gate:** EventBus is currently DEFERRED. The scheduler rides it, so un-deferring the bus unlocks
  everything below. (And it's the SAME bus sync-divergence-park rides — build it once.)

## The path (each milestone ships behind a test)

```
M0  effects field          [done, dormant]   builtins declare Pure / AsyncRead / OrderedIO / …
M1  EventBus primitive      ── un-defer ──►   waitForOne(bus, pred): a parked frame IS a subscriber
M2  Scheduler.park          (rides M1)        register/deregister a frame; live ParkSet snapshot
M3  Promise<T> + force       (rides M2)        explicit escape hatch: forcing an unready Promise parks
M4  invisible planner        (rides M2+M3)     overlap independent AsyncRead calls automatically  ← keystone
M5  dark debug park          (rides M2)        "what's parked, on what, since when"
M6  works-great bar          (rides M4)        perf + invisible-by-default + full suite green
```

## Done bars (falsifiable — define done by the artifact, not a vibe)
- **M1 EventBus** — publish wakes exactly the matching subscriber; N subscribers + one event ⇒ one wakes.
  (compose-check's `EventBus.Tests`, 7/7, already prove this.)
- **M2 park** — a frame is in `Scheduler.parked` while waiting and gone on resume (via try/finally); a
  `CCapabilityDenied` policy can `RPark` until a grant event (the caps-gate × scheduler integration).
- **M3 Promise** — `force` on an unready `Promise` parks the frame; `resolve` wakes it; a ready one
  returns inline. The explicit-control surface, shippable before the planner.
- **M4 invisible (keystone)** — two independent `AsyncRead` calls (e.g. two http GETs) run concurrently:
  wall-clock ≈ max(t1,t2), NOT t1+t2, with **no author change**; a dependent chain (`b` uses `a`) stays
  ordered. This is the "the author did nothing special" test.
- **M5 inspect** —
  ```
  $ dark debug park
  frame          waiting on             since
  ──────────────────────────────────────────────
  fetchUser#3    syncIn(opId=a1b2)      1.2s
  render#7       grant(http-client)     0.4s
  ```
- **M6 works-great** — a benchmark: 100 concurrent IO calls finish in ~max not ~sum; existing
  direct-style code compiles + runs identically (the default surface stays invisible); full backend
  suite green. **This is "async works great, tested."**

## Sequencing notes
- **M1+M2 are one effort** — the bus and the scheduler are the same machinery (a parked frame = a
  subscriber); don't split them.
- **Share the primitive with sync** — op-playback + sync-divergence-park ride the same bus. One
  coordination model, many producers; don't build a second.
- **The `effects` field is the planner's only consumer** — `AsyncRead` = safe to overlap, `OrderedIO`
  = keep order. Nothing needs the field until M4, so M0 can sit dormant until then.
- **Ship value before the keystone** — M3 (explicit `Promise`) + M5 (inspect) are useful on their own;
  M4 (the invisible planner) is the ambitious payoff and the main risk. Do M1→M3 + M5 first, then M4.

## Extract / un-defer order
1. Un-defer **EventBus** → its own branch (M1). Gates everything.
2. **Scheduler** (park + ParkSet) on top (M2) + the caps `RPark` wiring.
3. **`Promise<T>`** surface (M3) — the escape hatch, shippable.
4. The **planner** (M4) — keystone; needs the effects field (have it) + the scheduler.
5. **`dark debug park`** (M5) — cheap, high-trust; any time after M2.
