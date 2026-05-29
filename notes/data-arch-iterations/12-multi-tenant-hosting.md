# Iter 12 — Multi-tenant daemon hosting (dark.run)

Most of the architecture iterations have framed the daemon as
something running on the user's laptop or VPS. dark.run flips
that: it's a hosted instance of the daemon, optionally always-
on, that the user can sync into.

This iter pushes on: how does dark.run host thousands of
daemons? What's the isolation model? What's the cost model?
What changes about Dark's design when the data layer is *also*
the cloud layer?

The unique thing about Dark's hosting story is that **dark.run
is a peer, not a server**. The user's data lives on their own
machine; dark.run is just an always-on replica. This is the
opposite of every SaaS: there, the cloud is canonical and the
user's machine has a "view." Here the user is canonical and the
cloud is the view. Implications run deep.

## Isolation: the key question

Three viable models:

### A. Process-per-user

Every active user has a dedicated `darkd` process on dark.run's
infrastructure. Cgroups for CPU/memory; per-user filesystem
sandbox; private SQLite files.

```
host-1: [darkd:user-A] [darkd:user-B] [darkd:user-C]
host-2: [darkd:user-D] [darkd:user-E] [darkd:user-F]
host-3: [darkd:user-G] ...
```

Pros: full kernel-level isolation. One user's bug or runaway
load can't touch another's. Operationally simple — kill -9 to
terminate a misbehaving tenant.

Cons: per-process overhead. If darkd is ~100MB resident, 1000
users = 100GB RAM minimum. Cold-start latency on first request.
Idle users still consume slots.

### B. Shared-process, namespace-isolated

One `darkd` (or a small pool) hosts many users. Every internal
data structure keyed by `accountId`. Sandboxing relies on Dark's
existing language-level isolation.

Pros: dense — 1000 users in 10GB RAM possible. Fast tenant
switching (a context-swap).

Cons: a single bug in F# can cross tenants. One user's hot loop
spike on a shared core (no cgroup boundary). Operationally
fragile — kill -9 takes everyone down.

### C. WASM-style sandboxing

Each user's Dark code runs in a WASM (or similar) sandbox.
Isolation at the WASM boundary. F# is shared infrastructure.

Pros: sub-process-level isolation; can multiplex thousands per
F# host process.

Cons: WASM compilation overhead; Dark's runtime would need to
target WASM; significant new infrastructure.

### Recommendation: **A for v1, C for v2**

Process-per-user is the right starting point. It's
embarrassingly simple to implement:

- Each user gets `~/users/<accountId>/` on the host.
- Spawn `darkd --root ~/users/<accountId>/` when their first
  request lands.
- Run inside a cgroup + namespace.
- Tear down after N minutes idle.

100MB × 1000 active users = 100GB. That's expensive in absolute
terms but cheap relative to everything else (DBs, networking,
human hours). At 1000 paying users at ~$30/mo = $30K/mo
revenue, and the infra cost is well under $10K/mo. Profitable
even with naive process-per-user.

For v2 (if scaling > 10K users), look at WASM. By then the F#
platform has been winnowed down (per iter 02 / iter 11), so the
WASM port is a more contained project.

### Process pooling for cold-start

A per-user process is cheap once running, but cold-start matters
when responding to user requests:

- Hot pool: keep N pre-warmed `darkd` processes ready (no
  account assigned). When a user request comes in for a
  not-running tenant, claim a hot process, point it at the
  user's data dir, run.
- Pool size: 5-10× the QPS of new-tenant activations. Cheap to
  run; saves ~500ms cold-start per request.

This is a classic FaaS warm-pool pattern.

## Resource quotas

Per-tenant limits, enforced at the cgroup level:

| Resource | Free tier | Indie | Team | Enterprise |
|----------|-----------|-------|------|------------|
| CPU      | 100ms/s   | 1s/s  | 4s/s | metered    |
| RAM      | 256MB     | 1GB   | 4GB  | metered    |
| Disk     | 1GB       | 10GB  | 50GB | metered    |
| Bandwidth out | 1GB/mo | 10GB | 100GB | metered  |
| Concurrent reqs | 5    | 50    | 500  | unbounded |
| Op write rate | 10/s | 100/s | 1K/s | unbounded |

Free tier is "good enough for a hobby project." Indie (~$10/mo)
is "good enough for a personal app with light traffic." Team
(~$50/mo) supports a small SaaS. Enterprise is metered, custom
contracts.

Enforcement:

- CPU/RAM via cgroups. Hard limit; OOM-kill on overage. Process
  restarts; user notified.
- Disk via filesystem quota.
- Bandwidth via per-tenant counter; hub WS rate-limits when
  approaching.
- Op rate via daemon-internal counter; daemon refuses ops with
  `429-equivalent` past the limit.
- Concurrent req via daemon-internal semaphore.

## Abuse mitigation

