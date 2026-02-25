# Bootstrapping

## The Problem
Today: empty DB → run migrations → parse all `.dark` files from `packages/` → working system. This happens on every startup. It's slow (~10s) and breaks whenever PT changes make old binary blobs unreadable.

Goal: no `.dark` files in the repo. All package code lives in the DB. But you need a DB to run the CLI, and you need the CLI to get a DB from the server.

## Current Pipeline (from code review)

### LoadPackagesFromDisk (`backend/src/LocalExec/LoadPackagesFromDisk.fs`)
Two-pass parsing with ID stabilization:

1. **First pass** (`OnMissing.Allow`): Parse all `.dark` files from `/home/dark/app/packages` (hardcoded container path). Forward references allowed — other package items won't be resolved yet. Produces `List<PackageOp>`.

2. **Second pass** (`OnMissing.ThrowError`): Re-parse same files with first-pass ops available via `withExtraOps`. All names must resolve. Produces refined ops.

3. **Stabilization**: `stabilizeOpsAgainstPM` adjusts second-pass IDs to match first-pass IDs (deterministic content-addressed UUIDs). This ensures same `.dark` files → same UUIDs every time.

### Purge (`backend/src/LibPackageManager/Purge.fs`)
Deletes everything from: `locations`, `package_types`, `package_values`, `package_functions`, `package_ops`, `package_dependencies`, `commits`. Simple DELETE statements in a transaction. Does NOT touch `branches` table (main branch is well-known: `89282547-e4e6-4986-bcb6-db74bc6a8c0f`).

### Insert and Apply (`backend/src/LibPackageManager/Inserts.fs`)
Three-phase op application:
1. Insert ops into `package_ops` with `applied=false` (content-addressed hash ID from SHA256 of binary blob)
2. Apply to projection tables via `PackageOpPlayback.applyOps` (updates `package_types`, `package_values`, `package_functions`, `locations`, `package_dependencies`)
3. Mark ops as `applied=true`

### Value Evaluation (`backend/src/LocalExec/LocalExec.fs` lines 23–143)
Multi-pass evaluation (max 10 passes):
1. Query rows where `rt_dval IS NULL`
2. For each: deserialize `pt_def` → convert to RT instructions → execute
3. On success: serialize result → `rt_dval` column, compute `value_type`
4. On failure: retry next pass (dependency might resolve)
5. Stop when all done or no progress

### Package Stats
- **312 `.dark` files** in `packages/`
- **~51,000 lines of code**, **~2.6 MB**
- Largest files: CLI core (~8k lines), language tools (~2.3k lines)

## Seed DB

Ship a pre-built `data.db` as a release artifact alongside the CLI binary.

### How it's made
During the release process (CI):
1. Start with empty DB
2. Run migrations (5 migration files, schema is stable)
3. Parse `.dark` files from `packages/` — the one time this happens
4. Purge + insert ops with commit on `main` branch
5. Evaluate all values (multi-pass until all `rt_dval` populated)
6. Package the resulting `data.db` as `seed.db` (or compressed: `seed.db.zst`)
7. Upload alongside the CLI binary in the GH release

**Implementation:** Add a `generate-seed-db` command to `LocalExec.fs` that runs steps 1–5, then copies the DB file to an output path. CI calls this after building the CLI binary.

### How it's used

**Fresh install (no existing DB, no server access)**:
1. Download CLI binary + `seed.db`
2. Drop `seed.db` into `rundir/data.db`
3. CLI starts — no parsing, no `.dark` files needed
4. `dark pull` to get latest from server (if server exists)

**Fresh install (server available)**:
1. Download CLI binary
2. `dark clone <server-url>` — downloads server's `data.db`
3. Done — no seed DB needed, no `.dark` files needed

**Upgrade**:
1. Download new CLI binary (+ new seed DB, but probably won't need it)
2. Binary serialization migration handles format changes (see [03_binary-serialization.md](03_binary-serialization.md))
3. If migration fails catastrophically: replace `data.db` with new seed DB, lose local-only state, re-pull from server

### When `.dark` files are still needed
- During the release build (to generate the seed DB)
- As an emergency recovery fallback
- During internal development of Dark itself (editing stdlib, etc.) — until the server exists and local dev can pull from it

### Eliminating `.dark` files from the repo (endgame)
Once the server exists:
1. Stdlib lives in the server's DB (the canonical source)
2. CI generates the seed DB by cloning from the server, not by parsing `.dark` files
3. `.dark` files can be removed from the repo
4. Editing stdlib = edit in the CLI (or VS Code), push to server
5. Release process = clone from server → package as seed DB → ship

The `.dark` files were always a bootstrapping crutch. The server is what replaces them.

## Local Dev (building Dark itself)

When you're working on the F# backend and need a working `data.db`:
- Pull a `.db` snapshot from the server (or the small browsing site on the server)
- Or use the seed DB from the latest release
- If you're changing PT: you'll need to re-generate the DB (parse `.dark` files or run migration). This is the one context where `.dark` files remain useful during the transition period.

**Note:** The packages path is hardcoded to `/home/dark/app/packages` (container path). For seed DB generation in CI, this works fine (CI runs in Docker). For local dev outside Docker, this path would need to be configurable or the Docker container used.

## Previous attempts
Bootstrapping has been attempted before and failed. The key difference this time: the central server provides an alternative bootstrap path that doesn't depend on `.dark` files at all (`dark clone`). The seed DB is a fallback for when the server isn't available.

## TODOs
- [ ] Add `generate-seed-db` command to `LocalExec.fs`
- [ ] Add seed DB generation step to CI (`.circleci/config.yml`, after `build-cli`)
- [ ] Upload `seed.db` as additional release asset in `publish-github-release`
- [ ] CLI: detect "no data.db" on startup → offer to use seed DB or clone from server
- [ ] CLI: `dark clone` implementation (see [01_sync.md](01_sync.md))
- [ ] Once server is stable: stop including `.dark` files in the repo (or move to a separate bootstrap-only repo)
