# Implementation Plan

Concrete steps, ordered by dependency. Each step is small enough to be a single PR or work session.

## Phase 0: Quick Wins (no new features, just stop losing things)

### 0a. Stop purging old CLI releases
**What:** Delete lines 122–172 in `scripts/deployment/publish-github-release` (the "Delete old releases" section).
**Why:** Every release currently deletes all previous releases. If a binary serialization format change breaks things, there's no way to roll back to an older CLI.
**Effort:** 5 minutes. Delete the code, test manually.
**Risk:** GitHub releases will accumulate (~100KB per platform × 3 platforms per release). Negligible storage.
**File:** `scripts/deployment/publish-github-release`

### 0b. Add format version infrastructure to binary serialization
**What:** Modify `makeDeserializer` to pass the header version to reader functions, so readers CAN dispatch on version (even though they don't need to yet).
**Why:** Foundation for all future format changes. Without this, every PT change is a breaking change.
**Files to change:**
1. `BinarySerialization.fs`: Change `makeDeserializer` — reader function gets `(BinaryReader -> uint32 -> 'T)` instead of `(BinaryReader -> 'T)`. Pass `header.Version` through.
2. All reader entry points (11 modules): Add `version` parameter, ignore it for now — `let read (r: BinaryReader) (_version: uint32) = ...`
3. `RT.ValueType.serialize/deserialize`: Add header (currently headerless). This is the one special case.
4. `BinaryFormat.fs`: Optionally bump `CurrentVersion` to `2u` to distinguish "version-aware" blobs from old `1u` blobs. Or leave at `1u` until there's an actual format change.

**Effort:** ~2 hours. Mechanical refactoring — every reader gets an extra parameter.
**Risk:** Low. The existing `validateVersion` already accepts `version <= CurrentVersion`, so old blobs (version=1) still work. New blobs written with the same version value are identical.

### 0c. Document the tag registry
**What:** Add a comment block at the top of each serializer file listing all tag assignments (copy from the tables in [03_binary-serialization.md](03_binary-serialization.md)).
**Why:** When someone adds a new union variant, they need to know which tag number to use next. Currently you have to count through the match statement.
**Effort:** 30 minutes. Copy-paste from the tables.

## Phase 1: Server

### 1a. Create the `server` CLI command
**What:** Add a `server` command to `Registry.allCommands` in `packages/darklang/cli/core.dark`. The command starts an HTTP server with a sync router.
**Pattern to follow:** The existing `http-server` command in `packages/darklang/cli/commands/http-server.dark`.

**New files:**
- `packages/darklang/cli/commands/server.dark` — command handler
- `packages/darklang/cli/server/router.dark` — sync router with endpoints
- `packages/darklang/cli/server/handlers.dark` — push/pull/clone handler implementations

**Minimal first version:**
```darklang
// server.dark
let execute (state: Cli.AppState) (args: List<String>) : Cli.AppState =
  match args with
  | ["start"; portStr] ->
    match Stdlib.Int64.parse portStr with
    | Ok port ->
      Stdlib.printLine (Colors.success $"Sync server listening on http://localhost:{port}")
      Stdlib.HttpServer.serve port Server.Router.router
      state
    | Error _ ->
      Stdlib.printLine (Colors.error $"Invalid port: {portStr}")
      state
  | _ ->
    Stdlib.printLine "Usage: server start <port>"
    state
```

**Effort:** ~1 day for skeleton + 2-3 days for full handler implementation.
**Depends on:** Nothing (can start immediately).

### 1b. Implement `GET /snapshot` (clone)
**What:** Server endpoint that returns the entire `data.db` file as a binary response.
**Why:** This is the simplest sync operation and enables `dark clone`. Getting this working first proves the HTTP binary transport layer.

**Server side:** Read `data.db` file → return as response body.
**Client side:** `dark clone <url>` → `Stdlib.HttpClient.get(url ++ "/snapshot", [])` → write to `rundir/data.db`.

**Effort:** ~1 day.
**Depends on:** 1a (server command exists).

### 1c. Implement `GET /branches` and `GET /branches/:id/commits`
**What:** Server endpoints for listing branches and commits. JSON responses.
**Why:** Needed for push/pull precondition checks (is my branch up-to-date?).

**Implementation:** SQL queries against the server's DB → JSON response.

**Effort:** ~1 day.
**Depends on:** 1a.

### 1d. Add sync builtins (~90 lines of F#)
**What:** Add 5 new F# builtins to `BuiltinPM/Libs/PackageOps.fs`:
1. `scmInsertCommittedOps(branchId, commitId, commitMessage, ops)` — insert already-committed ops
2. `scmGetOpsSinceCommit(branchId, sinceCommitId)` — get ops for pull responses
3. `scmGetBranchLatestCommit(branchId)` — get latest commit for precondition checks
4. `scmCreateBranchFromRemote(id, name, parentBranchId, baseCommitId)` — create branch with specific ID
5. `fileCopy(src, dst)` — efficient file copy for snapshot (optional, can use existing fileRead/fileWrite)

**Key insight from code review:** `insertAndApplyOps` already supports committed ops — it takes `commitId: Option<Guid>`. And op playback automatically updates content tables, locations, and dependencies. **Only ops need to travel over the wire.** No need to sync content items, locations, or deps separately.

**Effort:** ~1 day. All follow existing patterns in the codebase.
**Depends on:** Nothing. Can be done now.
**See:** [05_new-builtins.md](05_new-builtins.md) for full details.

### 1e. Implement `dark push` (basic)
**What:** Push committed ops from a local branch to the server.

**Client flow (Darklang code):**
1. Check preconditions: no WIP, branch exists locally
2. `GET /branches/:id/latest-commit` to check if server is ahead
3. If server is ahead → error "pull first"
4. Get ops since server's latest commit using `scmGetOpsSinceCommit`
5. Send ops as JSON (Darklang values serialize to JSON naturally) via `Stdlib.HttpClient.post`
6. Print success

**Server flow (Darklang code):**
1. Parse ops from request body
2. Check preconditions (branch up-to-date)
3. Call `scmInsertCommittedOps` to insert and apply ops
4. Return success

**Major simplification:** Ops are Darklang values. They can travel as JSON (using existing JSON serialization). No custom binary envelope needed! Op playback on the receiving side automatically handles content tables, locations, and dependencies.

**New files:**
- `packages/darklang/cli/commands/push.dark`
- `packages/darklang/cli/server/handlers.dark`

**Effort:** ~2-3 days.
**Depends on:** 1a, 1c, 1d.

### 1f. Implement `dark pull` (basic)
**What:** Pull new committed ops from server to local.

**Client flow (Darklang code):**
1. Get local latest commit ID
2. `GET /branches/:id/ops?since=<last_known_commit>` from server
3. Parse response (list of commits + ops as JSON)
4. For each commit: call `scmInsertCommittedOps` to insert and apply

**Server flow (Darklang code):**
1. Call `scmGetOpsSinceCommit(branchId, sinceCommitId)`
2. Return commits + ops as JSON

**Effort:** ~2-3 days.
**Depends on:** 1a, 1c, 1d. Can be developed in parallel with 1e.

### 1g. Server backup
**What:** Automated backup of the server's `data.db`.
**Implementation:** systemd timer + shell script that runs `sqlite3 data.db ".backup /backups/data-$(date).db"` + rotation.
**Effort:** ~2 hours.
**Depends on:** 1a (server running).

### 1h. Value re-evaluation after sync
**What:** After applying pulled ops, call `evaluateAllValues` (or a scoped version) to evaluate any newly-created values that have `rt_dval = NULL`.
**Why:** `applyAddValue` always stores NULL for `rt_dval`. Currently `evaluateAllValues` only runs during initial package load from disk (`LocalExec.reloadPackages`). Without this, any values created by pulled ops (including propagation-generated values) are **unusable** — the CLI throws `ValueNotFound` when trying to access them.
**Implementation:** Add a `scmEvaluateNewValues` builtin that calls the existing multi-pass evaluation logic from `evaluateAllValues`, scoped to rows where `rt_dval IS NULL`.
**Effort:** ~0.5 day. Mostly reusing existing `evaluateAllValues` logic.
**Depends on:** 1e or 1f (push or pull working).

### 1i. Rename LibBinarySerialization → LibSerialization
**What:** Rename the project and add a `Json` sub-namespace for sync wire format serialization. See [04_serialization-rename.md](04_serialization-rename.md).
**Why:** JSON serialization for sync ops needs a natural home. Binary and JSON serialization share infrastructure (PT type definitions, common patterns).
**Implementation:** Mechanical rename across 22 internal files + 5 dependent projects. Then add `Json/` subdirectory with PackageOp JSON serializer.
**Effort:** ~1.5 days.
**Depends on:** Nothing. Can be done as a standalone PR.

## Phase 2: Bootstrap

### 2a. Add `generate-seed-db` command to LocalExec
**What:** New command in `backend/src/LocalExec/LocalExec.fs` that runs the full package loading pipeline and outputs a `seed.db` file.

```fsharp
| [ "generate-seed-db"; outputPath ] ->
    handleCommand "Generate seed DB" (HandleCommand.generateSeedDb outputPath)
```

The implementation is essentially the existing `reload-packages` flow + copying `data.db` to `outputPath`.

**Effort:** ~2 hours.
**Depends on:** Nothing.

### 2b. Add seed DB to CI release
**What:** Add a step in `.circleci/config.yml` after `build-cli` that runs `generate-seed-db` and uploads `seed.db` as an additional release asset.
**Effort:** ~2 hours.
**Depends on:** 2a.

### 2c. CLI detects missing `data.db` on startup
**What:** When the CLI starts and `rundir/data.db` doesn't exist, offer to download the seed DB from the latest GH release or clone from a server.
**Effort:** ~1 day.
**Depends on:** 2a, 2b, and ideally 1b (clone).

### 2d. Default branch config for git↔dark
**What:** CLI reads `DARK_BRANCH` env var or `.darklang-branch` file to set `currentBranchId` on startup.
**Where:** `initState()` in `packages/darklang/cli/core.dark` — add a branch lookup before falling back to `mainBranchId`.
**Effort:** ~2 hours.
**Depends on:** Nothing.

## Phase 3: Cleanup (future)

### 3a. Server-side merge
**What:** `POST /branches/:id/merge` endpoint that performs merge on the server (avoids client-server state divergence on merge operations).
**Depends on:** 1d, 1e working.

### 3b. Eliminate `packages/` as runtime dependency
**What:** Once the server is stable and seed DB is in CI, remove `.dark` files from the repo (or move to a `bootstrap/` subdirectory). CI generates seed DB by cloning from server instead of parsing `.dark` files.
**Depends on:** Everything else.

### 3c. `.db` snapshot browser
**What:** Additional server endpoints for listing and downloading backup snapshots.
**Depends on:** 1f (backups exist).

## Minimal Viable Sync (what to build first)

If the goal is "I want to push/pull with my coworker as soon as possible", the critical path is:

```
0a (5 min) → 1d (1 day, F# builtins) → 1a (1 day, server command) → 1b (1 day, clone) → 1c (1 day) → 1e + 1f (4-6 days, push + pull)
```

Total: ~1.5 weeks of focused work to get basic push/pull working.

**Key simplification discovered during research:** Only ops need to travel over the wire. Op playback on the receiving side automatically updates content tables, locations, and dependencies. Ops are Darklang values that can travel as JSON. No custom binary envelope format needed.

Clone (`1b`) alone is useful even without push/pull — it lets you copy a working DB from one machine to another.

Phase 0 items (0a, 0b, 0c) are independent and can be done anytime. They're low-effort safety improvements.

Phase 2 items are nice-to-have and can come after sync is working.
