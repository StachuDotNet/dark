# Iter 08 — migration plan in concrete PRs

The rewrite doc § 18 has slices 0-9 in outline. This iteration
puts each slice on a sentence-or-two-of-PR-description footing,
with CI gates, rollback, and rough LOC for each. Goal: someone
who skim-reads this knows what to file as a tracker issue.

## Discipline rules (apply to every slice)

- **Each PR leaves the tree green.** Tests pass. No "we'll
  re-enable this later" — if a test must be skipped, it's
  deleted, with the PR description noting what new test
  replaces it.
- **Each slice has a feature flag where possible.** Default off
  in early commits; flipped on in a separate, small PR. That PR
  is reversible without a full revert.
- **No DB-shape changes without kill-and-fill.** Per the project
  rule we just landed (migrations: schema.sql + hash kill-and-
  fill), schema changes don't migrate row data; user
  re-bootstraps if needed. (For ops.db, "re-bootstrap" means
  "pull from hub" once sync exists, or "re-extract embedded
  snapshot" before then.)
- **Each PR has a rollback path.** Spelled out below.

## Slice 0 — move data.db → ~/.darklang/ops.db

**Goal:** pure rename. Same single SQLite file, new home.

**Diff shape:**
- `LibConfig.Config.dbPath` reads `DARK_ROOT` env var, defaults
  to `~/.darklang/`. Path is `<root>/ops.db` (was
  `<rundir>/data.db`).
- All callers of `Sql.connect` go through a new helper
  `LibDB.Sqlite.connect ConnRef.OpsRW` (this PR introduces the
  ConnRef DU but with only OpsRW/OpsRO populated).
- Test fixture sets `DARK_ROOT=/tmp/dark-test-<gid>/` per test
  process.
- `scripts/run-cli` exports `DARK_ROOT=$REPO/rundir/.darklang`
  for dev compat (so existing reload-packages flows still work
  out of `rundir/`).

**LOC:** ~150 lines added (ConnRef DU + helper), ~60 changed (call
sites). Total ~200.

**CI:** existing tests run unchanged. New test:
`Tests.Bootstrap.darkRootEnvIsHonored` confirms a custom
DARK_ROOT lands files in the right place.

**Rollback:** revert. No data shape changed; `ops.db` IS
`data.db`, just a different path.

**Depends on:** nothing.

## Slice 1 — split traces into separate DB

**Goal:** the `traces` and `trace_fn_calls` tables move from
`ops.db` into a separate `~/.darklang/traces-global.db`. Single
file for now (per-branch comes in slice 3).

**Diff shape:**
- New `traces-global.db` schema (same shape as today's traces
  tables; lives in `migrations/traces-schema.sql`).
- `LibDB.Tracing` opens `ConnRef.TracesGlobal` (new variant on
  the ConnRef DU).
- Existing trace data migrates: a one-time script reads
  `ops.db.traces` + `ops.db.trace_fn_calls`, writes to
  `traces-global.db`, then DROPs from ops.db on next migration
  pass.
- Per the migrations design: bumping `schema.sql` hash forces
  kill-and-fill. So this PR really just deletes the trace tables
  from `schema.sql` and adds them to a new `traces-schema.sql`.
  Tests start clean. Real users lose any local trace history
  (acceptable — traces are ephemeral).

**LOC:** ~100 lines (one new connection ref, new schema file, one
extracted module).

**CI:** `Tests.CliTraces.*` runs against the new connection. New
test: `Tests.Bootstrap.tracesDbCreatedSeparately` verifies the
file exists at the right path.

**Rollback:** revert. Real users lose newly-recorded traces from
post-revert (they ended up in traces-global.db which is now
orphaned). Acceptable.

**Depends on:** slice 0 (DARK_ROOT plumbing).

## Slice 2 — split user_data into apps/_default/

**Goal:** the `user_data_v0` table moves from `ops.db` into
`apps/_default/data.db`. All `Stdlib.DB.*` builtins route to
this single fallback "default app."

**Diff shape:**
- New `apps/_default/data.db` schema. Same `user_data_v0` shape
  for v1; eventually replace with per-table schema (slice 6).
- `LibDB.UserDB.{set, get, query, …}` take a `SqliteConnection`
  arg instead of using the global `Sql.connect`. Caller
  (Builtins.Matter.Libs.DB) passes the default-app connection.
- ExecutionState gets an `appContext : Option<AppContext>` field
  (None today; default-app populated at runtime by the
  daemon/CLI).
- `dbCreate` / `dbListAll` builtins also operate on the
  default app's data.db.

**LOC:** ~250 lines (UserDB.fs gains a connection arg, new app
context plumbing, schema file).

**CI:** `Tests.LibExecution.tests` for `cloud/db.dark`,
`cloud/datastore.dark`, etc. — they should still pass. New
test: `Tests.AppContext.defaultAppDataIsolated` writes via
DB.set, asserts row landed in `apps/_default/data.db`, not
ops.db.

**Rollback:** revert. Users with data in `apps/_default/data.db`
keep it on disk; revert doesn't lose it (next deploy could
re-apply slice 2 and pick up where it left off).

