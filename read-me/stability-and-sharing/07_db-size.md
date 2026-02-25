# DB Size Optimization: PT-Only Shipping

## Current DB Breakdown

| Variant | Raw | Gzipped | Contents |
|---------|-----|---------|----------|
| Full DB | 12.0MB | 4.7MB | Everything (ops + PT content + RT content + locations + deps + indexes) |
| PT-only (RT stripped) | 7.5MB | 3.1MB | Ops + PT content + locations + deps, RT columns emptied |
| Ops-only | 3.9MB | 1.5MB | Just ops + branches + commits, no projection tables |
| Content-only (no ops) | 2.5MB | 1.3MB | Just content tables, no history |

### Where the bytes go

| Data | Size | Notes |
|------|------|-------|
| Op blobs (`package_ops.op_blob`) | 1.9MB | Source of truth |
| PT content (`pt_def` columns) | 1.7MB | Derived from Add ops — ~90% overlap with op blobs |
| RT content (`rt_def`, `rt_instrs`, `rt_dval`) | 1.5MB | Derived from PT via conversion/evaluation |
| Indexes | ~3.5MB | 20+ indexes for queries |
| SQLite overhead (free pages, etc) | ~3.4MB | Vacuum helps but doesn't eliminate |

**Key insight: the DB stores everything 3 times** — once in op blobs, once in PT content tables, once in RT content tables. Op blobs and PT content tables are ~90% the same data (the Add ops contain the PT definitions).

## What's Essential vs Derivable

| Data | Status | How to reconstruct |
|------|--------|-------------------|
| `package_ops` (op blobs) | **Source of truth** | Cannot be derived — this IS the history |
| `commits`, `branches` | **Essential metadata** | Small, must ship |
| `package_types.pt_def` | Derived | Replay `AddType` ops |
| `package_types.rt_def` | Derived from PT | `PT2RT.PackageType.toRT` (trivial, <1ms/type) |
| `package_functions.pt_def` | Derived | Replay `AddFn` ops |
| `package_functions.rt_instrs` | Derived from PT | `PT2RT.PackageFn.toRT` (bytecode compilation, 1-50ms/fn) |
| `package_values.pt_def` | Derived | Replay `AddValue` ops |
| `package_values.rt_dval` | Derived from PT | `evaluateAllValues` (actual execution, 10-100ms/value) |
| `locations` | Derived | Replay `SetName` ops |
| `package_dependencies` | Derived | `DependencyExtractor` on content items |

## Recommended Approach: Ship Ops-Only, Reconstruct on Client

**Ship:** Just `package_ops` + `commits` + `branches` (1.5MB gzipped).

**Client reconstructs on first startup:**
1. Replay all ops via `insertAndApplyOps` → populates PT content tables + locations + deps
2. Run PT→RT conversion → populates `rt_def` and `rt_instrs`
3. Run `evaluateAllValues` → populates `rt_dval` and `value_type`

This is essentially what `reloadPackages` already does, but from ops instead of `.dark` files. The ops are already in the DB; playback just applies them.

**Alternatively, for ongoing operation:** Make RT columns lazy so they're derived on first access.

## Lazy RT Derivation (for ongoing operation, not just seed DB)

The caching layer already exists (`LibPackageManager/Caching.fs`). Currently, RT items are loaded from DB on first access and cached in a `ConcurrentDictionary`. Adding lazy derivation is straightforward:

### Schema changes needed

```sql
-- package_types: change rt_def from NOT NULL to nullable
ALTER TABLE package_types ALTER COLUMN rt_def DROP NOT NULL;
-- (SQLite doesn't support ALTER COLUMN; need to recreate table or add new migration)

-- package_functions: same for rt_instrs
ALTER TABLE package_functions ALTER COLUMN rt_instrs DROP NOT NULL;

-- package_values: rt_dval already nullable (no change needed)
```

### Lazy loading pattern

```fsharp
// In RuntimeTypes.fs cache layer:
getType = withCache (fun id ->
  uply {
    match! PMRT.Type.get id with
    | Some rt -> return Some rt  // RT exists in DB, use it
    | None ->
      // RT missing — derive from PT
      match! PMPT.Type.get id with
      | Some pt ->
        let rt = PT2RT.PackageType.toRT pt
        // Optionally write back to DB for next time
        do! storeRtDef id (BS.RT.PackageType.serialize id rt)
        return Some rt
      | None -> return None
  })
```

### Cost of lazy derivation

| Item | Count | Per-item cost | Total first-load cost |
|------|-------|---------------|----------------------|
| Types | 592 | <1ms (trivial struct mapping) | <1s |
| Functions | 2,128 | 1-50ms (bytecode compilation) | 5-30s |
| Values | 193 | 10-100ms (actual execution, multi-pass) | 5-20s |

**Total first-load cost: ~10-50 seconds.** This only happens once — after that, RT data is in the DB (if written back) or in-memory cache.

For functions, there's already a second cache layer at the interpreter level (`vm.packageFnInstrCache` in `Interpreter.fs:178-187`), so even without DB write-back, compilation only happens once per process lifetime.

### Values are already lazy

`package_values.rt_dval` is already nullable. `applyAddValue` stores NULL. `evaluateAllValues` fills it in. The pattern already exists — just needs to be generalized to types and functions.

## Recommendation

**For seed DB / clone shipping:**
1. Ship ops-only DB (1.5MB gzipped) — minimal size
2. Client runs op replay + RT derivation + value evaluation on first startup
3. This takes ~10-50 seconds once, then the DB is fully populated

**For ongoing sync (push/pull):**
1. Ops travel over the wire (already the plan)
2. Receiving side applies ops (generates PT content + locations + deps automatically)
3. RT derivation happens lazily on first access (with write-back to DB)
4. Value evaluation runs after op application for any new NULL values

**Schema migration:** Make `rt_def` and `rt_instrs` nullable. This is a simple migration and is backwards-compatible (existing code already handles the nullable `rt_dval` pattern).

**The LibSerialization rename can be the first step** — it's independent of everything else and sets up the right namespace structure for JSON serialization.

## Gap: Branches and Commits Are Not Ops

For ops-only shipping to work, EVERYTHING in the DB must be reconstructable from the op log. Currently two critical tables are NOT projections of ops:

**`branches`** — Created/modified by direct SQL: `scmBranchCreate`, merge (sets `merged_at`), rebase (updates `base_commit_id`), delete. These are all direct mutations, not logged as ops.

**`commits`** — Created by `commitWipOps` as direct SQL inserts. They're metadata grouping ops together. The `commit_id` column on `package_ops` references `commits.id`.

**What this means:** An ops-only DB cannot reconstruct branch existence, hierarchy, or commit groupings. You need EITHER:

1. **Ship ops + branches + commits** (still small — branches/commits are ~10 rows each), OR
2. **Make branch/commit lifecycle into ops** so everything is derivable

Option 2 is the cleaner long-term answer. A separate `BranchOp` type:
```
BranchOp:
  | CreateBranch of id * name * parentBranchId * baseCommitId
  | RebaseBranch of branchId * newBaseCommitId
  | MergeBranch of branchId * mergedAt
  | DeleteBranch of branchId
  | CreateCommit of id * message * branchId * createdAt
```

These would go in a `branch_ops` table, and `branches`/`commits` would become projection tables — same pattern as package content tables are projections of package ops.

**For MVP:** Ship ops + branches + commits as-is. Convert to branch ops later.

