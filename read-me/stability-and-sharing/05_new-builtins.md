# New Builtins Needed for Sync

## What Darklang Code Can Already Do

The CLI context already provides:
- **HTTP server/client** — binary bodies, routing, full request/response support
- **File I/O** — `fileRead`, `fileWrite`, `fileExists`, etc. (for snapshot/clone)
- **Branch/commit operations** — `scmBranchCreate`, `scmBranchList`, `scmGetCommits`, `scmGetCommitOps`, `scmCommit`, `scmMerge`, etc.
- **Op insertion** — `scmAddOps` (WIP only), `scmGetRecentOps`, `scmGetWipOps`
- **Package queries** — `pmFindType`, `pmGetValue`, `pmFindFn`, etc.
- **Binary data** — `List<UInt8>` manipulation, Base64, file read/write as bytes

## What's Missing (new F# builtins needed)

### 1. `scmInsertCommittedOps` — Insert ops as already-committed
**Why:** `scmAddOps` only inserts as WIP. For sync, pulled ops are already committed on the remote and need to arrive committed locally.

**Signature:**
```
scmInsertCommittedOps(branchId: Uuid, commitId: Uuid, commitMessage: String, ops: List<PackageOp>) -> Result<Int64, String>
```

**Implementation:** Create commit record with the given ID, then call existing `insertAndApplyOps(branchId, Some commitId, ops)`. ~20 lines of F# in `BuiltinPM/Libs/PackageOps.fs`.

**Note:** `insertAndApplyOps` already handles committed ops (takes `commitId: Option<Guid>`). The WIP-only restriction is in the builtin layer, not the core logic.

### 2. `scmGetOpsSinceCommit` — Get all committed ops on a branch after a given commit
**Why:** For pull responses. Server needs to know "what ops does the client not have yet?" Currently `getCommitOps` gets ops for ONE commit. Need: all ops since a point.

**Signature:**
```
scmGetOpsSinceCommit(branchId: Uuid, sinceCommitId: Uuid) -> List<(Commit * List<PackageOp>)>
```

**Implementation:** SQL query joining `package_ops` and `commits` where `commits.created_at > (SELECT created_at FROM commits WHERE id = sinceCommitId)`. ~30 lines.

### 3. `scmGetBranchLatestCommit` — Get the most recent commit on a branch
**Why:** For push/pull precondition checks. Client needs to compare its latest commit with server's latest.

**Signature:**
```
scmGetBranchLatestCommit(branchId: Uuid) -> Option<Commit>
```

**Implementation:** `SELECT * FROM commits WHERE branch_id = ? ORDER BY created_at DESC LIMIT 1`. ~15 lines.

### 4. `fileReadBinary` / snapshot endpoint support
**Why:** Clone needs to download `data.db` as a binary file and write it to disk. File I/O builtins already exist (`fileRead`/`fileWrite` work with `List<UInt8>`), so this may already be sufficient. But for a 12MB file, `List<UInt8>` (heap-allocated list of boxed bytes) might be slow.

**Option A:** Use existing `fileRead`/`fileWrite` — works but may be slow for large files.
**Option B:** Add `fileCopy(src, dst)` builtin — simpler and faster for snapshot download (just copy the DB file directly).

~10 lines.

### 5. `scmCreateBranchFromRemote` — Create a branch with a specific ID and base_commit_id
**Why:** When pulling a branch that doesn't exist locally. `scmBranchCreate` generates a new UUID; for sync we need the branch to have the same UUID as on the server.

**Signature:**
```
scmCreateBranchFromRemote(id: Uuid, name: String, parentBranchId: Uuid, baseCommitId: Uuid) -> Result<Unit, String>
```

**Implementation:** Direct SQL insert into `branches` table. ~15 lines.

## What Does NOT Need New Builtins

- **Op serialization/deserialization** — ops travel as Darklang values (via `PT2DT.PackageOp.toDT`/`fromDT`), not raw binary. The builtins handle conversion.
- **Content items** — content tables (`package_types`, `package_values`, `package_functions`) are updated automatically by `PackageOpPlayback.applyOps` when ops are inserted. No need to sync content separately!
- **Location records** — also created by `PackageOpPlayback.applyOps`. No need to sync separately!
- **Dependencies** — also created by `applyOps`. No need to sync separately!
- **Merge/rebase** — existing builtins (`scmMerge`, rebase logic) work fine.

## Key Insight: Op Playback Handles Everything

The most important realization: **you only need to sync ops**. When ops are inserted via `insertAndApplyOps`, the playback system automatically:
- Creates/updates content items in `package_types`/`package_values`/`package_functions`
- Creates/updates location records
- Creates/updates dependency records

This means push/pull only needs to transfer **commit records + op blobs**. Not content items, not locations, not dependencies. Much simpler than originally planned!

## Data Size Context (from current DB)

| Metric | Value |
|--------|-------|
| Total `data.db` size | 12 MB |
| Total ops | 5,826 |
| Avg op blob size | 327 bytes |
| Max op blob size | 31 KB |
| Total op blob size | 1.9 MB |
| Commits | 3 |
| Branches | 10 |
| Locations | 2,913 |
| Content items (types+fns+values) | 2,913 |
| Dependencies | 9,961 |

A typical push/pull for a branch with ~100 ops: ~33KB of op data. Trivial over HTTP.
Clone (full DB download): ~12MB. Fine even on slow connections.

## Total New F# Code Estimate

~90 lines of F# across 5 new builtins. All following existing patterns in `BuiltinPM/Libs/PackageOps.fs` and `LibPackageManager/Queries.fs`.
