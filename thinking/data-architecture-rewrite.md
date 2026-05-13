# Data architecture rewrite — master ops DB + per-projection SQLite + a CLI daemon

A wild, opinionated proposal. Favors fresh ideas over old. Where this conflicts with prior plans (LibCloud-as-cloud, the `data.db` monolith, the rebuild-the-PT-PM-on-every-`dark`-invocation lifecycle), this proposal wins.

---

## Thesis (one paragraph)

We have, today, a single SQLite file (`rundir/data.db`) that mixes four very different kinds of state: append-only ops, derived projections of those ops, user-program data, and traces. They have wildly different access patterns, lifetimes, conflict semantics, and replication needs, and crushing them into one file forces the worst of every world (one writer lock, one schema, one migration story, one backup unit, one sync unit). The proposal: split data along its actual seams. **One** master "ops" DB per device — append-only, content-addressed, syncable, the source of truth — and **many** projection DBs that are kill-and-fill rebuilds of the master, indexed for the workload they serve. Per-branch projection DBs solve the "live editing while another agent works on a different branch" problem and unlock per-branch live processes. Per-app user-data DBs (val.town-but-better) solve the data-isolation problem. A **shared CLI daemon** owns the projection DBs and serves the actual `dark` invocations (and the Dark-language `serve`/`http-server`/cron/worker processes), so we stop paying CLI-startup tax on every command and stop fighting per-process SQLite contention.

Roughly:

```
~/.darklang/
  ops.db           ← master, append-only, the only thing that syncs
  projections/
    <branch-uuid>/
      pkg.db       ← rebuildable from ops.db + branch chain
      traces.db    ← per-branch trace store
  apps/
    <app-id>/
      data.db      ← user-program data, owned by the app
      assets/
  daemon.sock      ← long-running F# process, owns the cache
  daemon.pid
  config.toml
```

The rest of this document defends and details that picture.

---

## 1. Where we are now (concrete)

This is the code as of `misc-bwdserver-aot-etc-squashed`. I'm writing it down so the rest of the doc has something to push against.

**One DB, many concerns.** `LibConfig.Config.dbPath = "{runDir}/data.db"`. `LibDB.Sqlite.connString` builds a single connection string off it. Every caller — `LibDB.Inserts`, `LibDB.Queries`, `LibDB.UserDB`, `LibDB.Tracing`, `LibDB.Branches`, `LibDB.PackageOpPlayback`, `LibDB.BranchOpPlayback` — opens that connection. WAL mode, `synchronous=NORMAL`, `busy_timeout=5000`, `Cache=Private`, `Pooling=true`. Every test wipes the same file. `migrations/` runs every schema change against this same file.

**Tables, by what they really are:**

| Table | Kind | Source-of-truth? | Rebuildable? |
|---|---|---|---|
| `package_ops` | append-only ops | yes | n/a |
| `branch_ops` | append-only ops | yes | n/a |
| `branches` | derived metadata | no, derived from `branch_ops` | yes |
| `commits` | derived metadata | no, derived from `package_ops` w/ commit-hash + `branch_ops` | yes |
| `package_types` / `package_values` / `package_functions` | projections | no | yes |
| `package_blobs` | content-addressed blobs | yes (referenced from rt_dval) | no, but content-addressed |
| `locations` | branch-scoped name → hash | no, derived | yes |
| `package_dependencies` | derived | no | yes |
| `deprecations` | derived (per redesign) | no, derived from ops | yes |
| `user_data_v0` | user-program data | yes | no |
| `traces` / `trace_fn_calls` | observability | yes-ish (lossy is fine) | no, but ephemeral |
| `toplevels_v0` | legacy: HTTP handlers + DBs | mostly dead post-bwdserver-strip | n/a |
| `scripts` | scripts table | side data | n/a |
| `system_migrations_v0` | schema bookkeeping | yes | no |

Two columns are doing all the multi-tenancy work: `branch_id` on `locations` / `package_ops` / etc., and `commit_hash` (NULL = WIP, otherwise a committed commit). Branch-aware reads use `getBranchChain` (a recursive CTE), then a `WHERE branch_id IN (...)` filter on every read.

**Caches that paper over the shape.** `LibDB.Branches.branchChainCache` (ConcurrentDictionary, never invalidated), `LibDB.PackageManager.harmfulCache` (ConcurrentDictionary, branch-keyed, never invalidated), the in-memory PackageManager cache (`LibDB.Caching`). Both files have inline TODOs admitting the cache only works because the CLI exits. From `Branches.fs:191`:

> CLI lifecycle masks this (process exits between commands); server lifecycle wouldn't. Drop the cache or wire invalidation on MergeBranch / RebaseBranch.

That's a giant flag for "the architecture only works because we don't have a daemon yet." We're proposing the daemon. Time to stop pretending.

**Lifecycle.** `dark <cmd>` boots a fresh .NET process every time, pays AOT/ReadyToRun startup cost (down from 8s → ~1s per the binary-size notes, still real), reads tens of thousands of rows to warm the PT package manager (or rather, lazily reads them through `withCache`), runs your one command, and exits. Watch logs show this — `cli.log`, `packages.log`, `migrations.log`, `build-server.log`. The fact that `branchChainCache` / `harmfulCache` work without invalidation is exactly the smell of "every invocation is a new process."

**Sync.** Doesn't exist (was ripped out; see Sync notes — was reintroduced as a planning doc but isn't in the code). The ops table has the right append-only shape for it; everything else is in the way.

**No app concept.** `serve <router-path>` blocks the CLI. There is no notion of "a long-running thing called X with its own data." The only durable state a user program has is `Stdlib.DB.set`/`get` against `user_data_v0`, which is process-global (no scope per app, just per-`tlid`).

**No session concept.** None. Vault talks about it. Code does not.

That's the baseline.

---

## 2. The pains, ranked

I'll rank these by how often they bite, not by how interesting they are.