**Depends on:** slice 0.

## Slice 3 — projection DB for main only

**Goal:** introduce `~/.darklang/projections/main/pkg.db`,
populated from ops in `ops.db`. For `main`, route reads through
the projection. For other branches, keep reading from ops.db
directly. Compare results in CI for at least one PR-cycle (a
week of dogfooding).

**Diff shape:**
- New `LibProjection` project: `Build.fs` (replay ops),
  `Open.fs` (LRU + open-or-build), `Schema.fs` (the projection
  DB schema).
- `LibDB.PackageManager.pt branchId`: if branchId == main and
  feature flag `--enable-projection-main` is on, use the
  projection. Otherwise old path.
- Both paths active simultaneously. CI runs every test twice
  (one with flag on, one off) and compares query outputs.
- `LocalExec.Migrations` knows about the new
  `projections/<id>/` directories and how to wipe them on a
  schema bump.

**LOC:** ~1500 lines for the projection builder + LRU + tests
(this is the bulk of the slice).

**CI:**
- All existing tests pass with flag off (no behavior change).
- All existing tests pass with flag on (proves projection
  produces same results).
- A dedicated `Tests.Projection.equivalence` test that runs
  every package query both ways and asserts equality.
- Projection rebuild benchmark — must complete within 5s for
  the seed corpus.

**Rollback:** flip the feature flag off. Both paths still in
the codebase. Once stable in production for 2 weeks, slice 4
removes the old path.

**Depends on:** slice 0.

## Slice 4 — all branches use projections

**Goal:** every branch, not just main, reads through its
own projection. Drop the old code path entirely.

**Diff shape:**
- `LibDB.PackageManager.pt branchId` always uses projection.
- The recursive-CTE branch-chain walk in `Queries.fs`
  retires.
- The `WHERE branch_id IN (...)` filter retires.
- `LibDB.Branches.branchChainCache` retires (replaced by the
  `branch_chain` table in each projection).
- `LibDB.PackageManager.harmfulCache` retires (replaced by
  `deprecations` table in each projection).
- Schema cleanup: drop `branch_id` columns from projection
  tables in ops.db (they live in pkg.db now); drop `commits`,
  `locations`, `package_*`, `package_dependencies`,
  `deprecations` tables from ops.db.

**LOC:** ~600 deleted, ~200 added. Net -400.

**CI:**
- Existing tests must still pass.
- A migration test: take a real `ops.db` with all the soon-to-
  drop tables, run the new schema, confirm projection rebuild
  produces the same data the dropped tables held.

**Rollback:** revert is hard — schema dropped tables. Real
users would need their projections rebuilt from ops; ops.db
is preserved. Effectively the rollback is "stay on the old
binary; don't run the new schema migration."

**Depends on:** slice 3 must be stable in production for 2
weeks.

## Slice 5 — the daemon

**Goal:** introduce `darkd`. CLI becomes a thin RPC client.

This slice is too big for one PR. Sub-PRs:

### 5a — daemon binary scaffolding (no client integration yet)

- New project `backend/src/Darkd/`. Entry point that opens a
  unix socket, prints "hello" to anyone who connects.
- Connects to ops.db for the user it's running under.
- `dark daemon start / stop / status / logs` commands (CLI
  side).
- No actual functional integration yet.

LOC: ~300. Rollback: revert; CLI keeps working in-process.

### 5b — proxy mode

- `dark <cmd>` first checks if daemon socket exists. If yes,
  forward all commands to daemon over RPC. If no, fall back
  to in-process (existing) behavior.
- RPC framing (length-prefixed bincode/JSON).
- Daemon's command handler dispatches to the same code that
  in-process `dark` does today.
- Behind a feature flag: `DARK_USE_DAEMON=1`; default off.

LOC: ~500. Rollback: flag off.

### 5c — projection LRU in daemon

- Daemon holds the projection LRU in memory across requests.
- `LibProjection.Open.openOrBuild` keyed by branchId, cached.
- CLI gets faster on second-and-later invocations.

LOC: ~300. Rollback: flag off; in-process path uses fresh
opens.

### 5d — daemon-as-default

- Flip the default: `DARK_USE_DAEMON=1` everywhere. CLI auto-
  spawns daemon if not running.
