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
Async stays *invisible* at the Dark surface. → spec:
[pr-async-stage-a.md](../pre-s-and-s/pr-async-stage-a.md) · design: [async.md](../pre-s-and-s/async.md)

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

> **Merge-sequencing note** (verified in prework by composing two leaf branches): the
> LibExecution-type leaves — EventBus, async Stage A, conflict-dispatch — are *logically*
> independent but each inserts at the **same two spots** (the `RuntimeTypes.fs` and-chain just
> before `ExecutionState`, and `createState` right after `notify`). So landing several produces
> **trivial keep-both textual conflicts** (the `ExecutionState` *fields* auto-merge; only the
> type-blocks + `createState` collide) — ~2-min resolutions, no semantic conflict. Land them in
> any order; just expect a quick keep-both at merge, or rebase each on the prior.

## Getting sync working LOCALLY — the shortest path to a running demo

The engine is built + tested (prework): `opsToSend`/`opsSince` (read), `applyRemoteOps`
(idempotent insert+apply), `snapshot` (bootstrap cursor), `SyncCursors` (resume), the
cross-store op-log transfer (two real SQLite files), and the blob content channel
(`Blob.missing`/`getMany`). What's left is the **transport + two-store orchestration**. The one
hard constraint: **LibDB binds a single global connection per process** (`LibConfig.Config.dbPath`)
— so two stores means either two *processes* (each its own `data.db`) or the in-process
`connStore` seam (the connection-parameterized fold, on `compose-check`). That gives a 3-rung
ladder, simplest-runnable first:

1. **In-process convergence proof — DONE** *(`compose-check`, LibPmSeam)*. The test
   *"dispatchVia (connStore connB) folds AddFn + SetName — B resolves the name to the hash"*
   folds a sender's `AddFn` + `SetName` into a *separate* store B via `connStore`/`dispatchVia`,
   then asserts **store B resolves the folded name to the same hash as A**. That's the
   "a receiver actually runs the sender's code" proof, in-process, already green. The convergence
   engine is proven; rungs 2–3 only change where the ops come from.
2. **CLI local-file pull — DONE end to end** (only a live interactive demo remains). The full
   chain is built + validated: **`dark sync pull <peer.db>`** (`cli/sync.dark`, registered in
   core) → **`pmSyncPull` builtin** (`Builtins.Matter/Libs/PM/Sync.fs`, caps `{FileSystem}`) →
   **`Sync.pullFromFile`** (resume per-peer cursor → `Sync.pull` → persist cursor) →
   **`Sync.pull`** reads the peer's op log directly and applies via the same `insertAndApplyOps`
   the wire receiver uses — writing the op **log** (receiver becomes a re-serving peer) *and*
   folding projections, idempotently. Tested: a real two-file `pullFromFile` test (apply a peer
   log, cursor = op count, cursor persisted, re-pull resumes/no-ops, **and a peer blob the local
   lacks is fetched** — SyncIdempotency 11/11); the command + builtin parse/compile/reload cleanly.
   `pullFromFile` is complete: ops (log + projections) + **content blobs** (`Blob.missing` → copy)
   + persisted cursor, no deferred TODO. This is the first *user-facing* sync — "edit on A,
   `pull`, see it on B" on one machine.
   > **VALIDATED LIVE — content moves between two real instances.** Run a second instance on its
   > own DB (`DARK_CONFIG_DB_NAME=peerB.db`, a copy of the seed), author a value there
   > (`dark val Stachu.Demo.syncVal… = 42L` → ✓ Created), and on the local instance:
   > `dark view …` → *Not found*; `dark sync pull rundir/peerB.db` → *caught up through op 9853*
   > (peerB's 2 extra ops: AddValue + SetName); `dark view …` → **`val syncVal… = 42L`**. The
   > value authored on B is now resolvable on A. The full chain runs end to end:
   > `Cli.Sync.execute` → `pmSyncPull` → `pullFromFile` → `pull` → `insertAndApplyOps` (+ blob
   > fetch + persisted cursor; a re-pull resumes as a no-op). (Gotchas: the peer path must be
   > reachable *inside the devcontainer* — use a mounted dir like `rundir/`, not host `/tmp`; a
   > package location needs 3 parts owner.module.name.)
3. **HTTP localhost → tailnet — BUILT; the SSRF blocker is FIXED; live demo pending env.** The
   full chain is coded + loads: the **server** (`Darklang.Sync.Server.router`, a `/sync/events`
   GET → `pmSyncOpsSince` base64 wire batch, served via `dark serve`) and the **client**
   (`dark sync pull <url>` → `httpClientGetUnsafe` → `pmSyncApplyWire`), over the 13/13-tested
   payload codec (`encodeBatch`/`applyWireBatch`).
   > **SSRF blocker FIXED.** `Stdlib.HttpClient`'s `defaultConfig` blocks loopback, RFC-1918,
   > link-local — **and `100.64.0.0/10`, the Tailscale/CGNAT range** — so the guarded client
   > can't reach a localhost *or tailnet* peer. Landed **`httpClientGetUnsafe`** (a builtin with
   > its own `BaseClient.create looseConfig`, no SSRF guards — `looseConfig`'s own comment
   > describes exactly this trusted-CLI→trusted-server case; the tailnet is the trust boundary).
   > `dark sync pull <url>` now uses it. Builds + reloads clean.
   > **Live cross-machine demo still pending — blocked by the headless env, not the code:** the
   > in-container `dark serve` process runs, but its readiness can't be probed headlessly (no
   > `ss`/`netstat`; its stdout is block-buffered under `run_in_background` so "Listening on…"
   > never flushes), so the puller races server-startup. Needs a controlled run (TTY server +
   > a readiness probe, or a published port) — the *code* is in place (May-14 `httpServerServe`
   > on :9876 proves the HTTP server itself works). The same `pmSyncApplyWire` path applies the
   > body; the tailnet is only the transport. Core sync is already proven by the rung-2
   > cross-instance demo (a value moved between two instances).

Each rung reuses the rung below's apply path; only the *source of ops* changes (a second store →
a local file → an HTTP body) — exactly the keystone's "the fold is the same, only where ops come
from differs." Rung 1 is pure F# integration; rung 2 adds a CLI command; rung 3 adds the Dark
HTTP handlers (the only piece needing a live environment).

## What's punted (and why)

Removing the `.dark` files (needs working sync + a stable env — [bootstrap.md](bootstrap.md));
**public-internet / cross-org exposure beyond the tailnet** (the Tailscale `funnel`) — note
*tailnet-wide* sync across all members **is in scope**, since the tailnet is the trust
boundary; interactive capability grants (grants are instance settings for now); **WIP sync**
(ideal, but we don't yet know how to do it safely).
Each is detailed where it lives; the open *decisions* per effort stay in their own design docs,
not here.
