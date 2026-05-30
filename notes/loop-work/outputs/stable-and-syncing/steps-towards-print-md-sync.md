# Steps toward print-md sync — the spine

The through-line. Not a backlog — an **ordered list of efforts (PRs)**, each measured against
the one goal, each linking *down* to the design doc (and PR spec) that details it. A future AI
reads this top-to-bottom to execute.

> **Reviewing the whole thing cold? Read in this order:** (1) **this spine** — the plan and the
> floor/vision split; (2) the keystone **`../pre-s-and-s/distributed-event-sourcing.md`** — the
> one model everything reduces to (ops, projections, the thin `App`); (3) the per-effort
> **design docs** this spine links down to; (4) the **PR specs** (`pr-*.md`) for the floor
> efforts, which show the concrete `.fs`/`.dark`/test shape. The floor efforts are what ship
> print-md sync; the `[vision]` ones come after.

## The goal

> Stachu's `print-md` script lives in Dark. He inspects it, changes it, the changes **sync**
> to his other machines, and it shows up under **`dark apps`** as an installed app.

Concrete target: **any member of the Tailscale tailnet syncing — an always-on desktop as the
hub, and any number of clients (Stachu's other machines, Ocean, coworkers) — over a wire that
carries only ops and commits.** Two machines is the *first proof*, not the limit; the design
target is tailnet-wide sync across whoever is on the tailnet. End-state:
[apps-surface.md](../pre-s-and-s/apps-surface.md); substrate: [sync.md](sync.md).

## Floor vs. vision substrate (read this first)

The efforts split in two. The **floor** ships print-md sync and is deliberately git-small — an
ops DB, a wire, a fold. It needs **no scheduler, no event bus, and no always-on daemon**
(autosync can poll). The **vision substrate** (event bus + async scheduler) makes sync *live*
and *composable* — push notifications, cross-session await, hot-reload — but is **not on the
milestone's critical path**. Build the floor first; layer the substrate when the live features
are wanted.

```
 FLOOR — ships print-md sync (git-small; daemon optional, poll-based autosync)
   ops⊥projections ──┐
   conflict=FailLoudly┼─► sync read+write ─► autosync (poll) ─► print-md as an App
   Tailscale transport┘        ▲
                            identity (thin)

 VISION SUBSTRATE — makes it live + composable (after/parallel, not required for the floor)
   EventBus primitive ─┐
   async Stage A ──────┴─► scheduler core ─► push sync · await · hot-reload
```

## The efforts, in order

Tagged **[floor]** (required for print-md sync) or **[vision]** (the live/composable
substrate). Floor leaves can start the same day; the north star (10) is the floor's payoff.

**1. EventBus primitive. [vision]** New `LibExecution/EventBus.fs`; `ExecutionState` gains
`buses` (shared across VMs); **ProgramTypes untouched, no migration** (runtime substrate, not
serialized). The coordination substrate the *live* features rest on — **not** required for the
floor sync path (which just folds ops).
→ spec: [pr-eventbus.md](../pre-s-and-s/pr-eventbus.md) · design:
[event-bus.md](../pre-s-and-s/event-bus.md)

**2. async Stage A. [vision]** Effect metadata on the 9 builtin assemblies + child-VM isolation
+ structured cancellation — the shared prereq for the scheduler (and for concurrent sessions).
Async stays *invisible* at the Dark surface. → design: [async.md](../pre-s-and-s/async.md)

**3. Separate ops from their projections. [floor]** The data-model split the whole design rests on:
make the **op stream canonical** and every view a **regenerable projection**. Physically split
today's single `data.db` into `core.db` (ordered, content-addressed ops + sync coordination)
and per-branch projection caches. *This is foundational and was the missing piece* — sync,
replay, and conflict all assume it. → spec:
[pr-ops-projections.md](../pre-s-and-s/pr-ops-projections.md) · design:
[distributed-event-sourcing.md](../pre-s-and-s/distributed-event-sourcing.md) (Storage)

**4. Conflict-dispatch skeleton. [floor]** `Conflict` + `Resolution` sum types and a `conflictDispatch`
field on `ExecutionState`, defaulting to `FailLoudly` — a hook that changes no behavior until a
policy is installed. Sync, caps, and runtime errors all route through it later.
→ spec: [pr-conflict-dispatch.md](pr-conflict-dispatch.md) · design:
[conflicts-and-resolutions.md](conflicts-and-resolutions.md)

**5. Tailscale transport + ping/pong. [floor]** A `Builtins.Tailscale` package (`status --json`,
`serve` shell-out, `Tailscale-User-Login` header parsing), then a two-machine ping/pong over
`https://<peer>.<tailnet>.ts.net`. The single most confidence-building first move — it proves
the "lean on Tailscale" stance end-to-end. → design:
[tailscale.md](../pre-s-and-s/tailscale.md), [sync.md](sync.md)

**6. Scheduler core. [vision]** The parked-frame scheduler (`ready`/`parked`, keyed by event
selector) on top of (1)+(2) — async Stage C. **await, hot-reload, and *push* sync rest on it;
the floor sync path (poll + fold) does not** — so this comes after the floor works.
→ design: [async.md](../pre-s-and-s/async.md), [event-bus.md](../pre-s-and-s/event-bus.md)

**7. Sync read + write. [floor]** `GET /sync/snapshot`, `GET /sync/events`, then `POST /sync/events`
with idempotent apply through the existing op-playback path — localhost first, then over
Tailscale. The durable `syncIn`/`syncOut` buses (from (1)) flip to persisted here.
→ spec: [pr-sync-read-write.md](pr-sync-read-write.md) · design: [sync.md](sync.md)

**8. Identity binding (thin). [floor]** Just enough to sync safely between *any* tailnet
members: a `Tailscale-User-Login` → account mapping and `dark link --tailscale`, so synced ops
carry real authorship + a structured `Intent`. Scales to N members on the tailnet, not just
Stachu's own devices. Kept minimal and stable in PT; the fuller identity story is deferred.
→ design: [sync.md](sync.md)

**9. Autosync across the tailnet. [floor]** A background **poll**-based pull/apply loop (config
in the `.darklang` dir, not env vars); push is a [vision] upgrade once the bus lands.
Optionally hosted by the core daemon, but a plain cron-style poll works daemon-free. Two of
Stachu's machines is the first proof; the *same* loop syncs any tailnet member against the hub.
**The first real proof of the goal.** → design: [cli-daemon.md](../pre-s-and-s/cli-daemon.md),
[sync.md](sync.md)

**10. `print-md` as an App + the `dark apps` surface. [floor]** Declare print-md as an App,
install/list via `dark apps`, and get an edit on the desktop to surface on the laptop through
sync. The north star. → spec: [pr-print-md-app.md](pr-print-md-app.md) · design:
[apps-surface.md](../pre-s-and-s/apps-surface.md),
[distributed-event-sourcing.md](../pre-s-and-s/distributed-event-sourcing.md)

## Recommended first chunk

**Ping/pong (5)** — depends only on the Tailscale builtins, ~a week, visceral proof the
transport works. In parallel, the other **floor** leaves — **conflict-dispatch skeleton (4)**
and **ops⊥projections (3)** — are independent work others can start the same day. The
**[vision]** substrate (EventBus, scheduler) is deliberately *not* in the first chunk; it
follows once the floor syncs.

## What's punted (and why)

Removing the `.dark` files (needs working sync + a stable env — [bootstrap.md](bootstrap.md));
**public-internet / cross-org exposure beyond the tailnet** (the Tailscale `funnel`) — note
*tailnet-wide* sync across all members **is in scope**, since the tailnet is the trust
boundary; interactive capability grants (grants are instance settings for now); **WIP sync**
(ideal, but we don't yet know how to do it safely).
Each is detailed where it lives; the open *decisions* per effort stay in their own design docs,
not here.