1. **Branch chain cache and harmful cache are stale-prone.** Today they "work" because each CLI run is a fresh process. Any long-running process — a daemon, a `serve`, a sync agent — sees the staleness. Either we ship invalidation (we know how to spell it but it's threaded code with a footgun on every Merge/Rebase/Archive) or we sidestep it. Per-branch projection DBs sidestep it: the PM cache is the projection DB itself, and "invalidate" is "drop and rebuild that one DB." Cheap because branches are small and branches that aren't being edited don't get rebuilt.

2. **CLI startup tax compounds with workflow.** Every `fn` / `run` / `tree` / `status` is a fresh process. Build-time tax on F# we've fought (ReadyToRun, custom binary serialization, single-file publish), but startup tax × commands-per-minute is the dominant felt latency. A daemon makes the second `dark` call near-free.

3. **One writer lock for "what I'm editing" + "the trace from my last `run`" + "the script that wrote 50k rows of user data" + "the package_op that just landed via sync."** SQLite WAL helps, but a single DB makes long writes block short writes more than necessary. Worse: rebuilds, bench scripts, and `pm-sweep-blobs` all contend with everything else.

4. **No isolation between apps.** A user's HTTP `serve`'d app and another user's cron job share `user_data_v0`. (Multi-instance was nuked when scope_id went away; that was the right call for now, but per-app is the next step.) Today an app's "DB" is a Dark-language `Datastore<T>` plus `tlid`-scoped rows in one giant table. That doesn't scale, makes backup-per-app impossible, and hides the val.town-style "every script gets a SQLite" affordance behind a leaky abstraction.

5. **Migrations are global.** A schema change to `traces` or `trace_fn_calls` walks all of `data.db`, even though traces are ephemeral. Three migrations in this repo (`20260424_000000_merge_trace_fn_tables.sql`, `20260427_000000_trace_fn_calls_redesign.sql`, `20260501_000000_trace_fn_calls_duration.sql`) are exactly the "we're churning trace shape" pattern, and they make every `localexec migrations run` slower than it should be. Per-projection DBs let traces churn freely without the rest of the world caring.

6. **Sync needs ops alone.** When sync comes back, the only thing that should travel between devices is `package_ops` + `branch_ops` + `package_blobs` referenced therein. If those are isolated to their own DB file, sync becomes "ship rows from one append-only table to another" — the simplest possible protocol. The current monolithic file makes "what to sync" a column-level filter on every table. That's not a hard filter to write, but it's the wrong shape; you want a file-level boundary, because the sync target gets to drop its projection layer and rebuild from ops without negotiating.

7. **Tests do `kill-and-fill` of the whole DB to isolate.** Every test integration. We have a memory rule allowing kill-and-fill freely for the deprecation redesign — that rule is really general. Single-file kill-and-fill is fine for one DB, brutal for "wipe my user-data while preserving the package tree." Splitting cleanly means you can wipe just `apps/<X>/data.db`.

8. **No long-running processes.** `serve` blocks. A worker can't run alongside the CLI editing the worker's code. You can't `tail -f` a trace from one terminal while iterating on the handler in another. We need a daemon to host these things. The daemon needs a stable unit of state — a "branch projection DB" is that unit.

9. **The tracing migration treadmill, again.** See `Tracing.fs` (594 lines). Each redesign costs the whole DB a migration. With a `traces.db` per branch, the redesign is "drop and recreate." Every single trace-related migration in the repo would have been one-liner with a versioned DB file.

So: **split, daemon, sync ops only.**

---

## 3. The architecture, in one diagram

```
                ┌─────────────────────────────────────────────────────────┐
                │                  device (one tailnet node)              │
                │                                                         │
                │   ┌─────────────┐     ┌────────────────────────────┐    │
   another     │   │  ops.db     │ ──→ │ darkd  (long-lived F#)     │    │
   device  ←──→│   │ (append-    │     │   ├─ branch projection LRU  │    │
   (push/pull  │   │  only,      │     │   │   ├─ <branch>/pkg.db    │    │
   ops only)   │   │  master)    │     │   │   └─ <branch>/traces.db │    │
                │   └─────────────┘     │   ├─ app supervisor        │    │
                │         ▲             │   │   └─ <app>/data.db     │    │
                │         │             │   ├─ sync pull/push        │    │
                │         │             │   └─ http control plane    │    │
                │   ┌─────┴───────┐     └────────────────────────────┘    │
                │   │  dark CLI   │ ←── unix socket / loopback HTTP       │
                │   │  (thin RPC) │                                        │
                │   └─────────────┘                                        │
                └─────────────────────────────────────────────────────────┘
```

The CLI becomes a thin RPC client. The daemon is the engine. The master DB is a journal. Projection DBs are caches. Apps are supervised processes with their own DBs.

---

## 4. The master DB (`ops.db`)

This is the only DB that matters in the long run. Everything else can be deleted and rebuilt. Schema is brutally minimal:

```sql
-- Master ops DB — the only source of truth, the only thing that syncs.

CREATE TABLE package_ops (
  hash      TEXT PRIMARY KEY,        -- content-addressed; full hex, not truncated
  op_blob   BLOB NOT NULL,           -- LibSerialization.Binary blob
  branch_id TEXT NOT NULL,           -- which branch this op belongs to (logically)
  parent_op TEXT,                    -- prev op on this branch; NULL if first
  created_at INTEGER NOT NULL,       -- unix millis; not used for ordering
  origin_device TEXT NOT NULL        -- which device first saw this op (for diagnostics, not auth)
);
CREATE INDEX idx_pop_branch ON package_ops(branch_id, created_at);
CREATE INDEX idx_pop_origin ON package_ops(origin_device);

CREATE TABLE branch_ops (
  hash      TEXT PRIMARY KEY,
  op_blob   BLOB NOT NULL,
  created_at INTEGER NOT NULL,
  origin_device TEXT NOT NULL
);
CREATE INDEX idx_bop_created ON branch_ops(created_at);

-- Content-addressed blob storage. Rare/large stuff (embedded JSON literals,
-- pre-computed RT.Dval representations of values, embedded binary) lands here.
CREATE TABLE blobs (
  hash      TEXT PRIMARY KEY,
  length    INTEGER NOT NULL,
  bytes     BLOB NOT NULL,
  created_at INTEGER NOT NULL
);

-- Sync ledger — what each peer has, last we knew.
-- Used as a bandwidth optimization, not a correctness guarantee.
CREATE TABLE sync_state (
  peer_id        TEXT PRIMARY KEY,
  last_pulled_op TEXT,
  last_pushed_op TEXT,
  last_seen      INTEGER
);

-- Device identity — written once, by `dark init`.
CREATE TABLE device_identity (
  device_id    TEXT PRIMARY KEY,    -- UUID, generated locally
  display_name TEXT NOT NULL,       -- "stachu's laptop"
  created_at   INTEGER NOT NULL
);
```

Notes:

- **PRIMARY KEY is the full content hash.** The current code truncates to 16 bytes for UUID compat (`Inserts.fs:20-24`); kill that. We're not running out of UUIDs, but truncation makes "is this op the same op I already have" a probabilistic question, and that's not a question I want sync to need to ask. TEXT primary key on a hex hash is fine; SQLite indexes it as a B-tree just fine.

- **No `applied` column.** The current code has a two-phase "insert with applied=false, apply, mark applied=true" pattern (`Inserts.fs:38-104`). That makes sense when "insert" and "apply" are in the same DB and you want crash recovery. With `ops.db` separate from `pkg.db`, "applied" lives in the projection DB instead — see § 5.

- **No `commit_hash` column on ops.** Today `package_ops.commit_hash` is NULL = WIP, NOT NULL = committed. That's a join we can do: a commit is just a `BranchOp.CreateCommit { hash, message, branch_id, op_hashes: List<Hash> }`, and which ops "belong to" the commit is recoverable from that op alone. Drop the redundancy, eliminate a class of inconsistency bugs. Same goes for `propagation_id` — make propagation an op shape and stop carrying batch IDs as columns.

- **No `branches` / `commits` / `locations` / `package_*` / `package_dependencies` / `deprecations` tables.** Those are projection tables. They live elsewhere.

- **No `user_data_v0` / `traces*` / `toplevels_v0` / `scripts`.** Those aren't ops; they don't sync; they don't belong here.

- **`origin_device` is not auth.** Auth comes from the transport layer (per the Tailscale notes: `Tailscale-User-Login` header on the sync endpoint). `origin_device` is for diagnostics — "where did this op first appear" — and helps detect "you have an op signed by a peer you don't recognize" without needing per-op signatures.

That's it for the master. Five tables. The whole file should be well under 100 MB even for power users; you can `tailscale file cp` it. You can also `litestream` it to S3 if you want a backup. You can hand it to someone on a USB drive (per the `dl-sync.md` note in the vault).

### Migrations on `ops.db`

Append-only. **The schema above never changes.** New op shapes inside `op_blob` are fine — those are deserializer concerns, and we already have the binary serialization with content-addressed hashing pattern down (`LibSerialization.Hashing.computeOpHash`).

If we *do* ever need to change the schema (say, add a `parent_op` index variant, or a `signature` column), it's a one-line `ALTER TABLE` on a small file. Not the global migration treadmill we have now.

This is the closest the proposal gets to "kill-and-fill is fine" not applying — the master DB is the thing we *don't* kill-and-fill. Everything else we can.

---

## 5. Branch projection DBs (`projections/<branch-id>/pkg.db`)

This is the read-side. Per branch. Built lazily; held in an LRU in the daemon; can be deleted at any time.

```sql
-- One file per branch. Path: projections/<branch-uuid>/pkg.db

-- Materialized branch chain (this branch + parents up to main).
-- Single row, written on rebuild. Avoids the recursive CTE on every lookup.
CREATE TABLE branch_chain (
  position INTEGER PRIMARY KEY,   -- 0 = self, 1 = parent, ...
  branch_id TEXT NOT NULL
);

-- High-watermark: the last op (from ops.db) we've folded into this projection.
-- "Bring this projection up to date" = "play forward from here."
CREATE TABLE projection_state (
  k TEXT PRIMARY KEY,
  v TEXT NOT NULL
);
-- well-known keys:
-- 'last_op_hash'      → last package_op hash applied
-- 'last_branch_op_hash' → last branch_op applied
-- 'schema_version'    → schema of THIS projection (not the master)
-- 'built_at'          → unix millis

-- Same shape as today's projection tables. Indexed for the read patterns
-- in LibDB.Queries.
CREATE TABLE locations (
  location_id TEXT PRIMARY KEY,
  item_hash   TEXT NOT NULL,
  owner       TEXT NOT NULL,
  modules     TEXT NOT NULL,
  name        TEXT NOT NULL,
  item_type   TEXT NOT NULL,
  branch_id   TEXT NOT NULL,
  commit_hash TEXT,                    -- NULL = WIP
  created_at  INTEGER NOT NULL,
  deprecated_at INTEGER NULL
);
CREATE INDEX idx_loc_lookup ON locations(branch_id, owner, modules, name, item_type)
  WHERE deprecated_at IS NULL;
CREATE INDEX idx_loc_module ON locations(owner, modules)
  WHERE deprecated_at IS NULL;

CREATE TABLE package_types (
  hash TEXT PRIMARY KEY,
  pt_def BLOB NOT NULL,
  rt_def BLOB NOT NULL
);
CREATE TABLE package_values (
  hash TEXT PRIMARY KEY,
  pt_def BLOB NOT NULL,
  rt_dval BLOB,
  value_type BLOB
);
CREATE INDEX idx_pv_type ON package_values(value_type);
CREATE TABLE package_functions (
  hash TEXT PRIMARY KEY,
  pt_def BLOB NOT NULL,
  rt_instrs BLOB NOT NULL
);

CREATE TABLE package_dependencies (
  item_hash       TEXT NOT NULL,
  depends_on_hash TEXT NOT NULL,
  depends_on_item_type TEXT NOT NULL,
  depends_on_owner TEXT,
  depends_on_modules TEXT,
  depends_on_name  TEXT,
  PRIMARY KEY (item_hash, depends_on_hash)
);

-- Branch-local computed flags (replaces the harmfulCache).
CREATE TABLE deprecations (
  item_hash TEXT PRIMARY KEY,
  kind      TEXT NOT NULL,              -- 'superseded-by' | 'harmful' | 'obsolete'
  replacement_hash TEXT,                -- only for superseded-by
  reason_blob BLOB
);

-- Optional: a small in-projection blob cache for rt_dval pulls.
-- Empty by default — blobs live in master ops.db and are paged in by hash.
CREATE TABLE local_blob_cache (
  hash TEXT PRIMARY KEY,
  bytes BLOB NOT NULL,
  last_used INTEGER NOT NULL
);
```

### Properties

- **Disposable.** Anyone can delete `projections/<id>/` and the daemon will rebuild on demand from `ops.db`. The current `LibDB.Purge.purge ()` call (`LocalExec.fs:36`) becomes "delete the directory."

- **Branch-scoped, but doesn't carry the parent's data.** The `locations` table only contains rows for *this* branch. Resolution still walks the chain — but the chain walk is now a multi-DB attach pattern, not a bigger filter:

  ```fsharp
  // Pseudocode, daemon-side. Attach the parent projections READONLY,
  // run the lookup against the union, return the closest match.
  let openWithChain (branchId: BranchId) =
      let chain = readChain branchId   // [self; parent; ...; main]
      let conn = openProjection branchId
      chain |> List.skip 1 |> List.iteri (fun i parent ->
          conn.Execute(
              $"ATTACH DATABASE '{projectionPath parent}' AS p{i+1}"))
      conn
  ```

  ATTACH up to ~10 DBs (SQLite's default max is 10; it can be raised). Branch chains in practice are short — main, a feature branch, sometimes a sub-feature. If we ever blow that limit, we promote the projection to "consolidated" (rebuild the projection with parent rows folded in). This is a perf optimization, not a correctness step.

- **The recursive CTE goes away.** The branch chain is materialized into `branch_chain` once at projection build, then it's just a table lookup. No more `branchChainCache` ConcurrentDictionary in process memory; no more invalidation TODOs.

- **The harmful cache goes away.** `deprecations` is a table; it's accurate by construction; queries are O(log n) lookups. The whole `harmfulCache` ConcurrentDictionary in `PackageManager.fs:24-39` deletes.

- **Schema version is per-projection.** If we redesign `package_dependencies`, we bump the projection schema and the daemon notices the version mismatch on open and rebuilds the projection. Master DB doesn't move. This is the heart of why per-projection beats monolithic: experimental schema changes have a tiny blast radius.

### Build & invalidation

A projection is a fold over ops. The fold is exactly what `PackageOpPlayback.fs` already does, just sharded. The current 521-line `PackageOpPlayback.fs` becomes the projection-builder, with two new entry points:

```fsharp
module LibProjection.Build
open LibSerialization.Binary

/// Build a fresh projection from scratch by replaying every op in this branch's chain.
let buildFromScratch (branchId: BranchId) : Task<unit> = task {
    let projectionPath = ProjectionPaths.pkg branchId
    if File.Exists projectionPath then File.Delete projectionPath
    use conn = openProjectionRW branchId
    do! createSchema conn

    let chain = OpsDB.readBranchChain branchId
    do! writeChain conn chain

    // Replay branch_ops first (creates needed branch metadata), then package_ops.
    let! branchOps = OpsDB.readBranchOpsForChain chain
    for op in branchOps do
        do! applyBranchOp conn op

    let! packageOps = OpsDB.readPackageOpsForChain chain
    for op in packageOps do
        do! applyPackageOp conn op

    do! writeHighWatermark conn
}

/// Bring an existing projection forward to the master's current state.
/// O(new ops since last build), not O(all ops).
let catchUp (branchId: BranchId) : Task<unit> = task {
    use conn = openProjectionRW branchId
    let! lastApplied = readHighWatermark conn
    let! newOps = OpsDB.opsSince branchId lastApplied
    for op in newOps do
        match op with
        | PackageOp p -> do! applyPackageOp conn p
        | BranchOp b -> do! applyBranchOp conn b
    do! writeHighWatermark conn
}
```

Note: `applyPackageOp` is essentially `PackageOpPlayback.apply` today — just operating on a connection rather than the global `Sql.connect`. (See § 13 for the connection plumbing.)

This unlocks something important: **a sync pull is just N ops landing in `ops.db`, then `catchUp` on each affected projection**. No special path. Same code path as a local edit. Sync becomes "drag rows over, signal the projection LRU to recheck high-watermark." That's it.

### Eviction

The daemon holds an LRU of open projection connections. Idle projections are closed (file stays). If disk pressure matters (it won't, branches are small — kilobytes of `ops.db` rows yields ~megabytes of projection), we add an `evictIfOlderThan` policy. Default: keep the open projections, GC the closed-and-idle ones nightly.

---

## 6. App / user-data DBs — val.town but better

Today, `Stdlib.DB.set/get` writes to `user_data_v0`, keyed by `tlid` and `dark_version`. That's one giant table for everything. There's no app boundary, no backup unit, no per-app `sqlite3` shell-out, no "wipe just this app's data."

The proposal: **every running app gets its own SQLite file at `apps/<app-id>/data.db`.** That file is *the* data of that app. The Dark code accesses it through:

1. The existing `Datastore<T>` abstraction — but the underlying connection is the app's own file, not the global one. So `Stdlib.DB.set/get/query` keep working without source changes.
2. **A new low-level builtin** for raw SQLite access (val.town-style). Roughly:

   ```dark
   let result = Builtin.sqliteExec "SELECT * FROM users WHERE id = ?" [Sql.text userId]
   ```

   This is exactly what the vault `User Data/dl-data-rough.md` contemplates. The "but better" part is type-aware result decoding: result rows decode through the same parsing layer as `Datastore<T>` reads — so a raw query into a typed table gives you a `List<User>` not a `List<List<Dval>>`. The user can drop down to raw rows via a separate, more cumbersome API.

### What is an app, exactly?

An "app" is a runnable, addressable unit of Dark code with state. Concretely:

- A Dark-side value of type `App`, registered somewhere in the package tree.
- An app-id (UUID, generated on first start).
- A directory `apps/<app-id>/` with at least `data.db` and a `manifest.json` (id, latest router hash, owner, schema version, last started).
- A daemon supervisor that spins it up (whether HTTP-served, cron-driven, worker-driven, or one-shot).

Today there's only `serve <router-path> [--port N]`, and it blocks the CLI. After this rewrite:

```
$ dark app new my-blog                # creates apps/<uuid>/
$ dark app set-router my-blog Darklang.Examples.Blog.router
$ dark app start my-blog --port 9001
$ dark app list
ID                                   NAME      KIND    STATUS   PORT  ROUTER
68f9...  my-blog                     http    running  9001  Darklang.Examples.Blog.router
$ dark app data my-blog               # opens sqlite3 on apps/<uuid>/data.db
$ dark app logs my-blog --tail
$ dark app traces my-blog --recent 20
$ dark app stop my-blog
$ dark app delete my-blog --keep-data    # archives apps/<uuid>/ to apps/_archive/
```

Apps are **independent supervised processes** under the daemon (each one its own .NET ExecutionState; could be its own .NET process if we want full crash isolation, but in-daemon is fine for v1). Apps started this way are exactly what `serve <router>` is doing today, but elevated to a first-class concept with persistent state and a directory.

Crons and workers are app kinds too — `kind: "cron"`, `schedule: "*/5 * * * *"`, `kind: "worker"`, `subscribed_topics: [...]`. The daemon's app supervisor knows how to run all three.

### How user code addresses its DB

The current `RT.DB.T` passes through `tlid` everywhere. With per-app DBs, we need to carry a "DB connection" handle through `ExecutionState`. The cleanest cut:

- `ExecutionState.appContext : Option<AppContext>`  — None when running ad-hoc / from CLI; Some when running inside an app.
- `AppContext = { appId: AppId; dataDb: SqliteConnection; assets: AssetStore }`.
- `Stdlib.DB.set` / `get` / `query` builtins read from `vmstate.executionState.appContext` and route to that connection. If `appContext` is None, they hit a *temporary, ephemeral* DB scoped to this CLI invocation (so `eval Stdlib.DB.set ...` doesn't pollute anyone). The CLI gets a `--persist-as <app>` flag to run a one-shot against an existing app's data.

This eliminates the `tlid` filter on `user_data_v0`. The `tlid` becomes a *table name within the app's DB*, not a row column. So:

```
-- Today: one user_data_v0 table, filtered by tlid + version + dark_version
SELECT data FROM user_data_v0 WHERE table_tlid=? AND user_version=? AND dark_version=? AND key=?

-- Proposed: per-table, in the app's own DB
SELECT data FROM users_v0 WHERE key=?
```

This makes the `sqlite3 apps/my-blog/data.db` workflow actually useful. You see your data in your tables. You can `.schema`, you can `.dump`, you can `litestream replicate` a single app's DB without dragging the whole monolith.

### Schema migrations for apps

Each app's `data.db` carries its own `schema_version` and a per-app migration path driven by Dark code (the developer writes `migrate v1->v2` Dark fns; the daemon runs them on startup if the file is older). Until that's built, kill-and-fill on dev branches is fine — `dark app reset my-blog` wipes data.db and reinits.

---

## 7. Trace DBs (`projections/<branch>/traces.db`)

Traces are noisy, ephemeral, schema-volatile (3 redesigns in the last year per the migrations directory), and bound to a development context. They want to live separately from packages and from app data.

```sql
-- projections/<branch>/traces.db
-- Same shape as today's `traces` and `trace_fn_calls`, no other tables.
CREATE TABLE traces (
  id               TEXT PRIMARY KEY,
  root_tlid        INTEGER NOT NULL,
  handler_desc     TEXT NOT NULL,
  timestamp        INTEGER NOT NULL,
  input_name       TEXT NOT NULL,
  input_value_json TEXT NOT NULL,
  -- new: associate traces with their causal session
  session_id       TEXT,
  app_id           TEXT
);

CREATE TABLE trace_fn_calls (
  trace_id        TEXT NOT NULL,
  call_id         TEXT NOT NULL,
  parent_call_id  TEXT,
  kind            TEXT NOT NULL,
  fn_hash         TEXT,
  lambda_expr_id  TEXT,
  args_json       TEXT NOT NULL,
  result_json     TEXT NOT NULL,
  duration_ms     INTEGER,
  PRIMARY KEY (trace_id, call_id)
);
CREATE INDEX idx_tfc_trace_id ON trace_fn_calls(trace_id);
CREATE INDEX idx_tfc_fn_hash  ON trace_fn_calls(fn_hash);

CREATE TABLE trace_expr_values (
  trace_id TEXT NOT NULL,
  call_id  TEXT NOT NULL,
  expr_id  TEXT NOT NULL,
  value_json TEXT NOT NULL,
  PRIMARY KEY (trace_id, call_id, expr_id)
);
```

Why per-branch:

- A trace is *tied to* the package state that produced it (`fn_hash` references items that exist in the projection). Putting traces in the same dir as the projection makes that pairing explicit.
- Branch deletion deletes the traces. That's the right behavior — a deleted branch's traces have no reference targets anymore.
- Schema churn on traces is no longer a global concern.
- TTL / pruning is per-branch — the noisy `eval` traces from your scratch branch don't count against your main branch retention budget.

We could also have `apps/<app-id>/traces.db` for production-app traces — those live with the app rather than with a branch. (An app references a router fn at a particular hash, so it has a sort of branch-pin already; but app traces are owned by the app, not by the branch the app's code happens to have come from.)

---

## 8. The CLI daemon (`darkd`)

I'm calling this `darkd` for the rest of the doc. It is the brain. It is what every `dark` command actually talks to.

### Lifecycle

`dark <cmd>` does the smallest possible thing: connect to `~/.darklang/daemon.sock`, send a small JSON-or-bincode message, stream back text/events. If the socket isn't there, it spawns the daemon (forking via `dotnet bin/Darkd.dll &`) and retries. The daemon is `--idle-stop=2h` by default — it'll exit if nothing has talked to it for 2h. Cron-or-worker apps reset the idle timer by virtue of being scheduled events. `dark daemon stop` kills it; `dark daemon status` reports.

This is exactly the systemd-or-similar pattern the vault `CLI.md` calls out:

> for when we need to have a long-running `dark` process ... if a service is running and just able to process stuff, then `dark` itself can just be a super thing thing that reaches out to that long-running service

Yes. That.

### What the daemon owns

- **Master DB connection.** One read-write connection (WAL mode, exclusive writer for ops; readers can read concurrently). All op insertion goes through one in-process queue; sync incoming ops also append here.
- **Projection LRU.** `(BranchId → ConnectionPool × LastUsed)`. Each entry has its own `pkg.db` connection. Caps at, say, 32 open projections; idle eviction is fine.
- **App supervisor.** `(AppId → RunningApp)`. Each `RunningApp` is an `ExecutionState` plus its `data.db` connection plus a Task that runs the app loop (HTTP listener / cron timer / worker queue poller). Crashes get caught, restarted with backoff.
- **Trace recorder.** Receives trace events from running apps; batches inserts into the appropriate `traces.db`.
- **Sync agent.** A long-lived task per peer that pulls ops, applies them to `ops.db`, then signals the projection LRU "branch X has new ops; bump the watermark." Per Tailscale notes, this is HTTP over a tailnet name, with `Tailscale-User-Login` for auth.
- **Tracing of itself.** `darkd_status` prints per-projection memory, last-fold time, queued ops, etc. Crons that haven't fired, last sync timestamp per peer, etc.

### Process model — one big daemon vs one daemon per branch?

The user's framing was "one CLI daemon per session/active branch." I want to push back gently on the *strict* form of that, then walk it back to roughly that.

**Strict form (one daemon process per branch):** Bad. .NET startup cost is real. Five branches in flight = five .NET processes = five lots of GC/JIT/etc. They'd all need the master DB; SQLite WAL allows multiple readers but ops appends are serialized regardless. We don't get isolation between branches that we couldn't get inside one daemon — F# doesn't crash *that* often, and an app crash is what we have AppDomain / Task isolation for.

**Loose form (one daemon, but it owns a collection of "branch contexts"):** Good. One daemon, one master-DB writer, multiple in-process branch contexts. Each branch context is a closed-over `(branchId, pkgConn, traceConn, packageManager, harmfulSet)`. Commands route by branchId. This gives us the same logical isolation (a `dark --branch feat fn ...` doesn't disturb the main branch's PM cache) without the process explosion.

**Where strict form earns its keep:** when we want hard process isolation. Specifically:
- An app that could OOM should not take the daemon down. Each running *app* gets its own subprocess (small Dark host that loads the right `pkg.db` readonly + the app's `data.db` rw + the app's traces.db rw, runs its router, and reports back). Daemon supervises.
- An LLM agent running in `--allow-harmful` mode arguably wants its own subprocess too.

So: **one daemon, with subprocesses for apps.** Branches are not their own daemons; they're contexts inside the daemon. This stays simple and matches how F# wants to be deployed.

### Wire protocol

Unix socket, length-prefixed framed messages, content negotiated — `application/dark-bincode` for fast paths (PackageManager queries, ExecutionState sharing) and `application/json` for `dark` CLI commands. Loopback HTTP fallback for when sockets don't work (Windows compat).

```
client → daemon : Request { sessionId, command, args, env }
daemon → client : Stream of Event { Stdout / Stderr / Progress / Result(exitCode) }
```

Each request is logically an `ExecutionState.run` against the appropriate (branch, app, session) tuple. Session tracking is below.

---

## 9. Sessions

The vault has a lot of "what is a session" angst (`Sessions and Workspaces/Sessions.md`). I'll cut through it: a session is **a stable handle to a (branch, working-directory, env, intent) tuple, plus its tail of traces**. That's it. Everything else — pinned views, file lists, per-session env vars — is per-app UI state, not a runtime concept.

```sql
-- ~/.darklang/sessions.db   (small, local-only, never syncs)
CREATE TABLE sessions (
  id           TEXT PRIMARY KEY,
  display_name TEXT NOT NULL,
  branch_id    TEXT NOT NULL,
  intent       TEXT,
  cwd          TEXT,
  created_at   INTEGER NOT NULL,
  last_active  INTEGER NOT NULL
);

CREATE TABLE session_env (
  session_id TEXT NOT NULL,
  key        TEXT NOT NULL,
  value      TEXT NOT NULL,
  PRIMARY KEY (session_id, key)
);
```

CLI bindings:
- `dark session new --intent "fix Cli completion" --branch feat-cli` → returns id
- `dark session list`
- `dark session continue <id|name>` → exports `DARK_SESSION` env var; subsequent `dark` commands attach to it
- `dark session attach <id> --on <device>` → run on a peer
- `dark session kill <id>`

Sessions are lightweight. They don't have their own DBs (sessions.db holds them all, *not* per-session). The "session has its own DB" idea is wrong — a session owns *traces* (which live in the branch's `traces.db`, scoped by `session_id`), and a *branch* (which has its own DBs). The session itself is just a pointer.

The interesting move: **the daemon makes "current branch" a session-scoped variable, not a CLI-scoped flag.** Today, every `dark` invocation needs `--branch X`. With sessions, you `dark session continue feat-cli` once in a terminal, and from then on all `dark` commands in that shell are scoped to `feat-cli`'s branch. Across machines: `dark session continue feat-cli --on major` runs your session on the big PC.

This is exactly tmux semantics. Good.

---

## 10. Sync

I want to make sync **unbelievably boring**. The current vault notes flip between "automatic", "settings-controlled", "approval flows", "p2p", "central server", "feeds and queues". Most of that is premature. Start with:

### v1 sync

**Topology:** star. One device — `major` — runs `darkd` and acts as the hub. Every other device pushes ops to `major` and pulls ops from `major`. No mesh, no multi-master, no CRDTs.

**Transport:** HTTP over a Tailscale-served port. `tailscale serve --https=443 http://localhost:11000`. The daemon exposes:

```
GET  /sync/ops?since=<hash>&branch=<id>          → SSE/stream of ops, oldest first
POST /sync/ops                                    → body: ops to insert (idempotent on hash)
GET  /sync/branch-ops?since=<hash>                → stream of branch_ops
POST /sync/branch-ops
GET  /sync/blobs?hashes=<csv>                     → multipart of blob bodies
POST /sync/blobs                                  → multipart of blob bodies, keyed by hash
GET  /sync/heads                                  → { peers[], branches[] heads }
```

**Auth:** `Tailscale-User-Login` header is the entire auth story for v1. Per-tag ACLs handle who can sync; Dark doesn't reimplement auth. (Per the Tailscale notes, no API keys, no shared secret.)

**Idempotency:** every op has a content-addressed hash. POST is INSERT OR IGNORE. Push twice, no harm. Push three times concurrently from three devices, no harm. Sync is *exactly* "drag rows over."

**Ordering:** none required at the transport level. Inside Dark, ops on a branch chain logically order themselves through `parent_op` (if we keep it; today we use `created_at` as a soft order, which is fine because ops applied in different orders converge to the same projection given the current op shapes).

**Offline:** the daemon batches outgoing ops in `sync_state.last_pushed_op`. When `major` is reachable, push the gap. When unreachable, queue. When reconnecting, push everything since `last_pushed_op`.

**WIP vs committed:** the current vault says "sync only committed" or "sync uncommitted" should be config. Default: sync everything; let the editor (CLI/VS Code) hide WIP from non-owners. Adding a "don't sync WIP from this branch" filter is a 5-line opt-out.

That's v1. ~200 lines of F# in `LibSync/`. A few endpoints in a tiny Dark router on the central server.

### v2 sync

Once v1 works for two people:

- **Mesh.** When `major` is down, devices push to each other. Same protocol; just point at a different peer. The `sync_state` table tracks per-peer watermarks already.
- **Selective sync.** `dark sync set-policy --pull-only --branch main` etc. Vault has a rough taxonomy already; pick from it as needs emerge.
- **Conflict UX.** Per `dl-2025-11-12-ux-thinking.md`: conflicts in this model are basically just "two ops both name `Foo.bar` to different hashes." Surface those in `dark status`, let user pick. Don't build approvals/PRs in v1.

### What does NOT sync

- `projections/*` (projection DBs) — local cache, rebuildable
- `apps/*` (user data) — *probably* doesn't sync at all; an app on `major` is a different running instance from one on the laptop. If user wants their app's `data.db` synced, that's a `litestream` job, not a Dark concern. (Future: opt-in per-app sync via the same op/projection split — but applied to userland data.)
- `traces/*` — local-only; don't ship trace blobs across the wire.
- `sessions.db` — local-only; sessions are per-machine workflow state.

Important: **only `ops.db` and the blobs it references travel.** Everything else is derived locally.

---

## 11. Migrations, the fewest possible

Today: one global directory, every change runs against `data.db`. With this proposal:

- **Master DB migrations:** ~zero. Schema is frozen. Op shape changes go inside `op_blob` and are read by versioned binary deserializers.
- **Projection schema migrations:** none. We delete and rebuild. Bump `projection_state.schema_version`; daemon notices the version mismatch on open, logs "rebuilding projection for branch <id> due to schema drift", calls `buildFromScratch`. Free, automatic, takes seconds for any reasonable branch.
- **Traces:** same as projections. Drop and recreate.
- **Apps:** each app has its own migration path, written in Dark by the app author. Daemon runs Dark migrate fns when starting an app whose `data.db.schema_version` is older than the app's manifest version.
- **`sessions.db`:** trivial; tiny local file; can rewrite or migrate freely.

This is a huge win. We currently have 14 migrations in `backend/migrations/`. Most of them — `add_propagation_id_column`, `branch_ops_and_archive`, `add_trace_data`, `merge_trace_fn_tables`, `trace_fn_calls_redesign`, `trace_fn_calls_duration`, `package_blobs`, `dependency_location`, `trace_expr_values` — are exactly the kind of cross-table churn that goes away when:
- ops-shape changes are deserializer concerns, not schema concerns
- projection-shape changes are kill-and-fill, not ALTER

The few that remain (initial schema, schema rewrites we *want*) are fine.

The vault note about kill-and-fill being ok for the deprecation redesign generalizes: *all projections are kill-and-fill-friendly*, because the master is the truth. That mental model needs to extend to traces and (largely) to apps.

---

## 12. Booting from zero

What does `dark init` look like in this world?

```
$ dark init
~/.darklang/                          ← created
  ops.db                              ← initialized, empty schema
  projections/                        ← empty
  apps/                               ← empty
  config.toml                         ← {device_id: ..., display_name: "stachu's-laptop"}

$ dark sync clone tail-major          ← pull ops from peer "major"
Pulled 482,113 ops in 11s.
Building projection for main...
Built (4.3s).
$ dark tree
... usual output ...
```

Compare with today's "bake the seed.db.bin into the binary, copy it on first run". The seed approach goes away. Or rather: **the seed becomes a snapshot of `ops.db` at a known point**, and `dark init --bootstrap-from <embedded>` is what extracts it. Sync is the upgrade path. Embedded seed is the bootstrap.

If we want a quicker init for "I'm a new user, I just installed dark, I want stdlib": ship a frozen `ops-snapshot-v0.db` inside the binary (same idea as `data.db` is embedded today, just a different file), drop it as `ops.db`, and the projection builds from there. Same code path as a sync clone.

---

## 13. F# code sketch — the connection plumbing

This section is the part where we go from "nice idea" to "what changes in `backend/`". I'm sketching the plumbing because it's the bit most likely to surface a deal-breaker.

### LibConfig

```fsharp
module LibConfig.Config

let darkRoot =
  // resolution order: $DARK_ROOT > ~/.darklang > $XDG_DATA_HOME/darklang
  match getEnv "DARK_ROOT" with
  | Some d -> d
  | None ->
    let home = System.Environment.GetEnvironmentVariable "HOME"
    Path.Combine(home, ".darklang")

let opsDbPath = Path.Combine(darkRoot, "ops.db")
let projectionsDir = Path.Combine(darkRoot, "projections")
let appsDir = Path.Combine(darkRoot, "apps")
let socketPath = Path.Combine(darkRoot, "daemon.sock")
let configPath = Path.Combine(darkRoot, "config.toml")
let sessionsDbPath = Path.Combine(darkRoot, "sessions.db")

let projectionPkgPath (branchId: System.Guid) =
  Path.Combine(projectionsDir, branchId.ToString(), "pkg.db")

let projectionTracesPath (branchId: System.Guid) =
  Path.Combine(projectionsDir, branchId.ToString(), "traces.db")

let appDataPath (appId: System.Guid) =
  Path.Combine(appsDir, appId.ToString(), "data.db")
```

The single `dbPath` of today disappears. Every code path that opens a connection now needs to know *which* DB. That's a lot of call sites — but the discipline is healthy.

### LibDB.Sqlite

Today's `LibDB.Sqlite` exposes `Sql.connect` which wraps a *single* connection string. That goes:

```fsharp
module LibDB.Sqlite

type ConnRef =
  | OpsRW
  | OpsRO
  | ProjectionPkgRW of branchId: System.Guid
  | ProjectionPkgRO of branchId: System.Guid
  | ProjectionTracesRW of branchId: System.Guid
  | ProjectionTracesRO of branchId: System.Guid
  | AppDataRW of appId: System.Guid
  | AppDataRO of appId: System.Guid
  | Sessions

let private connStringFor (ref: ConnRef) : string =
  let path =
    match ref with
    | OpsRW | OpsRO -> Config.opsDbPath
    | ProjectionPkgRW b | ProjectionPkgRO b -> Config.projectionPkgPath b
    | ProjectionTracesRW b | ProjectionTracesRO b -> Config.projectionTracesPath b
    | AppDataRW a | AppDataRO a -> Config.appDataPath a
    | Sessions -> Config.sessionsDbPath

  let mode =
    match ref with
    | OpsRO | ProjectionPkgRO _ | ProjectionTracesRO _ | AppDataRO _ -> "ReadOnly"
    | _ -> "ReadWriteCreate"

  $"Data Source={path};Mode={mode};Cache=Private;Pooling=true"

// Per-connection initialization (PRAGMAs).
let private initialize (props: SqlProps) : SqlProps = ...

let connect (ref: ConnRef) : SqlProps =
  Sql.connect (connStringFor ref) |> initialize

// Compatibility: keep the old `Sql.connect` until the migration is done.
let connectLegacy = connect OpsRW   // points at master, since most legacy callers want ops
```

Connection pooling: each ConnRef gets its own pool key (Microsoft.Data.Sqlite handles this via the connection string). Per-projection DBs have small pool sizes (1-2). Master ops DB has a bigger one (8-ish, since it's the busiest reader).

### LibDB.PackageManager

Today: builds a `PT.PackageManager` whose `findFn` etc. do branch-chain resolution by querying `locations` on the master DB.

Tomorrow:

```fsharp
module LibDB.PackageManager

let pt (branchId: BranchId) : PT.PackageManager =
  // Look up (or build) the projection for this branch.
  let conn = Projection.openOrBuild branchId

  { findFn = fun (_, location) ->
      withCache (fun () -> Queries.findFn conn location) location
    findType = fun (_, location) ->
      withCache (fun () -> Queries.findType conn location) location
    // ...etc.
  }
```

Two key changes:

1. The `branchId` is no longer needed *inside* `findFn` — the connection is already branch-scoped (the projection IS the branch chain materialized). So queries get simpler: no more `WHERE branch_id IN (chain)`, no more recursive CTE on every call.
2. `Projection.openOrBuild` is the daemon's responsibility. From inside the daemon, this is a HashMap lookup on the LRU. From a non-daemon caller (e.g. tests, LocalExec), this is "build on the fly into a temp dir" — slower but still correct.

### Projection module (new)

```fsharp
module LibProjection.Open

open LibDB

type Projection = {
  branchId: BranchId
  pkg: SqlProps           // RO connection to projections/<id>/pkg.db
  traces: SqlProps        // RW connection to projections/<id>/traces.db
  state: ProjectionState  // schema version, last applied op, etc.
}

let private lru = ConcurrentDictionary<BranchId, Projection>()

/// Daemon-side: open the projection, building it from ops if absent.
let openOrBuild (branchId: BranchId) : Task<Projection> = task {
  match lru.TryGetValue branchId with
  | true, p -> return p
  | _ ->
    let path = Config.projectionPkgPath branchId
    if not (File.Exists path) then
      do! Build.buildFromScratch branchId
    else
      let! state = readState branchId
      if state.schemaVersion < CURRENT_PROJECTION_SCHEMA then
        File.Delete path
        do! Build.buildFromScratch branchId
      else
        // Catch up to master, no full rebuild
        do! Build.catchUp branchId

    let p = makeProjection branchId
    lru.[branchId] <- p
    return p
}
```

### LibDB.Inserts

The current insert path inserts an op into `package_ops` AND applies it to the projection tables in the same transaction. With the split, we tease those apart:

```fsharp
module LibDB.Inserts

/// Append ops to master ops.db.
/// Returns ops actually inserted (dedup'd by content hash).
let appendOps
  (branchId: BranchId)
  (commitMsg: Option<string>)
  (ops: List<PackageOp>)
  : Task<int64> = task {
    use conn = Sqlite.connect Sqlite.OpsRW
    // ... write to package_ops table, with origin_device = our device_id
    // No call to `applyToProjection` here.
    return inserted
  }

/// Update affected branches' projections.
/// Called *after* `appendOps` returns.
let advanceProjections (branchId: BranchId) : Task<unit> = task {
  // Bring this branch's projection forward.
  do! LibProjection.Build.catchUp branchId
  // Also descendants (if a parent changed, their chains include the new ops).
  let! descendants = OpsDB.descendants branchId
  for d in descendants do
    if Projection.isOpen d then
      do! LibProjection.Build.catchUp d
}
```

The two-phase `applied=false / applied=true` dance from current `Inserts.fs` lines 38-104 is gone, because there's no half-applied state to track *within ops.db*. Failure modes:
- `appendOps` fails: nothing in master, no projection change. Caller retries.
- `appendOps` succeeds, `advanceProjections` fails: master is consistent; the next read (or the next `catchUp`) re-applies. Idempotent.
- Crash mid-`advanceProjections`: same as above. The high-watermark in `projection_state` is the only thing that has to be advanced atomically (it is — single write, end of transaction).

### Builtins.PM / Builtins.Matter

These are the consumers. Today they call into `LibDB.PackageManager` directly. They keep working — just behind the new projection interface. No surface changes.

### Builtins.DB (user-data builtins)

This is `Stdlib.DB.set/get/query`. Today they read `vmstate.executionState.program.scopeID` (or rather: don't, post-`scope_id`-strip; they use `tlid` directly). Tomorrow, they read `vmstate.executionState.appContext` and route to that connection:

```fsharp
let setBuiltin = ...
  fun (_, _, _, args) -> uply {
    match args with
    | [DDB db; DString key; value] ->
        match exeState.appContext with
        | Some ctx ->
          let! id = LibDB.UserDB.set ctx.dataDb threadID true db key value
          return ...
        | None ->
          // Ad-hoc CLI eval — use a temp DB, scoped to this VM's lifetime
          let! id = LibDB.UserDB.set ctx.scratchDb threadID true db key value
          return ...
    ...
```

The `LibDB.UserDB` module gets parameterized on a connection rather than reaching into the global `Sql.connect`.

### Sqlite raw-access builtin (new)

```fsharp
let sqliteExec : BuiltInFn = ...
  fun (_, _, _, args) -> uply {
    match args with
    | [DString sql; DList paramList] ->
        match exeState.appContext with
        | None -> return Error (RuntimeError.Other "sqliteExec requires an app context")
        | Some ctx ->
          let! rows = ctx.dataDb |> Sql.query sql |> Sql.parameters (...) |> ...
          return Ok (Dval.list (rows |> List.map decodeRow))
    ...
```

This is the val.town-style escape hatch. We should *also* generate typed wrappers for declared `Datastore<T>`s automatically — but raw access is needed too, for migrations and for apps that want join/aggregate semantics over their own data.

---

## 14. Dark-side surface

What does this look like to a user writing Dark code or driving the CLI?

### CLI commands

Almost everything stays. New things:

- `dark daemon status / start / stop / restart / logs` — the daemon's lifecycle
- `dark app new / list / start / stop / delete / data / logs / traces` — apps
- `dark session new / list / continue / kill / attach`
- `dark sync clone / pull / push / status / set-policy`
- `dark devices` — list tailnet peers (per the Tailscale notes)
- `dark on <device> <command>` — run a command on a peer's daemon (requires Property 2 from the Tailscale notes)

`dark project` becomes a thin wrapper around `dark app` for the most common case: "I want to run this directory as a single app." `serve <router-path>` becomes `dark app start --inline <router-path>` — registers an ephemeral app, runs its server, deletes the app on exit.

### Dark-language additions

```dark
// Stdlib.DB stays the same
let users : Datastore<User> = ...

// Stdlib.Sqlite is new — raw access, val.town style
let highValueUsers =
  Stdlib.Sqlite.query<User>
    "SELECT data FROM users_v0 WHERE json_extract(data, '$.balance') > ?"
    [Stdlib.Sqlite.SqlValue.Int64 1000L]

// Stdlib.App is new — introspection
let myAppId = Stdlib.App.currentId ()
let myAppDataDir = Stdlib.App.dataDir ()      // "/home/.../apps/<id>/"
let mySession = Stdlib.Session.current ()     // None if not in a session

// Stdlib.Sync is new — control sync from inside Dark code (rare)
Stdlib.Sync.pushNow ()
```

That's a small addition — most Dark code doesn't change. The big affordance is *the existence* of per-app data, not how you address it.

---

## 15. Filesystem layout (canonical)

Putting everything together:

```
~/.darklang/
  config.toml                                  # device id, display name, etc.
  ops.db                                       # MASTER, append-only
  ops.db-shm
  ops.db-wal
  daemon.sock                                  # daemon listener
  daemon.pid
  daemon.log
  sessions.db                                  # local-only sessions

  projections/
    <branch-uuid>/
      pkg.db                                   # branch projection
      pkg.db-shm
      pkg.db-wal
      traces.db
      built-at                                 # marker file with timestamp

  apps/
    <app-uuid>/
      manifest.json                            # router, kind, schedule, schema_version
      data.db                                  # app's user-data DB
      data.db-shm
      data.db-wal
      assets/                                  # optional static assets
      logs/                                    # rolling stdout/stderr
      traces.db                                # production traces (separate from dev traces)

    _archive/                                  # `dark app delete --keep-data` lands here

  cache/
    package-refs/                              # PackageRefs hash file (today's package-ref-hashes.txt)
    builds/                                    # if we move tree-sitter compiled grammars here

  bootstrap/
    ops-snapshot-v0.db                         # embedded seed (extracted on first run)
```

Everything outside `~/.darklang/` is source code or the binary. Everything inside `~/.darklang/` is state.

The `rundir/` concept disappears. Tests get their own `DARK_ROOT=/tmp/dark-test-XXX/` per process.

---

## 16. What dies

Concretely, this proposal kills a lot of code:

- `LibDB.Branches.branchChainCache` — gone, replaced by `branch_chain` table in projection
- `LibDB.PackageManager.harmfulCache` — gone, replaced by `deprecations` table in projection
- `LibDB.Caching.withCache` — kept but lives only inside the daemon, not per-CLI-process
- The two-phase `applied=false / applied=true` dance in `LibDB.Inserts` — gone, projection has its own watermark
- The `WHERE branch_id IN (...)` filter on every projection query — gone, projection is branch-scoped
- The recursive CTE in `getBranchChain` — gone, materialized
- `LibCloud` (per existing memory rule: disable, don't delete) — disable; absorb survivors into LibDB
- `WipRefresh.fs` (181 lines of "rebuild package refs after a load") — most of it; the projection rebuild path subsumes it
- `LibDB.Purge.purge ()` — replaced by `rm -rf ~/.darklang/projections`
- `package-ref-hashes.txt` as a build artifact — moved to `~/.darklang/cache/`, no longer embedded
- Per-tlid `dark_version`/`user_version` filter pattern in `LibDB.UserDB` — when each app has its own DB, those columns become "what schema version is this DB" (a single row in a `meta` table), not a per-row filter

The `LocalExec` `reload-packages` command also changes shape:

```
Today:
  reload-packages = parse all .dark files → emit ops → wipe data.db tables → apply ops to projection tables

Tomorrow:
  reload-packages = parse all .dark files → emit ops → append to ops.db → drop projections/ → next read rebuilds
```

Or, if we go full bootstrap (per `next steps towards stability+sync+sharing.md`), we don't reload-packages at all in normal use; we sync from `major`, and the `.dark` files become a build-time snapshot, not a runtime input.

---

## 17. Risks, hazards, footguns

Where this could go wrong:

### Risk: ATTACH-based branch-chain reads have surprising perf

SQLite's ATTACH is fast, but query planner across attached DBs is sometimes worse than across native tables. We may discover that a 3-level chain with 80k locations each is slower than the current monolith.

Mitigation: profile early, with realistic data. If ATTACH is bad, **denormalize the chain into the projection** — every projection contains a copy of its parents' rows (since locations are tiny: hash + name + a few flags). Cost: O(branches × avg-chain-depth × locations). With 50 branches and a chain depth of 3 and 80k locations each, that's 12M rows, ~1GB. Acceptable.

### Risk: app subprocess overhead

Each running app being its own .NET process means each consumes ~20MB+ baseline. If we have 50 apps running, that's a gig.

Mitigation: in-daemon hosting by default; per-process only when an app declares `--isolated` or has crashed too many times. Or: pre-AOT'd app host (smaller; the AOT memo from the vault has notes on this).

### Risk: tests want a clean slate

Today, `TestUtils.initializeTestCanvas` truncates the global tables. With per-projection DBs, the test's "clean slate" is "delete the test's projection dir." That's actually cleaner — no shared state to worry about — but every test file needs to declare its DARK_ROOT.

Mitigation: a small test fixture that sets `DARK_ROOT=/tmp/dark-test-<gid>/` and tears down on completion. Wire it into `TestUtils.initializeTestCanvas`.

### Risk: sync without a generation/clock breaks on real concurrency

If two devices both do `package_op X` against the same name, they both think they "won". Today's branch-scoping helps (each device works on its own feature branch), but two devices both editing main — say a stdlib improvement landed by Stachu and Feriel on the same morning — will produce a divergence.

Mitigation: rebase-before-push on main (per the existing SCM design). Each device, before pushing to main, pulls main, rebases its local `main`-aimed work onto the new tip, resolves any name conflicts, then pushes. The protocol is simple; the UX is the work. v1 ignores this and lets last-writer-win on names (correct semantics: two SetName ops, the later one wins; the earlier definition still exists at its hash, just not bound to a name). This is fine for a two-person team.

### Risk: projection-rebuild thundering herd

If we change projection schema and a user has 30 branches, opening main triggers a rebuild — ~2 seconds. If they then open another branch, another 2 seconds. They notice.

Mitigation: rebuild-on-demand is fine; `dark daemon warm-projections` proactively rebuilds the top N most-recent branches in parallel. Show a progress bar in `dark status` if a rebuild is in progress.

### Risk: the daemon model is harder to debug than "fresh process every time"

Live caches, mutable state, supervisor fan-out — all the classic daemon hazards.

Mitigation: structured logging (`darkd_status` introspection), per-projection stats, an explicit `dark daemon dump` that drops the in-memory state to JSON for postmortem. Also: keep the "no-daemon mode" working as a fallback (`dark --no-daemon ...` runs everything in-process). It'll be slower but it's a critical escape hatch.

### Risk: trace volume blows up `traces.db`

Every `eval` records a trace. Hundreds of traces per dev session. `traces.db` per branch can grow.

Mitigation: keep the existing trace-prune idempotent assertion (it's already in the recent commit log). Run a daily cron-style prune that keeps last-N-per-handler. Per-branch traces.db means pruning never touches the package data — already a win.

### Risk: people who edit `data.db` directly today

Some workflows involve poking `data.db` with `sqlite3` to debug. With the split, "where do I look" is more questions.

Mitigation: documented! `dark debug db <kind>` opens a `sqlite3` shell on the right file. `kind` ∈ `ops`, `projection <branch>`, `app <name>`, `traces <branch>`, `sessions`. Discoverable. And per-app `dark app data <name>` is a *better* DX than the current "find the right tlid filter".

---

## 18. Phasing — the smallest first cut that proves the whole thing

I want to land this in slices that each leave the tree green. Rough order:

### Slice 0: nothing visible yet
- Move `data.db` path from `rundir/data.db` to `~/.darklang/ops.db`. (Same file, new home.)
- Introduce the `LibDB.Sqlite.ConnRef` type but leave only `OpsRW`/`OpsRO` (no projection refs yet).
- All callers go through `connect ConnRef.OpsRW` instead of the legacy global. Pure rename.
- Tests still pass. No user-visible change.

### Slice 1: split `traces.db` out of `ops.db`
- Migration: read `traces`, `trace_fn_calls`, `trace_expr_values`; write to `~/.darklang/traces-global.db` (single file for now, not yet per-branch); drop from master.
- Update `LibDB.Tracing` to use a separate connection.
- Single change. Validates the split-out pattern. Easiest win.

### Slice 2: split `user_data_v0` out (per-app, but with a single fallback "default" app)
- Add `apps/_default/data.db`. Route all `Stdlib.DB.*` builtins to it for now.
- Drop `user_data_v0` from master. Test isolation now wipes `apps/_default/data.db` instead.

### Slice 3: introduce projection DBs for one branch
- Pick `main`. Add `LibProjection.Build.buildFromScratch` and `catchUp`.
- For `main`, route reads through the projection DB; for other branches, keep reading from master.
- Compare query results in CI for a week. If equal everywhere, flip the default.

### Slice 4: route all branches through projections
- All branches now have their own `pkg.db`. Master retains only `package_ops`/`branch_ops`/`blobs`.
- Drop projection columns from master. Major schema cleanup migration.

### Slice 5: the daemon
- Implement `darkd`. CLI-to-daemon RPC over Unix socket.
- Daemon hosts the projection LRU.
- `dark` command becomes a thin client.
- `dark --no-daemon` flag preserves the old in-process path for emergencies.
- This is the moment "second `dark` invocation is fast" lands.

### Slice 6: apps as a first-class concept
- `dark app new/start/stop/...`.
- `serve` deprecates to `dark app start --inline <router>`.
- Each app gets its own `data.db` (move them out of the `_default` app over time).

### Slice 7: sessions
- `sessions.db` and the small CLI surface.
- Daemon respects `DARK_SESSION` for branch routing.

### Slice 8: sync
- Sync HTTP endpoints in a Dark router — exactly per `next steps towards stability+sync+sharing.md`.
- Background pull/push agent in the daemon.
- Tested across two tailnet peers (this device + `major`).

### Slice 9: per-app subprocesses
- Apps that declare `--isolated` get their own .NET process under the daemon supervisor.

Each slice is independently shippable. Each leaves the tree better than it found it. Each has a clear test.

---

## 19. Open questions I'd want answers to before turning this into a plan

1. **Are we okay forcing `~/.darklang/` as the canonical state location?** Some users will want it elsewhere (USB drive, encrypted volume, NFS). `DARK_ROOT` env var handles this, but defaults matter.

2. **Cross-branch transactionality.** A propagation today touches many ops. With ops in master and projections separate, do we need a "session" concept at the storage layer (separate from the user-facing session)? Probably not — projections catch up after appendOps; intermediate states are visible briefly. Confirm this is OK semantically.

3. **What happens to `LocalExec` / `reload-packages` mid-rewrite?** It's the bootstrap. It needs to keep working even as we churn the underlying split. The slice ordering above makes it work at every step, but I'd want to confirm.

4. **PackageRefs.fs and `package-ref-hashes.txt`.** This is currently embedded into LibExecution. With per-projection DBs, those refs are arguably per-projection too. Or: we keep them global (cross-branch) since the ref hashes are content-addressed.

5. **Inter-app DB references.** Can app A reference app B's data? Probably not directly — apps should communicate over HTTP, not SQLite. But this is a UX question more than a technical one.

6. **Versioning of the daemon protocol.** `dark` and `darkd` may be different builds during a partial upgrade. Need a small version-handshake.

7. **`dark app data <name>` opening sqlite3:** is this how we want users to debug, or is this dangerous (they can write garbage)? Maybe `dark app data <name> --readonly` is the default, with `--rw` as opt-in.

8. **Backup strategy.** `ops.db` is the only thing that needs to be backed up (everything else rebuilds). `litestream` or `cron + cp` works. Does the daemon need to coordinate with the backup agent (e.g. a brief `BEGIN IMMEDIATE` hold to make sure WAL is checkpointed)? Probably yes.

9. **Tracing of the daemon itself.** A long-running F# process should expose at least basic `/healthz` / `/metrics`. Where do those go? OpenTelemetry? Plaintext? Per the vault `Tracing/` notes, this is wide-open design space.

10. **What does the LSP do?** The VS Code extension talks to the package manager directly today (or via some bridge). With a daemon, the LSP becomes a daemon client too. This is a separate project but one this rewrite *enables* — the daemon is the natural LSP backend.

---

## Closing

The current architecture is one DB pretending to be many. Pretending costs us: cache invalidation footguns, migration treadmills, single-writer contention, no sync-by-construction, no apps, no daemon, no sessions. The pretense is held together by "every CLI invocation is fresh," which we already want to stop.

The proposed architecture is many DBs, each modeling its actual lifetime:

- **Ops** are the past, immutable, the only thing to ship around.
- **Projections** are the present, derived, disposable, one per branch.
- **App data** is the future of running things, owned, exposed, val.town-style.
- **Traces** are observability, ephemeral, schema-volatile, isolated.
- **Sessions** are workflow state, local, tiny.

The daemon is what lets all these pieces live as long as they should, instead of being torn down per CLI command. The CLI becomes a thin client. Sync becomes "drag rows around." Migrations become "delete and rebuild." Apps become a real concept. Tracing becomes cheap to evolve.

The biggest single bet: **content-addressed ops + cheap projection rebuilds means we can stop being precious about schema.** Today, every schema change is a global migration that walks `data.db`. Tomorrow, every schema change is "drop the projection." That changes how aggressively we can iterate on every layer above ops.

Worth doing.
