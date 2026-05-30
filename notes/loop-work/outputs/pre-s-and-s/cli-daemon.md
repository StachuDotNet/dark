# CLI daemon — hosting the live substrate

A per-call `dark <cmd>` boots the runtime, loads the package tree from `data.db`, does one
thing, and exits. The *live* substrate from [event-bus.md](event-bus.md) and
[async.md](async.md) wants a resident host instead. This doc splits into two parts: (1) generic
long-running-daemon support in the CLI, and (2) which daemons actually exist and how they're
shaped.

`main` has no daemon today (no `dark daemon`, no socket host) — this is all new surface.

## The floor is daemon-free; the daemon is opt-in

To be **as universal as git and sqlite, the boring local path must work with no daemon at
all** — per-call, in-process, like `git` and `sqlite`. That floor is *complete*: edit, run,
search, and even **poll-based sync** (`dark sync` as a plain command / cron) need no resident
process. The daemon is a strictly **opt-in** layer you add for the *live* features — push
sync, cross-session await, hot-reload, warm-projection speed, hosting apps/crons. So:

| Works daemon-free (the floor) | Wants the daemon (the vision layer) |
|---|---|
| edit / run / search / `view` | live **push** sync (vs poll) |
| **poll-based sync** + print-md syncing | cross-session `await` / parked frames |
| one-shot commands | hot-reload; warm projections (speed) |
| | hosting HTTP apps, crons, `start()` daemons |

Nothing below makes the daemon a *dependency* of the floor — it's an accelerator and a host
for the live surface, never the only way to use Dark.

---

# Part 1 — Long-running daemon support

## Why a resident process at all

Four things can't live in a process that exits after one command:

- **The event bus** — push-based, multi-subscriber, partly durable. A subscriber needs a
  process to live in.
- **The scheduler** — parked frames (continuations awaiting a promise, a conflict, a human
  answer) must outlive the command that spawned them.
- **Long-lived external connections** — anything holding an *open* socket (push sync's
  WebSocket is the headline consumer). Poll-based sync doesn't need this — it's a periodic
  command — which is exactly why the floor can sync daemon-free.
- **Crons + `start()` background work** — a cron tick is `Bus.publish "cron-tick" ()`, which
  presupposes a process alive to receive it.

Warm projections (the package tree, dep graphs) are a bonus: held in memory, a query is a
read, not a replay — and the ~1 s .NET cold-start the original optimization chased vanishes
as a side effect.

## Daemons are just Apps

**A long-running daemon is an [App](apps-surface.md) with a background loop** — nothing new
to manage. It shows up in `dark apps`, and start/stop/status are the *app* surface, not a
parallel "daemon manager." This keeps the user model small:

```
$ dark apps
NAME          KIND      STATUS
core          daemon    running   (bus, scheduler, routing)
print-md      app       idle
my-http-api   daemon    running   :8080  (3 handlers)

$ dark apps stop my-http-api      # same verb as any app
$ dark apps status core
core  running  pid 4821  sessions:2  parked-frames:1  uptime:3h
```

So "daemon lifecycle" = "app lifecycle for apps that happen to run a loop." The only
daemon-specific surface is the host plumbing below.

## Lifecycle + client/daemon protocol

The user-facing CLI is a **thin client** that talks to a resident host over a socket:

- **Start.** Lazy — the first command that needs a daemon starts it if absent; explicit
  `dark apps start <name>` for the always-on case.
- **Connect.** Client opens the socket, sends a handshake `(protocolVersion, sessionId,
  branch)`, gets a routed channel. On version mismatch → fall back to per-call.
- **Stop/restart.** Drains: parked frames resolve, persist (durable buses already do), or
  time out; restart rebuilds warm projections by replay — no durable loss.
- **Crash recovery.** The daemon holds only projections, so a crash loses warm caches and
  ephemeral frames; durable buses replay on restart and resurface pending conflicts/queries.
  The next client connection transparently restarts a dead daemon.
- **Signals.** Client CTRL-C cancels *that client's* in-flight request (scheduler injects
  `Cancelled` into its frames) — it does not kill the daemon. Killing is explicit.

```
thin client ──socket──► resident daemon ─┬─ event bus + scheduler (parked frames)
  serialize cmd+ctx     handshake routes │─ warm projections (package tree, ...)
  stream results+events  by session/branch└─ background loops (crons, app handlers)
```

A command that parks doesn't block the socket: the daemon returns a `parked` handle, the
client keeps working or subscribes to the wake event, and the result streams on resume. The
blocking `readKey`/`watchLoop` problem dissolves — the loop lives in the daemon, clients get
events.

**Not a fallback — the floor:** per-call in-process is the *default*, not a degraded mode. The
whole floor (edit, run, search, **poll-based sync**, print-md syncing) runs daemon-free. Only
the live surface (push sync, background loops, open connections, cross-session parked frames)
*requires* the host. The daemon accelerates and extends; it is never a dependency of the floor.

---

# Part 2 — The daemon topology

Not one monolith per machine, and not one per session. Instead: **a core coordinator plus
per-app daemons it supervises** — matching "an instance per app; a core instance coordinates
the rest, that's its only job."

```
            ┌──────────────── core daemon (one per machine/account) ───────────────┐
            │  owns: the event bus · the scheduler · routing · external connections │
            │  job: coordinate everything + drive sync.  Holds no app logic.        │
            └───────────────┬───────────────────────────┬──────────────────────────┘
              supervises     │                           │
            ┌────────────────▼─────┐            ┌────────▼──────────────┐
            │ app daemon: my-http  │            │ app daemon: a warm    │
            │ serves :8080         │            │ projection / cron host│
            └──────────────────────┘            └───────────────────────┘
```

- **The core daemon** — one per machine (per account). Owns the single event bus, the
  scheduler, request routing, and the long-lived external connections. Its *only* job is
  coordination + sync; it holds no app-specific logic. One bus, many logical subscribers is
  the whole point of a push-based design — N cores would duplicate connections and fragment
  coordination.
- **Per-app daemons** — each long-lived App (an HTTP handler host, a warm projection, a cron
  host) runs as its own daemon-App, spawned and GC'd by the core. Most are temporary; some
  stick around. This is where "each app has its own DB / instance" lands
  ([distributed-event-sourcing.md](distributed-event-sourcing.md)).
- **Sessions and branches are logical filters**, multiplexed inside via the handshake fields
  — not separate processes. A branch is a selector on one op stream; the scheduler already
  keys parked frames by selector, so branch isolation is query-time tagging, not a process
  boundary.

Filesystem footprint — one core set per account; app daemons register with the core:

```
~/.darklang/core.sock       # the one socket clients connect to
~/.darklang/core.pid        # liveness + single-instance guard
~/.darklang/core.version    # client/daemon protocol compat check
```

### When per-machine is the wrong grain

- **Strong isolation** (a bench harness needing hard guarantees one task can't observe
  another): opt-in separate core via `DARK_DAEMON_SOCK=...`. Not the default.
- **Multi-account** on one machine: different OS users get different `~/.darklang` naturally.

## Open questions

- **App-daemon supervision.** Does the core spawn app daemons as child processes it
  supervises, or host them as in-core background loops? Lean: in-core loops for v1, separate
  processes only when an app needs isolation or its own resource budget.
- **Idle shutdown.** Stay resident when sync/apps/crons are active; idle-timeout when only
  serving stateless CLI calls.
- **Multi-branch per session.** The per-connection `branch` is a default, not a hard scope —
  confirm a per-request override.
