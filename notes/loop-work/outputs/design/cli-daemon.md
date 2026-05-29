# CLI daemon — the long-running host

The original framing of this doc was narrow: a daemon amortizes the ~1 s .NET
cold-start that every `dark` invocation pays. That benefit is real but it is the
*least* interesting reason to build one. The async, sync, and event-bus work has
since produced a set of components that are inherently long-lived — an event bus,
a frame-parking scheduler, open sync connections, warm projections — and none of
them fit the "fresh process per call" model at all. A long-running daemon is the
natural host for all of it. Cold-start savings become a side effect.

This doc reframes the daemon as **the resident process that owns the live
substrate**, and the user-facing CLI as a thin client that talks to it.

## The shift: from optimization to architecture

The per-call CLI is stateless by construction: each `dark <cmd>` boots the
runtime, loads the package tree from `data.db`, does one thing, and exits. That
is fine for a stateless world. But the substrate described in `design/event-bus.md`
and `design/async.md` is **not** stateless:

- The **event bus** is a push-based, multi-subscriber, partly-durable structure.
  Subscribers register interest and get woken on emit. A process that exits after
  one command cannot host a subscriber.
- The **scheduler** holds parked frames — continuations waiting on a promise, a
  conflict resolution, a capability grant, or a human answer. A parked frame that
  outlives a single command (a long materialization, an `await` on a remote op)
  has nowhere to live in the per-call model.
- **Sync connections** (`design/sync.md`) want to stay open: a WebSocket on
  `/sync/live`, or a polling loop against `/sync/events`. Reconnecting per command
  is wasteful and loses push latency.
- **Crons and `start()` daemons** — user-defined background work — have no host at
  all today. A cron tick is `Bus.publish "cron-tick" ()`, which presupposes a
  process alive to receive it.
- **Projections** (the package tree, dep graphs, the conflict view, budget totals)
  are rebuilt from the op stream. Keeping them warm in memory means a query is a
  read, not a replay.

The question is no longer "is the cold-start worth optimizing." It is "where does
the live substrate run." The answer is: a daemon.

## What the daemon hosts

Six responsibilities, roughly in order of how much they *need* a resident process:

1. **The event bus.** One set of typed `RuntimeBuses` (per `design/event-bus.md`),
   resident, with durable buses backed by SQLite and ephemeral ones in memory.
   Subscribers across all logical sessions attach here.
2. **The scheduler.** The `ready`/`parked` split lives in the daemon. Parked frames
   survive across CLI interactions — a frame parked on a slow LLM materialization
   keeps waiting while the user runs other commands.
3. **Sync producers and consumers.** The daemon holds the open connection(s) to
   peers, runs the `/sync/live` WebSocket or the poll loop, and pushes arriving ops
   onto `syncIn` / outgoing ops off `syncOut`. One connection per remote, not one
   per command.
4. **Crons and `start()` background work.** User daemons and scheduled work run here
   on the same bus pipeline — a cron tick is just another publish.
5. **Warm projections.** The package tree and other derived views are built once
   and updated incrementally as ops arrive, instead of replayed per call.
6. **The apps surface** (see [apps-surface.md](apps-surface.md)). Serving running
   Darklang apps (HTTP handlers, the eventual app runtime) needs an always-on host.
   **The apps work likely depends on this daemon** — there is no per-call model for
   "serve an HTTP endpoint."

Cold-start amortization and instant autocomplete (the original doc's whole case)
fall out of items 1 and 5 for free: the client talks to a warm process, so it
never pays boot cost and never re-reads the package tree.

## Ops vs projections

The daemon does not introduce new mutable state. The durable op stream (sync ops,
conflicts, capability grants) remains canonical; everything the daemon holds in
memory — the warm package tree, the parked-frame table, the live projections — is
a **projection** of that op stream, rebuildable on restart by replay. This matters
for the lifecycle story below: a daemon crash loses no durable state, only warm
caches, which it rebuilds. The daemon is a cache and a coordinator, never a source
of truth.

## One daemon per machine, sessions multiplexed inside

The original doc left this open ("per-user? per-rundir? per-machine?"). The
substrate now forces a clear answer.

**Position: one background service per machine (per user account on that machine).
Sessions and branches are logical state multiplexed inside it — not separate
sockets, pidfiles, or processes.**

Reasoning:

- **The event bus is shared by nature.** A sync op arriving for branch B should
  wake frames parked on B regardless of which session triggered them. Sync
  connections are per-remote, not per-session. If each session ran its own daemon,
  each would open its own sync connection and maintain its own bus — duplicating
  the open connections and fragmenting the very coordination the bus exists to
  provide. One bus, many logical subscribers, is the whole point of a push-based
  multi-subscriber design.
- **Branches are filters, not boundaries.** Sync is "per-branch with namespace as
  a filter on top" (`design/sync.md`). A branch is a selector applied to one op
  stream, not a separate world needing its own process. The scheduler keys parked
  frames by event selector; the selector already carries branch identity. Branch
  isolation is a query-time concern, handled by tagging frames and subscriptions,
  not a process boundary.
- **Isolation is achievable in-process.** The against-case worry — bench tasks
  cross-contaminating — is handled by rundir/branch tagging on frames, ops, and
  subscriptions, the same tagging the scheduler already needs. We do not need OS
  process boundaries to keep concurrent agent runs apart; we need disciplined
  selectors, which the bus design already has.
- **Resource cost.** N daemons means N warm package trees, N sync connections, N
  schedulers. One daemon shares all of it. For the concurrency=4 bench case, that
  is one daemon coordinating four logical sessions, not four daemons.

So the filesystem footprint is **one set per machine** (per account):

```
~/.darklang/daemon.sock      # one Unix domain socket — the client connects here
~/.darklang/daemon.pid       # one pidfile — liveness + single-instance guard
~/.darklang/daemon.version   # one version stamp — client/daemon compat check
```

A logical session or branch is identified by a header/handshake field on the
client connection (`session_id`, `branch`), not by a distinct socket. The client
opens the one socket, announces who it is and which branch it cares about, and the
daemon routes its requests and event subscriptions accordingly. This mirrors how
sync already maps a connection to an `account_id` via a handshake rather than a
dedicated endpoint per identity.

The `.version` stamp does real work: a client built against an older protocol must
detect a mismatch and either tell the daemon to restart or fall back to per-call
mode. The single-instance pidfile guard prevents two daemons fighting over the one
socket.

### When per-machine is the wrong grain

Two exceptions worth naming, both narrow:

- **Strong isolation requirements.** If a bench harness needs hard guarantees that
  one task cannot observe another's state (a security or determinism requirement,
  not just hygiene), spawn a daemon per isolation domain with its own socket path
  (`DARK_DAEMON_SOCK=...`). This is opt-in, not the default.
