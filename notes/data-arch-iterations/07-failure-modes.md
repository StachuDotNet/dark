# Iter 07 — failure modes and chaos

The previous iterations have been "what's the design when
everything works." This one's the inverse: what breaks, how do
we detect it, what does recovery look like, when is data
genuinely lost.

The orienting principle: **`ops.db` is the only source of truth.
Anything else can be rebuilt.** Failure responses follow that.

## Storage corruption

### `ops.db` itself is corrupt

The doomsday case. SQLite has page-level checksums; corruption
manifests as `SQLITE_CORRUPT` on the next read.

**Detection:** SQLite returns error on any query. Daemon catches
in the read path; logs "ops.db corruption detected"; refuses to
serve writes.

**Response:**
1. Daemon enters read-only mode, surfaces a banner on every CLI
   command: "ops.db is corrupted; recovery options: …"
2. Try `PRAGMA integrity_check` — sometimes corruption is
   confined to a subset of pages and SQLite can tell which.
3. If recoverable: `PRAGMA quick_check` plus dumping rows we
   can read into a new file (`ops-recovered.db`); user manually
   inspects.
4. If not: pull from hub (entire ops history), rebuild ops.db
   from scratch.

**Data loss:** any ops written but not yet synced to hub or
peer. Mitigation: aggressive sync — push every op to hub within
seconds of writing. Then "data loss window" is the last few
seconds of work, recoverable from the user's editor or memory.

**The vault note's "kill-and-fill is OK" general rule** doesn't
apply to ops.db. Make recovery explicit:

```
$ dark recover
ops.db integrity check failed.

Found 23,847 of 24,019 op rows recoverable.
Found 0 of 5 blob rows recoverable. Blobs not in this set will be
re-fetched from hub (12 MB to download).

You appear to have 12 ops written in the last hour that haven't
synced to hub. They may or may not be in the recoverable set;
check `~/.darklang/recovery.log` for details.

Proceed? [y/n]
```

### `pkg.db` (projection) corrupt

Easy case: delete it, daemon rebuilds from ops.db.

**Detection:** SQLite error on read. Or `projection_state.last_op_hash`
points to an op that doesn't exist in `ops.db` (impossible in
normal operation, indicates corruption or bug).

**Response:** delete file, log it, rebuild. ~3s for a normal
branch.

**Data loss:** none. Projection is derived.

### `data.db` (app data) corrupt

App's user data is gone if not opted into the op log
(`LocalDatastore`). Otherwise rebuildable from `ops.db`.

**Detection:** SQLite error on app DB read.

**Response:**
- For `LocalDatastore`: data lost. App restart with a fresh
  data.db; user notified. (LocalDatastore's tradeoff for perf is
  precisely this — no recoverability.)
- For ops-modeled tables: rebuild data.db from `app:<id>` op
  stream. Same code path as projection rebuild.

### Disk full

Can hit during any write — sync pull, projection build, app
trace.

**Detection:** SQLite returns `SQLITE_FULL`. Daemon catches.

**Response:**
1. Refuse new writes. Surface "disk full" on every CLI command.
2. Auto-prune candidates: trace projections older than retention,
   `_archive/` apps, `cache/` directory. Estimate freed bytes.
3. Suggest `dark gc` — daemon does the prune, reports freed.

**Data loss:** none from disk-full alone (writes refused, not
lost). Long-term: if user keeps writing without pruning, eventually
pruning hits "we've already pruned everything and you still need
more." Then user has to free disk externally.

### Filesystem write hole

WAL/SHM out of sync with main DB after kernel crash. SQLite
auto-recovers on next open.

**Detection:** SQLite recovery log on open.

**Response:** automatic. Daemon waits for recovery; logs "WAL
recovery completed in <ms>." Worst-case ~30s for large WALs.

**Data loss:** transactions not yet committed to the main file
roll back. If a write transaction was open at the moment of
crash, that transaction's ops are gone — but they were never
acknowledged to the user, so user retries.

## Network failures

### Hub down

**Detection:** WS connection closes; reconnect fails N times.