The kinds of abuse to anticipate:

### Op spam

A user's daemon emits 1M ops/sec, filling storage, swamping
sync. Defense:

- Op write rate quota (table above).
- Per-stream rate quota (e.g., trace stream allowed higher rate
  than packages stream).
- Storage cap; refuse new ops past quota.
- Trace-stream specifically: optional sampling at high QPS.

### Compute abuse (crypto, fork-bombs, etc.)

A user runs CPU-heavy code 24/7. Defense:

- CPU quota (table above).
- Per-request wall-clock cap (10s default; configurable per
  handler).
- Anomaly detection on usage patterns: 100% CPU for hours →
  flag for review, throttle by 50%.
- ToS prohibition on crypto-mining; manual revocation if
  detected.

### Network abuse (DDoS amplification, scraping)

A user's Dark code makes 10K outbound HTTP requests/sec.
Defense:

- Outbound HTTP rate limit per tenant.
- Outbound HTTP requires explicit grant in user's app config
  (no surprise calls).
- IP reputation feed: refuse calls to known bad destinations.
- Per-destination rate limit.

### Storage abuse (large blob spam)

A user uploads 1TB of blobs to dump on dark.run. Defense:

- Blob size cap per blob (1GB default).
- Per-tenant storage quota.
- Content-addressed dedup means duplicate uploads are free, but
  unique-blob storage is metered.

### Sync abuse (DDoS the hub)

A user runs 1000 fake daemons all syncing simultaneously.
Defense:

- One active session per account, except with explicit
  multi-device grants.
- Per-session WS connection cap.
- Session establishment rate limit (10/min per account).

## Cost metering

Two ways to charge:

### Tier-based (simpler)

Pick a tier, get its quotas, pay flat fee. Most users prefer
predictable costs.

### Metered (Enterprise)

Pay per:
- CPU-hour: $0.05/hr (rough number)
- GB-month storage: $0.05/mo
- GB egress: $0.10
- Op writes: $0.01 per million

Daemon emits a `usage` op every minute summarizing consumption.
Billing system sums them. User sees a `dark usage` command in
their CLI:

```
$ dark usage --month 2026-05
CPU:      14.3 CPU-hours    × $0.05 = $0.72
Storage:   2.1 GB-months    × $0.05 = $0.11
Egress:    0.4 GB           × $0.10 = $0.04
Ops:      1.2M ops          × $0.01 = $0.01
                                       ─────
Total:                                 $0.88
```

Default to tier-based; enterprise opt-in to metered.

## The key property: **dark.run isn't the source of truth**

This is genuinely different from every SaaS. Implications:

### User retains data sovereignty

If dark.run is hostile, malicious, or just goes offline — the
user has a complete copy of their data on their laptop. No
"export your data" workflow needed; the data already exists
elsewhere.

### Cost is for compute, not custody

We're not charging for the privilege of holding the user's
data. We're charging for running a peer that happens to be
always-on. The data is the user's; we provide compute.

This changes the pitch: "dark.run isn't where your code lives;
it's a copy that runs even when your laptop is closed."

### Egress is free (for the user)

No "export your data" friction. They can run another peer
anywhere, sync, and have the same data.

### Lock-in is structurally limited

The user can stop paying dark.run and self-host. Their data
already syncs to their laptop. Their app's URL might change
(was `myapp.dark.run`, now `myapp.example.com`), but the code
and data is theirs.

This is the OSS-meets-SaaS sweet spot: we sell convenience, not
custody.

### What if dark.run goes offline?

- User's local daemon keeps working.
- User's app, if served by dark.run, becomes unreachable.
- DNS for `*.dark.run` routes elsewhere or fails.
- New ops generated locally queue in the user's pending-sync
  buffer, sync on dark.run's return.

### What if user's local laptop goes offline?

- dark.run's instance has the user's last-synced state.
- Apps continue serving from dark.run.
- New ops generated by dark.run (webhook → handler → DB write)
  sync back when laptop returns.
- If laptop edits clash with dark.run's autonomous edits,
  conflicts surface per iter 04.

## Sync direction subtleties

A user's code is the same on laptop and dark.run. But state
(DBs, traces, sessions) diverges:

- DB writes: most happen on dark.run (apps serve there); few
  happen on laptop (occasional manual ops). Mostly one-way:
  dark.run → laptop.
- Trace writes: only on dark.run (apps don't run on laptop
  during normal flow). One-way: dark.run → laptop.
- Code edits: most happen on laptop; few on dark.run (rare in-
  prod patches). Mostly one-way: laptop → dark.run.
- Session ops: distributed by source.

Sync is bidirectional but flows are imbalanced. The hub knows
this — it can prefetch the dominant direction.

Conflict density: low. Code ops on dark.run while laptop is
also editing the same fn = conflict. Possible, rare, surfaced.
DB writes from laptop and dark.run on the same row = potential
conflict. Resolution per iter 04.

