# Add Branches to SCM

## Context

Darklang has built-in SCM for the package tree. Currently: WIP + commits on a single timeline (main). Adding branches with rebase and merge.

### Key Rules
- Must rebase before merge. Merge is always safe (no conflicts possible after rebase).
- No force-rebase. Conflicts must be manually resolved.
- Branches can be created from any branch. Merge always goes to parent branch.
- Before any commit, user confirms they've tested. (Future: automated checks.)
- We don't care about existing data. Rewrite migrations, delete data.db. LocalExec reruns migrations and reloads packages from .dark files.

### Core Model: Items vs Locations
- **Items** (fn bodies, type declarations, values) are global/branch-agnostic. Content-addressed by UUID.
- **Locations** (name bindings: `owner.modules.name` → UUID) are branch-scoped.
- `SetTypeName` on branch B creates a location on B. Visible to B and B's children. Not visible to parent or siblings.
- **WIP** locations are private to their branch. Not inherited by children.
- **Committed** locations are inherited downward (parent → child).

### Location Visibility (name resolution on branch B)
1. B's WIP locations
2. B's committed locations
3. Parent's committed locations (not WIP!)
4. Grandparent's committed locations
5. ... up the chain
6. Main's committed locations

### Not in scope
- Accounts/users, instances/sync, approvals/PRs
- Stash, branch diff (TODO: defer)

### Reference
Old (deleted) implementation: `git show c36ab3f6f^:<path>`
- `LibPackageManager/Branches.fs`, `BuiltinPM/Libs/Branches.fs`
- `packages/darklang/scm/branch.dark`, `packages/darklang/cli/packages/branch.dark`

---

## Layer-by-Layer Design

### Layer 1: ProgramTypes.fs

```fsharp
/// SCM branch identifier
type BranchId = uuid
```

That's it. The `Branch` record type lives in `LibPackageManager/Branches.fs` (storage concept, not language concept). `BranchId` is in PT because it's needed by `PackageManager` construction and will appear in function signatures throughout.

`PackageManager` type itself does NOT change signature. The `find*` / `getLocation` / `search` closures already hide their implementation. When constructing a PM for a branch, the branch chain is captured in the closures. Switching branches = reconstruct the PM (flush caches, new closures with new branch context).

`RuntimeTypes.fs` - no changes. RT.PackageManager takes UUIDs → content, which is branch-agnostic.

### Layer 2: SQL

No migration compatibility needed. Rewrite the package schema migration.

**Explicit "main" branch**: Instead of NULL-means-main, create a well-known main branch row. Every row always has a `branch_id`. No NULL special-casing in queries.

**Drop `is_wip`**: Redundant with `commit_id IS NULL`. Remove from schema entirely.

```sql
CREATE TABLE branches (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL UNIQUE,
  parent_branch_id TEXT REFERENCES branches(id),  -- main has NULL here
  base_commit_id TEXT REFERENCES commits(id),     -- fork point on parent
  created_at TIMESTAMP NOT NULL DEFAULT (datetime('now')),
  merged_at TIMESTAMP                             -- NULL until merged
);

-- Well-known main branch (inserted by migration)
INSERT INTO branches (id, name) VALUES ('MAIN_BRANCH_UUID', 'main');

-- package_ops: branch_id is NOT NULL (always set)
CREATE TABLE package_ops (
  id TEXT PRIMARY KEY,
  op_blob BLOB NOT NULL,
  branch_id TEXT NOT NULL REFERENCES branches(id),
  commit_id TEXT REFERENCES commits(id),  -- NULL = WIP, non-NULL = committed
  applied INTEGER NOT NULL DEFAULT 0,
  created_at TIMESTAMP NOT NULL DEFAULT (datetime('now'))
);

-- locations: branch_id is NOT NULL
CREATE TABLE locations (
  location_id TEXT PRIMARY KEY,
  item_id TEXT NOT NULL,
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,
  name TEXT NOT NULL,
  item_type TEXT NOT NULL,
  branch_id TEXT NOT NULL REFERENCES branches(id),
  commit_id TEXT REFERENCES commits(id),  -- NULL = WIP
  created_at TIMESTAMP NOT NULL DEFAULT (datetime('now')),
  deprecated_at TIMESTAMP NULL
);

-- commits: branch_id is NOT NULL
CREATE TABLE commits (
  id TEXT PRIMARY KEY,
  message TEXT NOT NULL,
  branch_id TEXT NOT NULL REFERENCES branches(id),
  created_at TIMESTAMP NOT NULL DEFAULT (datetime('now'))
);

-- Content tables: NO branch_id (content is global)
-- package_types, package_values, package_functions stay as-is

-- Key indexes
CREATE INDEX idx_locations_branch_lookup
  ON locations(branch_id, owner, modules, name, item_type)
  WHERE deprecated_at IS NULL;

CREATE INDEX idx_locations_wip
  ON locations(branch_id)
  WHERE commit_id IS NULL;

CREATE INDEX idx_package_ops_wip
  ON package_ops(branch_id)
  WHERE commit_id IS NULL;

CREATE INDEX idx_commits_branch
  ON commits(branch_id, created_at DESC);
```