- The "run in-process" path (`--no-daemon`) stays, but as an
  escape hatch.

LOC: ~50. Rollback: flip default back.

### 5e — daemon retains long-lived state

- Daemon holds Hub WS connection (for slice 8).
- Daemon holds App supervisor (for slice 6).
- These are stubs in slice 5e; populated in 6 and 8.

**CI gates** (across 5a-5e):
- All existing tests run with daemon mode AND in-process mode
  for at least 5d's review window.
- Latency benchmark: `dark help` cold = ~3s (daemon spawn);
  warm = <100ms.
- Memory benchmark: idle daemon < 200 MB RSS.

**Depends on:** slices 0-4 stable.

## Slice 6 — apps as first-class

**Goal:** `dark app new / start / stop / list / data / logs /
traces`. Each app gets its own `apps/<id>/data.db`.

**Sub-PRs:**

### 6a — App type and registry

- Dark-side `Stdlib.App.App` type.
- Daemon registry (in-memory + persisted to a `apps_meta.db`).
- `dark app new <name>` creates the directory and registers.

LOC: ~400 (mostly Dark).

### 6b — App supervisor

- Daemon spawns an in-process app instance (ExecutionState +
  data.db connection + HTTP listener task).
- `dark app start` / `stop` lifecycle.
- Crash recovery (restart with backoff).

LOC: ~600 (mostly F# subprocess plumbing; see slice 9 for
isolation).

### 6c — Per-app routing for Stdlib.DB

- `ExecutionState.appContext` is populated per-app.
- `Stdlib.DB.set/get` route to the app's data.db, not the
  default fallback.
- `Stdlib.Sqlite.exec` (val.town-style raw SQL) builtin.

LOC: ~300.

### 6d — `serve` deprecation

- `dark serve <router>` becomes a sugar for `dark app start
  --inline`. Old syntax keeps working but with a deprecation
  notice.

LOC: ~50.

**CI:**
- App lifecycle tests (start, stop, restart, crash).
- Two apps running concurrently isolation test (writes from
  one don't show up in the other's data.db).
- Stdlib.Sqlite.exec test — typed and untyped queries.

**Rollback:** apps stay around; revert disables the supervisor.
Apps' data.dbs persist on disk for re-enabling later.

**Depends on:** slices 2 and 5d.

## Slice 7 — sessions

**Goal:** `dark session new / list / continue / kill`.
Sessions stored in `~/.darklang/sessions.db` (single file for
v1).

**Diff shape:**
- New `sessions.db` schema: `sessions` + `session_env` tables
  (already drafted in rewrite doc § 9).
- New CLI commands.
- `DARK_SESSION` env var that the daemon reads to scope
  branch-context per shell.

**LOC:** ~400 (Dark-heavy: most of this is `Darklang.Stdlib.
Session.*`).

**CI:** session create/continue/kill tests. Multi-shell test
(start session in shell A, continue in shell B, confirm
state).

**Rollback:** sessions.db just sits around if revert. New CLI
commands disappear; users go back to per-command `--branch`.

**Depends on:** slice 5d.

## Slice 8 — sync

**Goal:** ops sync between instances via the hub.

**Sub-PRs:**

### 8a — login flow

- `dark login` opens browser, polls hub, gets token.
- Token + instance id stored in `~/.darklang/config.toml`.
- Hub-side: minimal Postgres schema + the WS endpoint stub
  that authenticates and returns WELCOME.

LOC: ~600 (split between dark CLI side and hub-side Dark
code per iter 02; the hub itself is a Dark `App`).

### 8b — sync push

- Daemon pushes new ops to hub on every local write (with
  500ms batch window).
- Hub validates signatures, idempotent INSERT, broadcasts
  to other instances of the same user.

LOC: ~400.

### 8c — sync pull

- Daemon receives ops from hub.
- Verifies, INSERTs into ops.db, triggers projection
  catch-up.
- The "I just made an op on stachu-major; my laptop sees
  it 200ms later" loop closes here.

LOC: ~300.

### 8d — sharing (cross-user)

- `dark share branch <id> with <user> --read|write`.
- Hub-side grants table.
- Per-stream filter on routing (per iter 03).

LOC: ~500.

### 8e — conflict surfacing

- Per iter 04: `dark conflicts`, `dark resolve`.
- Conflict detection in projection-build.

LOC: ~600 (mostly Dark — `Stdlib.Conflicts.*`, CLI commands,
LSP integration).

**CI:**
- Two-instance integration test: instance A writes, instance B
  pulls, confirms equality.
- Conflict test: both instances write to the same key
  simultaneously, both see a conflict, resolution syncs.