- **Multi-account on one machine.** Different OS users get different daemons
  naturally (different `~/.darklang`). That is per-user, which is the intended
  grain anyway.

## Lifecycle

The daemon's lifecycle is the genuinely new surface area, and the original doc's
caution here was right. Concretely:

- **Start.** Lazy: the first `dark` command that needs the daemon starts it if the
  pidfile is stale/absent, then connects. Explicit `dark daemon start` exists for
  the apps/sync case where you want it up without issuing a command.
- **Connect.** Client opens `daemon.sock`, sends a handshake (protocol version,
  session_id, branch), and gets a routed channel. On version mismatch, fall back
  to per-call.
- **Stop / restart.** `dark daemon stop|restart`. Stop drains: it lets parked
  frames either resolve, persist (durable buses already do), or time out, then
  exits. Restart rebuilds warm projections by replay — no durable loss.
- **Crash recovery.** Because the daemon holds only projections, a crash loses
  warm caches and in-flight ephemeral frames. Durable buses (`conflict`,
  `capability`, `syncIn/Out`, `humanQuery`) persisted their state; on restart the
  daemon replays and resurfaces pending human queries and unresolved conflicts.
  The next client connection transparently restarts a dead daemon.
- **Health.** `dark daemon status` reports liveness, connected sessions, parked
  frame count, sync connection state.
- **Signals.** The daemon must distinguish "stop the daemon" from "interrupt the
  operation this client requested." Client-side CTRL-C cancels the client's
  in-flight request (the scheduler injects `Cancelled` into that session's frames,
  per the event-bus cancellation note); it does not kill the daemon. Killing the
  daemon is the explicit `stop` command or a signal to the daemon pid directly.

## Client/daemon protocol

The client is thin: it serializes a command + context (session, branch, cwd) over
the socket and streams back results and events. This is deliberately close to the
sync wire protocol's shape — a handshake that establishes identity, then a
request/response plus an optional event stream — so the two can share framing.

A command that parks (because it `await`s something not yet ready) does not block
the socket: the daemon returns a "parked" status with a handle, the client can
keep working or subscribe to the wake event, and the result streams when the frame
resumes. This is the per-call model's blocking `readKey` / `File.watchLoop`
problem dissolved — the blocking loop lives in the resident daemon, and clients
get events.

## Fallback: per-call still works

The daemon is an optimization-and-host layer, not a hard dependency for the basic
CLI. A `dark <cmd>` that does not need the live substrate (a pure `view`, a
one-shot `search`) can still run in-process if the daemon is absent or a version
mismatch is detected. What *requires* the daemon is the long-lived surface: serving
apps, holding sync connections open, running crons, and awaiting cross-session
parked frames. The split is clean: stateless reads can go either way; live
substrate needs the resident host.

## Cross-references

- `design/event-bus.md` — the bus + scheduler + parked-frame model the daemon
  hosts; durable-vs-ephemeral persistence; the QueueWorker precedent for
  background work.
- `design/sync.md` — the wire protocol whose framing the client/daemon protocol
  mirrors; the open connections the daemon holds; per-branch-as-filter, which
  justifies one daemon over many.
- `design/async.md` — the core async model the scheduler depends on; the daemon
  hosts the scheduler but should depend on the interface that doc settles, not on
  Ply specifics.

## Open questions

- **Apps runtime coupling.** How tightly does the apps surface bind to the daemon —
  is the daemon the app host, or does it spawn separate app processes it
  supervises? Likely the former for v1, but worth settling once the apps design
  firms up.
- **Idle shutdown.** Should the daemon exit after some idle period to free
  resources, or stay resident for instant response? Probably stay resident when
  sync/apps/crons are active, idle-timeout when only serving CLI calls.
- **Protocol sharing with sync.** How much framing can the client/daemon protocol
  literally reuse from the sync wire protocol versus merely resemble it?
- **Multi-branch in one session.** A session may touch several branches; confirm
  the per-connection branch field is a default rather than a hard scope, with
  per-request override.