State matrix:
| branch_id | commit_id | Meaning |
|-----------|-----------|---------|
| main | NULL | WIP on main |
| main | X | Committed to main |
| B | NULL | WIP on branch B |
| B | X | Committed to branch B |

### Layer 3: LibPackageManager (F#)

**Branches.fs** (new):
```fsharp
type Branch =
  { id : BranchId
    name : string
    parentBranchId : Option<BranchId>  // None only for main
    baseCommitId : Option<uuid>
    createdAt : NodaTime.Instant
    mergedAt : Option<NodaTime.Instant> }

let mainBranchId : BranchId = (* well-known UUID *)

let create (name, parentBranchId) -> Branch
let get (id) -> Option<Branch>
let getByName (name) -> Option<Branch>
let list () -> List<Branch>
let rename (id, newName) -> Result
let delete (id) -> Result  // refuses if has children
let setMerged (id)
let getBranchChain (id) -> List<BranchId>
  // Returns [current; parent; grandparent; ...; main]
  // Used by name resolution queries
```

**ProgramTypes.fs** updates (the find/getLocation functions):

All `find*` and `getLocation` queries need branch-aware filtering. The branch chain is pre-computed and passed in. Single SQL query with `IN (...)` and priority ordering:

```sql
SELECT item_id FROM locations
WHERE owner = @owner AND modules = @modules AND name = @name
  AND item_type = @type AND deprecated_at IS NULL
  AND (
    branch_id = @current                              -- current: WIP + committed
    OR (branch_id IN (@parent, @grandparent, ..., @main)
        AND commit_id IS NOT NULL)                    -- ancestors: committed only
  )
ORDER BY
  CASE branch_id
    WHEN @current THEN 0
    WHEN @parent THEN 1
    ...
    WHEN @main THEN 99
  END,
  CASE WHEN commit_id IS NULL THEN 0 ELSE 1 END,     -- WIP before committed (current only)
  created_at DESC
LIMIT 1
```

Same pattern for `getLocation` (reverse lookup) and `search`.

**Rebase.fs** (new):

```
rebase(branchId):
  branch = getBranch(branchId)
  parentLatest = getLatestCommit(branch.parentBranchId)

  if branch.baseCommitId == Some parentLatest:
    return Ok "Already up to date"

  # Conflict = same location path modified on both sides since fork
  branchLocations = getLocationPathsModifiedSince(branchId, branch.baseCommitId)
  parentLocations = getLocationPathsModifiedSince(branch.parentBranchId, branch.baseCommitId)
  conflicts = intersection(branchLocations, parentLocations)

  if conflicts not empty:
    return Error conflicts

  UPDATE branches SET base_commit_id = parentLatest WHERE id = branchId
```

Note: rebase doesn't change what the branch sees (it already sees parent's latest). It's purely bookkeeping for conflict detection + merge eligibility. The 99% case (no conflicts) is one SQL UPDATE.

**Merge.fs** (new):

