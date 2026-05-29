# Next steps

The thin successor to the old `READY-WORK` doc. Not a backlog — a launch pad. The
detail lives in the design docs; this says **what to build first and in
what order**, measured against the one goal.

## The goal

> Stachu's `print-md` script lives in Dark. He inspects it, changes it, the changes
> **sync** to his other machines, Ocean can **fork** it, and it shows up under
> **`dark apps`** as an installed app.

Concretely, the near-term target: **two local release builds syncing — one server
(an always-on desktop on the Tailscale network), one client — over a wire that
carries only ops and commits.** See [apps-surface.md](stable-and-syncing/apps-surface.md)
for the end-state and [sync.md](stable-and-syncing/sync.md) for the substrate.

## Priority order

**1. Stable & Syncing (do this).** Everything below points at the two-builds-syncing
target. **2. Removing `.dark` files — punted** (see
[bootstrap.md](removing-dark-files/bootstrap.md); blocked on stable env + working sync).
**3. PDD — resting** ([pdd.md](pdd/pdd.md); spike, not advancing). The
rest (capabilities depth, structural editor, the apps runtime) follows the substrate.

## The Stable & Syncing path

Roughly ordered; the first three can start in parallel.

1. **Tailscale transport + ping/pong.** A `Builtins.Tailscale` package
   (`status --json`, `serve` shell-out, header parsing), then a two-machine
   ping/pong over `https://<peer>.<tailnet>.ts.net`. The single most
   confidence-building first move — it proves the "lean on Tailscale" stance
   end-to-end. ([sync.md](stable-and-syncing/sync.md), [remote-access.md](stable-and-syncing/remote-access.md))
2. **Conflict-dispatch skeleton.** `Conflict` + `Resolution` sum types and a
   `conflictDispatch` field on `ExecutionState`, defaulting to `FailLoudly` — a hook
   that changes no behavior until a policy is installed. Everything (sync, caps,
   runtime errors) routes through it later. ([conflicts.md](stable-and-syncing/conflicts.md))
3. **Event-bus + scheduler core.** The thin F# `EventBus` (publish / subscribe /
   `waitForOne`) plus the parked-frame scheduler. The coordination substrate sync,
   hot-reload, and async all rest on. ([event-bus.md](stable-and-syncing/event-bus.md),
   [async.md](stable-and-syncing/async.md))
4. **Sync read + write.** `GET /sync/snapshot`, `GET /sync/events`, then
   `POST /sync/events` with idempotent apply through the existing op-playback path —
   localhost first. ([sync.md](stable-and-syncing/sync.md))
5. **Identity binding.** A Tailscale-login → account mapping and `dark link
   --tailscale`, so synced ops carry real authorship + intent.
   ([identity.md](stable-and-syncing/identity.md))
6. **Autosync between two of Stachu's machines.** A background pull/apply loop
   (config in the `.darklang` dir, not env vars). This is the **self-sync
   milestone** — the goal's first real proof.
7. **`print-md` as an App + the `dark apps` surface.** Declare print-md as an App,
   install/list/fork via `dark apps`, and get an edit on the desktop to surface on
   the laptop through sync. This is the north star.
   ([apps-surface.md](stable-and-syncing/apps-surface.md),
   [distributed-event-sourcing.md](stable-and-syncing/distributed-event-sourcing.md))

## Recommended first chunk

**The ping/pong (step 1).** It depends only on the Tailscale builtins, is about a
week, and gives visceral proof the transport works. In parallel, the
conflict-dispatch skeleton (step 2) and event-bus core (step 3) are independent
leaf substrate work someone else can start the same day.

## Explicitly not next

- **Removing `.dark` files** — punted until sync + a stable environment exist
  ([bootstrap.md](removing-dark-files/bootstrap.md)).
- **The PDD command surface** — resting; `dark prompt` waits for real implementation.
- **Multi-user / `matter.darklang.com` / public funnel** — after self-sync works;
  approval-as-ops is designed ([sync.md](stable-and-syncing/sync.md)) but deferred.
- **Interactive capability grants** — deliberately deferred
  ([capabilities.md](stable-and-syncing/capabilities.md)); grants are instance-specific for now.

## Open decisions to settle as you build

- Conflict-blind vs. conflict-carrying op-playback (settle on the first real App).
- WIP local-only vs. synced — i.e. which store it lives in
  ([distributed-event-sourcing.md](stable-and-syncing/distributed-event-sourcing.md)).
- Whether the core async model (Ply replacement) lands before or under the event bus
  ([async.md](stable-and-syncing/async.md)).