- Hub-down test: instance keeps working offline; queues ops;
  pushes when hub returns.

**Rollback:** revert per sub-PR. Each is independently
revertable since they're behind a `sync` feature flag overall.

**Depends on:** slices 0-7.

## Slice 9 — per-app subprocesses

**Goal:** apps that declare `--isolated` (or have crashed too
many times) run in their own .NET subprocess.

**Diff shape:**
- New tiny app-host binary (~30 lines: connect to daemon over
  stdin/stdout, run a Dark fn, ship results).
- Daemon's app supervisor knows two modes (in-process,
  subprocess); routes accordingly.
- Crash isolation test: a SEGFAULT in one app doesn't take
  down the daemon.

**LOC:** ~400.

**CI:**
- A "deliberately crash" Dark fn that isolated apps survive
  but in-process apps would have killed the daemon.

**Rollback:** revert. Apps fall back to in-process.

**Depends on:** slice 6d.

## What CI needs to enforce, regardless of slice

A few invariants the test framework must guard, not slice-by-
slice but globally:

1. **No SQLite path is hardcoded.** Every connection goes
   through `LibDB.Sqlite.ConnRef`. CI grep test:
   `Sql.connect "Data Source=` should appear nowhere.
2. **Test isolation.** Every test creates its own `DARK_ROOT`.
   No global-state leakage. CI: parallel tests must succeed.
3. **Op shape stability.** Adding a payload tag is fine;
   removing or repurposing one is forbidden. CI: golden
   binary serialization fixtures (already exists for
   `Serialization.Binary.Tests`; extend per stream).
4. **Schema drift detection.** Any change to `schema.sql`
   triggers a "you just changed the schema" warning; the PR
   description must say "kill-and-fill expected."
5. **Backward compatibility window.** A daemon at version N
   must read ops written by daemon at version N-1 and N-2.
   Older-than-that is allowed to fail at startup with a clear
   "upgrade required" message.

## Rollback summary table

| Slice | Rollback feasibility | Data loss on rollback |
|---|---|---|
| 0 | trivial (revert) | none |
| 1 | revert | recent traces (ephemeral) |
| 2 | revert | none (data persists) |
| 3 | flag off | none |
| 4 | hard (schema dropped) | none if hub-synced |
| 5a-c | flag off | none |
| 5d | flip default | none |
| 6a-d | revert | apps' state persists |
| 7 | revert | sessions persist |
| 8a-e | per-PR revert | varies (sync-dependent) |
| 9 | revert | none |

The hard one is slice 4 (schema cleanup). Mitigation: stable
2-week window after slice 3 before slice 4 lands.

## Estimated total effort

```
Slice 0:    ~200 LOC,  1 day
Slice 1:    ~100 LOC,  half day
Slice 2:    ~250 LOC,  1 day
Slice 3:   ~1500 LOC,  3-5 days (the big one for projection plumbing)
Slice 4:    ~400 LOC change, 1 day (mostly deletion)
Slice 5:   ~1200 LOC across 5 sub-PRs, 5-7 days
Slice 6:   ~1350 LOC across 4 sub-PRs, 4-5 days
Slice 7:    ~400 LOC, 2 days
Slice 8:   ~2400 LOC across 5 sub-PRs, 7-10 days
Slice 9:    ~400 LOC, 2 days
              ────
Total:    ~8200 LOC, ~30 working days for one engineer
```

This is "concentrated work." Real calendar time depends on
review cycles, dogfooding windows, and how many things break.
~3 months at a steady pace. ~6 months if interleaved with
other work.

## What lands first if we land anything

The smallest valuable slice that proves the architecture: **0
+ 3 + 4**. Together they validate:
- DARK_ROOT plumbing.
- Projection-per-branch as the core unit.
- Schema cleanup as kill-and-fill works.

That's ~2400 LOC over ~5-7 days. After that, daemon (slice 5)
unlocks every subsequent slice.

If we land *only* 0+1+2 we get a tiny architectural
clarification (separated DBs) without any of the user-visible
benefits. Not worth a multi-week project.

If we land 0+1+2+3+4+5, we get: faster CLI, per-branch
isolation, projection rebuilds, daemon. That's the milestone
where the architecture shift starts paying back. Call it the
"Phase 1 milestone."

## TL;DR

Each slice is an independently-shippable PR (or PR family).
Each leaves the tree green. Each has a rollback. The first
landable demo-able thing is "Phase 1" (slices 0-5d), ~3-5K
LOC across 2-3 weeks of concentrated work.

Slice 4 is the only one with a one-way-door element (schema
drop). Stage it after a 2-week stability window on slice 3.
Everything else is reversible.
