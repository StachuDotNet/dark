# Steps toward print-md sync — the spine

The through-line. Not a backlog — an **ordered list of efforts (PRs)**, each measured against
the one goal, each linking *down* to the design doc (and PR spec) that details it. A future AI
reads this top-to-bottom to execute.

## The goal

> Stachu's `print-md` script lives in Dark. He inspects it, changes it, the changes **sync**
> to his other machines, and it shows up under **`dark apps`** as an installed app.

Concrete near-term target: **two local release builds syncing — one always-on desktop on the
Tailscale network, one client — over a wire that carries only ops and commits.** End-state:
[apps-surface.md](../pre-s-and-s/apps-surface.md); substrate: [sync.md](sync.md).

## The dependency picture

```
 foundations (pre-S&S, parallelizable)        sync (S&S)              north star
 ┌─ EventBus primitive ─┐                                                          
 ├─ async Stage A ──────┼─► scheduler core ─┐                                      
 ├─ ops⊥projections ────┼───────────────────┼─► sync read+write ─► autosync ─► print-md
 ├─ conflict dispatch ──┘                    │      ▲                  as an App
 └─ Tailscale transport ─────────────────────┘   identity (thin)                  
```

## The efforts, in order

Foundations (1–5) are pre-S&S leaves — the first ones can start the same day. Sync (6–9)
builds on them. (10) is the north star.

**1. EventBus primitive.** New `LibExecution/EventBus.fs`; `ExecutionState` gains `buses`
(shared across VMs); **ProgramTypes untouched, no migration** (runtime substrate, not
serialized). The coordination substrate everything rests on.
→ spec: [pr-eventbus.md](../pre-s-and-s/pr-eventbus.md) · design:
[event-bus.md](../pre-s-and-s/event-bus.md)

**2. async Stage A.** Effect metadata on the 9 builtin assemblies + child-VM isolation +
structured cancellation — the shared prereq for the scheduler. Async stays *invisible* at the
Dark surface. → design: [async.md](../pre-s-and-s/async.md)

**3. Separate ops from their projections.** The data-model split the whole design rests on:
make the **op stream canonical** and every view a **regenerable projection**. Physically split
today's single `data.db` into `core.db` (ordered, content-addressed ops + sync coordination)
and per-branch projection caches. *This is foundational and was the missing piece* — sync,
replay, and conflict all assume it. → design:
[distributed-event-sourcing.md](../pre-s-and-s/distributed-event-sourcing.md) (Storage)

**4. Conflict-dispatch skeleton.** `Conflict` + `Resolution` sum types and a `conflictDispatch`
field on `ExecutionState`, defaulting to `FailLoudly` — a hook that changes no behavior until a
policy is installed. Sync, caps, and runtime errors all route through it later.
→ design: [conflicts-and-resolutions.md](conflicts-and-resolutions.md)

**5. Tailscale transport + ping/pong.** A `Builtins.Tailscale` package (`status --json`,
`serve` shell-out, `Tailscale-User-Login` header parsing), then a two-machine ping/pong over
`https://<peer>.<tailnet>.ts.net`. The single most confidence-building first move — it proves
the "lean on Tailscale" stance end-to-end. → design: [sync.md](sync.md)

**6. Scheduler core.** The parked-frame scheduler (`ready`/`parked`, keyed by event selector)
on top of (1)+(2) — async Stage C. Sync, hot-reload, and await all rest on it.
→ design: [async.md](../pre-s-and-s/async.md), [event-bus.md](../pre-s-and-s/event-bus.md)

**7. Sync read + write.** `GET /sync/snapshot`, `GET /sync/events`, then `POST /sync/events`
with idempotent apply through the existing op-playback path — localhost first, then over
Tailscale. The durable `syncIn`/`syncOut` buses (from (1)) flip to persisted here.
→ design: [sync.md](sync.md)

**8. Identity binding (thin).** Just enough to sync safely between Stachu + coworkers: a
`Tailscale-User-Login` → account mapping and `dark link --tailscale`, so synced ops carry real
authorship + a structured `Intent`. Kept minimal and stable in PT; the fuller identity story
is deferred. → design: [sync.md](sync.md)

**9. Autosync between two of Stachu's machines.** A background pull/apply loop (config in the
`.darklang` dir, not env vars), hosted by the core daemon. **The self-sync milestone — the
goal's first real proof.** → design: [cli-daemon.md](../pre-s-and-s/cli-daemon.md),
[sync.md](sync.md)

**10. `print-md` as an App + the `dark apps` surface.** Declare print-md as an App,
install/list via `dark apps`, and get an edit on the desktop to surface on the laptop through
sync. The north star. → design: [apps-surface.md](../pre-s-and-s/apps-surface.md),
[distributed-event-sourcing.md](../pre-s-and-s/distributed-event-sourcing.md)

## Recommended first chunk

**Ping/pong (5)** — depends only on the Tailscale builtins, ~a week, visceral proof the
transport works. In parallel, **EventBus (1)**, **conflict-dispatch skeleton (4)**, and
**ops⊥projections (3)** are independent leaf substrate work others can start the same day.

## What's punted (and why)

Removing the `.dark` files (needs working sync + a stable env — [bootstrap.md](bootstrap.md));
multi-user / public funnel (after self-sync works); interactive capability grants (grants are
instance settings for now); **WIP sync** (ideal, but we don't yet know how to do it safely).
Each is detailed where it lives; the open *decisions* per effort stay in their own design docs,
not here.