**Response:**
- After 3 failed reconnects, mark hub-offline state.
- All daemon operations continue locally — packages, projections,
  apps — they don't depend on the hub.
- Sync pause: incoming push attempts queue locally
  (`sync_queue` table); pulled-from-hub attempts return "offline."
- Retry connection every 60s with exponential backoff up to 30 min.
- CLI banner once per session: "hub offline; working locally."

**Data loss:** none. User keeps working. When hub returns,
queued ops push.

### Hub returns garbage

A hub bug or malicious hub returns malformed frames.

**Detection:** envelope parse fails (wrong version byte, bad
length-prefix, etc.).

**Response:** drop the bad frame; log it; consider disconnecting
and reconnecting if multiple bad frames in a row. If
reconnection still gives garbage, treat as "hub compromised" —
refuse sync, alert user.

```
⚠ Hub returned malformed sync data 5 times. This may indicate
   a hub bug or compromise. Sync paused.
   Run `dark sync --force` to resume against your will.
```

### TLS cert expired

Hub's cert lapsed. Daemon's WS connection refuses to start.

**Detection:** TLS handshake fails with cert error.

**Response:** treat as hub-down. Don't auto-trust expired certs
even with `--insecure` (security boundary; require explicit
admin override). Log "hub cert expired; check hub.dark.run
status."

### Network partition mid-sync

Pull starts, network dies after 800 of 1000 ops.

**Detection:** WS frame timeout or socket close mid-stream.

**Response:** the 800 ops in `ops.db` are valid (each was
content-addressed, signature-verified, INSERTed before the
partition). On reconnect, sync resumes from the last
successfully-applied watermark. Idempotent.

**Data loss:** none.

### Slow connection (timeouts)

User on hotel wifi pushing a 50MB blob. Connection slow but not
broken.

**Detection:** timeouts on individual frames.

**Response:** generous timeouts on initial connect (30s); per-
frame timeouts proportional to expected size. For very large
payloads, chunk and resume.

### Hub split-brain

Different hub backend nodes hold different state (Postgres
replica lag, cache inconsistency).

**Detection:** instance gets a `WELCOME` frame from one hub
node with `last_pulled_op = X` and from another with
`last_pulled_op = Y` after reconnect.

**Response:** trust the higher value (more ops applied).
Reconcile client-side. If hub itself is in a worse state than
the client, the daemon will push ops the hub claims to need.
Idempotent INSERT OR IGNORE handles dedup.

**Data loss:** none if hub eventually converges. If hub is
permanently inconsistent, that's a hub bug — file a hub
incident.

## Authentication failures

### Token expired

Tokens issue with `expires_at`; daemon rotates nightly.

**Detection:** WS handshake returns 401.

**Response:** prompt user to `dark login` again. Until then,
work locally.

```
⚠ Your auth token expired. Working offline.
  Run `dark login` to refresh.
```

### Token revoked retroactively

User revoked a device on `dark.run/account/devices`. Hub returns
403 on the WS handshake.

**Detection:** 403 on connect.

**Response:** lock the daemon — the user explicitly disowned
this instance. Refuse to write new ops. Read-only against the
local snapshot.

```
✗ This instance was revoked at 2026-05-09 04:32 UTC.
  Run `dark login` on a different device or `dark recover` to
  re-enroll this instance.
```