## What dark.run runs that the laptop doesn't

dark.run's daemon is the same binary as laptop's. But its
runtime context is different:

1. **Always-on**. Apps serve continuously.
2. **Public IP / TLS**. Apps reachable on `*.dark.run`.
3. **Shared package store**. Stdlib's blobs are deduplicated
   across all tenants on a host (same content, one storage
   slot).
4. **Hub-aware**. The hub routes incoming WS connections to
   dark.run for users opted into hosted mode.
5. **Metered**. Usage logged at minute granularity.

These are configurations, not different code. A user could run
their own daemon as "always-on with public IP" by configuring
it that way; dark.run is just a managed hosting offering.

## Multi-region

Eventually:

- US-east, US-west, EU, AP regions.
- User picks a "primary" region (where their daemon lives).
- Sync between regions is via the hub (latency is fine for
  ops; not great for inline reads).
- Per-region replicas of read-only public packages (Stdlib,
  popular community packages) on each host.

For v1, single region. v2 is multi-region after revenue
justifies it.

## Observability for ops

dark.run's operators (us) need:

- Per-host: CPU, RAM, disk, network — standard stack.
- Per-tenant: quota usage, anomaly flags, recent-error rate.
- Cross-tenant: total active sessions, op write rate, queue
  depths, failed sync count, hub conn churn.
- Billing: usage rolled up per account per minute.

Implementation: dark.run is, surprise, a Dark app. Tenant ops
sync into dark.run's "operations" account, where dashboards are
built (per iter 09's Dark-driven UIs). Eat our dogfood.

This means our operations stack uses the same projection /
trace / handler machinery as user apps. If our metrics layer
falls over, we feel the same pain users feel.

## Migration: from today's Dark to dark.run hosting

Today's Dark has hosted "canvases" with shared global tables
(`canvases_v0`, `events_v0`, etc.). The new world:

1. Each canvas → an account on dark.run.
2. Canvas's data → its account's ops.db (synced from old DB on
   migration).
3. Canvas's URL → `<canvasName>.dark.run` (configured at
   account level).
4. User's existing local installation gets the same account
   credentials; both peers in the sync mesh.

Migration tool: takes a canvas's existing DB, writes it as a
sequence of ops in the new format, ships to the user's account
on dark.run. One-time, scriptable. Can probably write the
whole thing in Dark.

## Open questions

1. **App-handler concurrency.** A popular app on dark.run gets
   1000 concurrent reqs. Does the daemon parallelize them?
   Currently, F# async + ExecutionState per request — should
   scale to thousands without issue. Bottleneck is the SQLite
   write lock (for handlers that mutate DBs); WAL mode helps
   but isn't unlimited.
2. **Per-app daemon vs per-account daemon.** An account hosts
   multiple apps. Are they in one daemon process, or one each?
   Recommendation: per-account daemon hosts all the account's
   apps, with per-app sub-isolation (each app's handlers in its
   own ExecutionState).
3. **Live-attach across the hub.** From a user's local REPL,
   `:attach my-blog --on dark-run` attaches to their hosted
   daemon. WS-tunneled JSON-RPC. Latency 50-200ms (same as
   any internet call).
4. **Multi-account on one machine.** A user has personal +
   work accounts. Local daemon supports both? Yes — daemon
   reads `~/.darklang/accounts/`; each account has its own
   data dir; one daemon process serves all of them with
   account-context per request.
5. **Pricing for the hub itself.** Sync uses bandwidth; hub
   relays connection negotiation. We pay for that even when
   users don't use dark.run hosting (they just sync between
   their own machines through the hub). Cap: free tier gets
   10GB hub traffic/month; paid plans more. Direct P2P
   (Tailscale-style) bypasses the hub for paid users who
   configure it.
6. **DDoS on dark.run apps.** A user's app gets attacked.
   Default rate limits per IP at the dark.run edge; per-app
   captcha/Cloudflare-style DDoS protection upgrade for paid
   tiers.
7. **Region failover.** If `us-east-1` goes down, do we
   failover the user's daemon to `us-east-2`? Requires their
   ops.db replicated across regions. v1 answer: sync to laptop
   is good enough; if dark.run is down, app is down. v2:
   active-passive replicas in adjacent region.

## TL;DR

- Process-per-user with hot pool for v1; WASM sandbox for v2.
- Cgroup-enforced quotas; tier-based pricing default; metered
  for enterprise.
- The unique property: **dark.run is a peer, not a server**.
  User retains data sovereignty; we charge for compute, not
  custody.
- Bidirectional sync between laptop and dark.run; conflicts
  rare and resolved per iter 04.
- dark.run is itself a Dark app — eats its own dogfood.

The pitch: "dark.run is a copy of your code that runs even when
your laptop is closed." Not "where your code lives." That's the
structural difference from every SaaS, and it's the unique
selling point.