Preserves individual commits (moves them to parent, doesn't squash).

```
merge(branchId):
  branch = getBranch(branchId)

  # Must be rebased
  parentLatest = getLatestCommit(branch.parentBranchId)
  if branch.baseCommitId != Some parentLatest:
    return Error "Must rebase first"

  # Must have no WIP
  if hasWipOps(branchId):
    return Error "Commit or discard WIP first"

  # Must have no children
  if hasChildBranches(branchId):
    return Error "Merge or delete child branches first"

  parentId = branch.parentBranchId

  # For each branch location being moved to parent,
  # deprecate any existing parent location at the same path
  branchLocations = getActiveLocations(branchId)
  for loc in branchLocations:
    deprecateLocationAtPath(parentId, loc.owner, loc.modules, loc.name, loc.itemType)

  # Move commits, ops, and locations to parent
  UPDATE commits SET branch_id = parentId WHERE branch_id = branchId
  UPDATE package_ops SET branch_id = parentId WHERE branch_id = branchId
  UPDATE locations SET branch_id = parentId WHERE branch_id = branchId AND deprecated_at IS NULL

  # Mark branch as merged
  UPDATE branches SET merged_at = now() WHERE id = branchId
```

**Queries.fs** updates - all SCM queries get `branchId` parameter:
- `getWipOps(branchId)` → `WHERE branch_id = @branchId AND commit_id IS NULL`
- `getWipSummary(branchId)` → same filter
- `getCommits(branchId, limit)` → `WHERE branch_id = @branchId`
- `getCommitOps(commitId)` → no change (commit already has identity)
- `getLocationPathsModifiedSince(branchId, commitId)` → new, for conflict detection

**Inserts.fs** updates:
- `insertAndApplyOps` takes `branchId` parameter
- `commitWipOps(branchId, message)` scoped to branch
- `discardWipOps(branchId)` scoped to branch

**PackageOpPlayback.fs** updates:
- `applySetName` takes `branchId` parameter, creates locations with `branch_id`

**PackageManager.fs** updates:
- PM construction takes a `BranchId` and pre-computes the branch chain
- `find*` closures use the branch-aware query pattern above
- `getLocation` closures similarly branch-aware
- Branch switch = reconstruct PM (flush find/location caches; content caches by UUID can persist)

**Caching.fs** updates:
- `find*` cache keyed by `(BranchId, PackageLocation)` — or just flushed on branch switch
- `get*` cache (UUID → content) is branch-independent, no change needed

### Layer 4: Builtins (F#)

All SCM builtins take `branchId` explicitly (not server-side session state). The Darklang wrapper layer threads it through.

**Libs/Branches.fs** (new):
- `scmBranchCreate(name)` - creates from "current" branch (passed by caller)
- `scmBranchList()`, `scmBranchGet(id)`, `scmBranchGetByName(name)`
- `scmBranchRename(id, newName)`, `scmBranchDelete(id)`

**Libs/Rebase.fs** (new):
- `scmRebase(branchId)`, `scmGetRebaseConflicts(branchId)`

**Libs/Merge.fs** (new):
- `scmMerge(branchId)`, `scmCanMerge(branchId)`

**Libs/PackageOps.fs** updates - all functions gain `branchId` parameter:
- `scmAddOps(branchId, ops)`
- `scmGetWipOps(branchId)`, `scmGetWipSummary(branchId)`
- `scmCommit(branchId, message)`, `scmDiscard(branchId)`
- `scmGetCommits(branchId, limit)`

### Layer 5: Darklang Packages

**packages/darklang/scm/branch.dark** (new):
```
module Darklang.SCM.Branch
type Branch = { id: Uuid; name: String; parentBranchId: Option<Uuid>; ... }
create, list, get, getByName, rename, delete, getCurrent, switch, switchByName
```

**packages/darklang/scm/rebase.dark** (new):
```
module Darklang.SCM.Rebase
type Conflict = { locationPath: String; ... }
rebase, getConflicts
```

**packages/darklang/scm/merge.dark** (new):
```
module Darklang.SCM.Merge
type MergeError = | NotRebased | HasWip | HasChildren | NothingToMerge
merge, canMerge
```

**packages/darklang/scm/packageOps.dark** updates:
All functions thread `branchId` from caller (CLI state) to builtins.

### Layer 6: CLI Commands

**branch.dark** (new):
```
branch                    # show current branch, list all
branch create <name>      # create from current branch, switch to it
branch switch <name>      # switch to existing branch
branch rename <old> <new> # rename
branch delete <name>      # delete (--force if has WIP/commits but no children)
```

**rebase.dark** (new):
```
rebase                    # rebase current onto parent's latest
rebase --status           # show if needed + list conflicts
```

When rebase finds conflicts:
- Shows which location paths conflict
- User must: edit their branch's definitions to account for parent's changes, then `rebase` again
- There is no `rebase --force` or `rebase --resolve`. User just fixes their code and retries.

**merge.dark** (new):
```
merge                     # merge current branch to parent (must be rebased, no WIP)
merge --dry-run           # preview what would be merged
```

Commits are preserved (moved to parent, not squashed).

**Update existing commands:**
- `status` - show current branch name, show if rebase needed, then WIP summary
- `commit` - branch-scoped, add confirmation ("Have you tested? Proceed? (y/n)"). `--yes`/`-y` skips prompt (for scripts/AI).
- `log` - branch-scoped by default. `--all` shows all branches.
- `discard` - branch-scoped. Warn: "You have N WIP ops on [branch]. Discard? (y/n)"
- `show` - include branch name in commit display

### Layer 7: CLI State + Docs

**core.dark** AppState:
```
type AppState =
  { ...existing fields...
    currentBranchId: Uuid }  // defaults to main branch UUID
```

**core.dark** registry: add `branch`, `rebase`, `merge` commands to SCM group.

**Status bar**: show current branch name (not just "Darklang").

**help.dark**: update SCM section with branch/rebase/merge commands.

**docs/scm.dark**: rewrite with branch workflow:
```
## Workflow
  1. branch create feature   # create branch
  2. fn myFn                 # work on branch
  3. commit "msg"            # commit to branch
  4. rebase                  # sync with parent
  5. merge                   # merge to parent
```

**docs/for-ai.dark**: add branch awareness to SCM section.

---

## How Rebase Actually Works

Branch B always sees parent's latest committed state (name resolution walks the chain). `base_commit_id` is NOT a visibility boundary — it's only for:
1. **Conflict detection**: "what location paths changed on parent since `base_commit_id`?"
2. **Merge eligibility**: "is `base_commit_id` == parent's latest commit?"

Rebase = verify no conflicts + update the pointer. It does NOT replay ops or rewrite commits.

This means integration issues surface **as you work** (since you see parent's latest), not in a batch during rebase. Rebase just formalizes "yes, I've accounted for parent's changes."

99% case (no conflicting location paths): one SQL UPDATE. Done.
1% case: user sees conflicting paths, edits their definitions, retries.

### Cascading rebase
When parent A rebases, child B's `base_commit_id` becomes stale. B will need to rebase too. `rebase --status` should detect this (parent's latest != B's base). No automatic cascade — user rebases bottom-up.

---

## Merge Details

### What merge does
1. Verify rebased, no WIP, no children
2. For each branch location at a path that also exists on parent: deprecate parent's version
3. Move commits, ops, locations to parent (`UPDATE ... SET branch_id = parentId`)
4. Mark branch as merged (`merged_at = now()`)

Individual commits are preserved on the parent timeline.

### Branch lifecycle after merge
Merged branch becomes read-only (has `merged_at` set). Can be deleted. Cannot accept new WIP or commits.

### Branch deletion rules
- Cannot delete if has children (must delete/merge children first)
- If merged: just delete the branch row (data already on parent)
- If not merged with WIP/commits: require `--force`

---

## Implementation Guide

### Dev Environment

Everything auto-rebuilds on save. Never manually rebuild.

- **F# changes** (`backend/src/`): auto-build logs to `rundir/logs/build-server.log`. Takes ~1min.
- **Darklang changes** (`packages/`): auto-reload logs to `rundir/logs/packages-canvas.log`. Takes ~10s.
- F# build completion triggers a package reload too.

### Testing your changes

```bash
# Run CLI interactively to test commands
./scripts/run-cli

# Inside the CLI:
help                    # see all commands
status                  # test SCM commands
docs scm                # check docs

# Run backend tests
./scripts/run-backend-tests
./scripts/run-backend-tests --filter "branchName"
```

### Key log files (rundir/logs/)

Check these when something goes wrong:
- `cli.log` — CLI runtime errors
- `packages-canvas.log` — .dark file loading errors (check after editing packages/)
- `build-server.log` — F# build errors (check after editing backend/)
- `migrations.log` — schema migration issues

### Database

```bash
# Delete and recreate (migrations rerun automatically on next CLI start)
rm rundir/data.db

# Inspect directly
sqlite3 rundir/data.db ".tables"
sqlite3 rundir/data.db "SELECT * FROM branches;"
```

### Adding a CLI command (Darklang)

1. Create `packages/darklang/cli/commands/<name>.dark` (or `scm/<name>.dark`)
2. Implement: `execute`, `help`, `complete` functions
3. Register in `packages/darklang/cli/core.dark` → `Registry.allCommands`
4. Wait for package reload (~10s), check `packages-canvas.log` for errors

### Adding a Builtin (F#)

1. Edit/create file in `backend/src/BuiltinPM/Libs/`
2. Register in `backend/src/BuiltinPM/BuiltinPM.fs`
3. Wait for build (~1min), check `build-server.log`
4. Build triggers package reload, then test in CLI

### Implementation order suggestion

1. SQL migration + `Branches.fs` CRUD (can test via `sqlite3`)
2. Update `Inserts.fs`, `Queries.fs`, `PackageOpPlayback.fs` with `branchId`
3. Update `PackageManager.fs` for branch-aware name resolution
4. Builtins for branch operations
5. Darklang SCM packages (branch, rebase, merge)
6. CLI commands (branch, rebase, merge)
7. Update existing CLI commands (status, commit, log, discard)
8. Update help + docs

Work layer by layer, bottom up. Test each layer before moving up.

---

## Phase 2: IDE Integration

After CLI is working end-to-end.
- VS Code: branch in status bar, tree view, switch/create/merge commands
- LSP: handlers for branch operations

## Phase 3: Selective WIP Op Deletion

Separate PR. View WIP ops, multiselect, delete with dependency safety checks.
- `wip` / `wip delete` / `wip deps` CLI commands