`dark recover` requires a fresh login (proves user identity)
and a new instance token. Old ops authored under the revoked
token might still be in `ops.db`; on re-enrollment, the daemon
optionally re-signs them with the new instance key (configurable;
default is "leave them with revoked-instance signature for the
audit trail").

### Clock skew breaks JWT validation

User's system clock is off by hours. JWT `exp`/`iat` reject.

**Detection:** JWT validation returns "invalid exp."

**Response:** check system clock vs hub's reported time
(WS WELCOME frame includes server_time). If skew > 60s,
warn the user prominently. Continue with token until
hub-side rejection.

```
⚠ Your system clock differs from dark.run by 2h 14m.
  This will cause auth and sync issues. Fix with:
    sudo timedatectl set-ntp true   (Linux)
    System Settings → Date & Time   (macOS)
```

### Account suspended

Hub returns 403 with body `{"reason": "account_suspended"}`.

**Response:** lock daemon harder — refuse all sync. Read-only
against local. User contact required.

## Adversarial

### Malicious peer pushes valid-signature, wrong-content op

A peer holds a write grant. They sign a valid op whose payload
is intentionally crafted to break the projection (e.g., infinite
recursion in a Dark fn definition; circular reference; UTF-8
BOM that breaks downstream parsers).

**Detection:** projection apply throws or hangs (timeout).

**Response:**
- `applyOp` runs in a per-op timeout (~500ms).
- If it times out, the op is marked "poison" in
  `projection_state.poison_ops`.
- Daemon serves projection-as-of-just-before-the-poison-op.
- User notified; option to `dark inspect <op>` and decide.

The grant is still valid (the peer was authorized to write).
But this is suspicious enough to warrant a UI prompt:

```
⚠ Op 7d3f… by feriel-air took >500ms to apply (suspected
   pathological). Projection is paused at the previous op.
  [view] inspect the op contents
  [revoke] revoke feriel-air's grant
  [unblock] apply anyway (might hang the daemon)
```

### Peer pushes ops outside grant scope

Hub's pre-filter (per iter 03) catches this; daemon shouldn't
ever see them. Belt-and-suspenders: on apply, daemon re-checks
grant scope. If mismatch:

**Response:** drop the op, log "peer X attempted out-of-scope
write to (stream, key); rejected." Don't INSERT into ops.db.

### Replay attacks

Same op signature shipped twice. INSERT OR IGNORE on hash
makes this trivially safe.

### DoS via massive op pushes

Peer floods with millions of ops. Designed to fill disk.

**Detection:** sync pull rate exceeds threshold (configurable;
default 10K ops/min/peer).

**Response:** rate-limit per peer. Prompt user "peer X is
sending unusually high volume; [pause sync] [continue]."

### Hub manipulates relayed ops

Hub MITM's ops in flight. Each op is signed by origin_instance;
hub modifies the bytes; signature now invalid.

**Detection:** apply-time signature verification fails.

**Response:** drop the op, log it, alert user. Multiple
signature-fail in a row = hub compromise; lock sync.

### Stolen instance token

Token leaked. Attacker can sign ops as the user.

**Detection:** anomalous origin_instance + content patterns
(hard to detect mechanically). User-facing detection: "weird
ops appearing in my history."

**Response:**
1. User goes to `dark.run/account/devices`, revokes the
   compromised instance.
2. From a trusted device, run `dark grants revoke instance
   <id> --recompute` (per iter 03's revoke modes).
3. The user's own ops continue valid; attacker's ops get
   stripped from projections.

The audit trail (`origin_instance` on every op) is what makes
this recoverable. Without it, "which ops are mine vs the
attacker's" is unanswerable.

## Software failures

### Daemon crash

`darkd` panics, stack trace in log.

**Detection:** systemd / init / supervisor process notices.

**Response:**
- Auto-restart with a 10s cooldown to avoid restart loops.
- After 3 crashes in 60s, give up; surface to user.
- On restart, run "post-crash sanity check": verify ops.db
  consistency, projections valid, sync state coherent.

```
$ dark
✗ Daemon crashed 3 times in 60s. See ~/.darklang/daemon.log.
  Run `dark daemon restart --reset-state` to clear caches.
```

### Daemon hang

Deadlock somewhere — a read holding a writer lock; an `applyOp`
in an infinite loop; an HTTP listener stuck.

**Detection:** daemon's main loop heartbeat misses. (Heartbeat
should be a periodic write to a `daemon_alive` file with
timestamp; CLI checks this on every command. >10s old = dead.)

**Response:**
- CLI returns "daemon unresponsive; force-restart? [y/n]"
- `kill -9` daemon process, clean up socket, restart.

```
⚠ Daemon is unresponsive (last heartbeat 47s ago).
  [r] restart  [d] dump stack via gcore for debugging  [q] quit
```

### Memory leak hits ulimit

Daemon RSS exceeds 90% of `ulimit -v`.

**Response:** preemptive restart. Apps survive (state on disk).
Log "OOM avoidance restart at <ts> with RSS <N>." Spike
mitigation rather than fix; the underlying leak needs
investigation.

### Subprocess apps crashing in loops

Per-app subprocess (slice 9) keeps SEGFAULTing.

**Response:** exponential backoff between restarts. After 5
crashes in 5min, mark app `unhealthy`; daemon refuses to start
it until user runs `dark app restart <name> --force`.

### Schema version mismatch (daemon vs ops.db)

Daemon binary is 0.5.0 but ops.db was written by 0.6.0 (op shape
the daemon doesn't recognize).

**Detection:** `applyOp` returns "unknown payload tag."

**Response:** per iter 01, projection marks "saw an op I can't
apply; rebuild me when you're upgraded." Daemon serves
projection as of the last applicable op. CLI banner: "Some
recent ops require dark >= 0.6.0; run `dark daemon update`."

### Bad payload breaks applyOp permanently

Op payload bytes are valid envelope-level (signature OK,
deserialization succeeds) but applyOp on it throws an unhandled
exception every time.

**Detection:** crash on apply.

**Response:**
- Catch the exception in projection-build.
- Mark op as "poison" in projection_state.
- Surface to user: "op X causes apply to fail; this is a daemon
  bug or corrupted op."
- Skip the op, continue projection build.
- File a daemon bug. (User can also `dark op delete <hash>`
  if they're certain it's bad — drops from ops.db; sync will
  re-pull it from hub if it's still there.)

## Time / state oddities

### Clock skew

Op's `created_at` is in the future ("2030").

**Detection:** any received op with `created_at > now() + 1h`.

**Response:** clamp to now() + 1h for ordering purposes.
Surface a warning: "peer X's clock seems off; their ops will
appear in the recent timeline." Don't reject — clock skew
isn't malicious by default.

### Op older than parent

A child op's `created_at < parent_op.created_at`. Not impossible
(clock running backwards).

**Detection:** during projection apply.

**Response:** ignore the temporal inconsistency. The DAG
structure (parent_op pointer) is what matters; created_at is
just for display.

### Concurrent edits with weird histories

Three devices all edit the same fn within 100ms. We get a
3-way conflict instead of a typical 2-way.

**Detection:** projection apply finds 3 ops sharing the same
parent.

**Response:** the conflicts table records all 3 as conflicting
(per iter 03). User picks one. Auto-LWW picks the latest. Same
machinery as 2-way; just more rows in `conflicts`.

## Hardware

### Disk failing intermittently

Reads sometimes succeed, sometimes return garbage.

**Detection:** SQLite reads return inconsistent results across
queries; `PRAGMA integrity_check` flags errors.

**Response:** treat as ops.db corruption (above). User must
replace disk; recover from hub.

### Out of RAM

Subprocess crash from OOM killer is the most common shape.

**Detection:** subprocess exit code or signal.

**Response:** see "Subprocess apps crashing in loops."

## User error

### Accidentally deleted `~/.darklang/`

User typed `rm -rf ~/.darklang` (oops).

**Detection:** daemon socket gone; `dark` can't find anything.

**Response:** auto-detect first-run state on next `dark`
invocation, repeat the bootstrap (per iter 05). User loses
local-only state (sessions, app data not in op log,
unsynced ops). Synced state recovers from hub via clone.

### Two daemons on same DARK_ROOT

User runs `darkd` directly, then runs `dark` which spawns
another daemon on the same socket.

**Detection:** second daemon's socket bind fails ("Address in
use").

**Response:** second daemon exits gracefully with "daemon
already running on this DARK_ROOT." First daemon keeps
serving.

### Manual SQLite poking

User runs `sqlite3 ops.db` and writes a malformed op directly
into the table.

**Detection:** signature verification fails on next sync (peer
rejects the op).

**Response:** signature is hash-of-envelope-bytes, so a
hand-edited row has an invalid signature. Daemon refuses to
sync it. Locally, it stays in ops.db until user manually
removes. Apply-time signature check catches it for projection.

## Recovery toolbox

The daemon ships these explicit recovery commands:

```
dark recover                  # interactive triage on first failed start
dark recover ops              # rebuild ops.db from sync
dark recover projection <branch>   # rebuild one projection
dark recover all-projections  # rebuild every projection
dark recover app <id>         # rebuild one app's data.db (if op-modeled)
dark daemon restart --clean   # restart daemon, drop in-memory caches
dark daemon dump              # dump in-memory state to JSON for postmortem
dark gc                       # prune retention-eligible data
dark op inspect <hash>        # show one op's metadata, payload preview
dark op delete <hash>         # remove an op from local ops.db (dangerous)
```

All of these should exist before sync ships. Recovery is not
optional.

## Test plan for chaos

A chaos-test harness should exercise:

1. Kill `-9` the daemon during projection rebuild → recover.
2. Inject corrupt page into ops.db → detect + recover.
3. Disconnect network during sync pull → resume on reconnect.
4. Push 1000 random-bytes "ops" through the wire → reject all.
5. Sign an op with a revoked instance key → reject.
6. `created_at = 2099-01-01` → clamp + warn.
7. Concurrent dark commands × 50 against one daemon → no
   corruption.
8. App fn that infinite-loops in `applyOp` → 500ms timeout
   triggers, op marked poison.
9. Cold daemon start with 1M ops in ops.db → measure projection
   build time, confirm < 60s.
10. Disk full mid-write → graceful refuse + recovery on freed
    space.

Run nightly. Failures get filed automatically.

## Recovery story summary

| Failure | Detect | Auto-recover? | Data loss? |
|---|---|---|---|
| ops.db corruption | SQLite error | partial; pull from hub | last unsynced ops |
| pkg.db corruption | SQLite error | yes (rebuild) | none |
| disk full | SQLITE_FULL | with manual gc | none |
| hub down | WS disconnect | yes (queue + retry) | none |
| token revoked | 403 | no (manual re-login) | none |
| poison op | timeout | partial (skip op) | none |
| daemon crash | supervisor | yes (auto-restart) | last in-memory write |
| disk failing | inconsistent reads | no (replace disk) | last unsynced ops |
| network partition | timeout | yes (resume) | none |
| stolen token | user notice | with recompute revoke | varies (depends on attacker) |
| user deletes ~/.darklang/ | first-run path | yes (re-bootstrap) | local-only state |

The pattern: **synced ops survive; everything else is best-effort.**

That's the contract.

## Open questions

1. **Should ops automatically push to hub after every write?**
   (Hot question.) Default-on optimizes for "minimize data loss
   window." Costs: bandwidth, hub load. Default-on with batch
   window of 500ms is probably the sweet spot.

2. **Should the daemon use SQLite's `PRAGMA cell_size_check =
   ON`?** Catches corruption earlier at slight perf cost.
   Probably yes — corruption-detection is a freebie even at 5%
   slowdown.

3. **What about partial-write torn-page protection?** SQLite's
   default journal mode + sync settings handle it. We're
   already using WAL. Worth verifying no one accidentally
   set `synchronous = OFF`.

4. **Clock-skew tolerance**. The 1-hour clamp is arbitrary.
   What's the right threshold? In practice, > 5min skew
   indicates a real problem; warn at 5min, block at 1h.

5. **Recovery UX for non-technical users.** Any of the above
   recovery flows requires at least minor SQL/CLI knowledge.
   Eventually we want `dark.run/account/devices/recover`
   button: "this device looks broken; nuke it and re-clone
   from hub." One-click recovery for the bad case.

## TL;DR

`ops.db` is the only thing we can't lose; everything else
rebuilds. Detection: SQLite errors, timeouts, signature
verification, heartbeats. Response: auto-recover where safe,
prompt the user where it isn't. Data loss is bounded to "ops
written but not yet synced" — keep that window small and
the failure mode shrinks to "nothing important lost."

The chaos-test harness is non-optional; it's the difference
between "we think this is robust" and "we know."
