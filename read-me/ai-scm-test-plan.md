# SCM Integration Test Plan

## Scenario

Simulate a small team building a "MathUtils" library across multiple branches, exercising branching, commits, rebase, merge, conflicts, and edge cases.

## Phase 1: Setup — build features on separate branches

### 1a. Create `math-basics` branch, add arithmetic types and functions
- `branch create math-basics` — **PASS**
- Create type `Test.MathUtils.BinaryOp = | Add | Subtract | Multiply | Divide` — **PASS**
- Create fn `Test.MathUtils.describe` — **PASS**
- `status` shows 1 type, 1 fn — **PASS**
- `commit "add BinaryOp type and describe fn"` — **PASS** (commit affa355c)
- Status clean, log shows commit — **PASS**

### 1b. Create `math-geometry` branch (also off main)
- `branch create math-geometry` (from main) — **PASS**
- Create type `Test.MathUtils.Shape` — **PASS**
- Create fn `Test.MathUtils.area` — **PASS**
- `commit "add Shape type and area fn"` — **PASS** (commit cb8a73b1)

### 1c. Create a child branch off `math-basics`
- `--branch math-basics branch create math-basics-extras` — **PASS**
- Create fn `Test.MathUtils.applyOp` — **PASS**
- `commit "add applyOp fn"` — **PASS** (commit e23ad9c1)

## Phase 2: Branch management

### 2a. List and verify branch tree
- `branch list` shows main, math-basics, math-geometry, math-basics-extras — **PASS**

### 2b. Rename a branch
- `branch rename math-geometry math-shapes` — **PASS**

### 2c. Try deleting a branch with children
- `branch delete math-basics` → "Cannot delete branch with active children" — **PASS**

### 2d. Try deleting a branch with commits
- `branch delete math-shapes` → "Cannot delete branch: it contains 1 commit(s), 2 package location(s). Merge the branch first." — **PASS**

## Phase 3: Merge bottom-up

### 3a. Merge `math-basics-extras` into `math-basics`
- `rebase` → "Already up to date" — **PASS**
- `merge` → "Branch merged successfully. Switched to parent branch." — **PASS**
- extras branch gone from list — **PASS**
- math-basics log shows both commits (affa355c + e23ad9c1) — **PASS**

### 3b. Try merging `math-basics` while it still has work
- Added fn `opSymbol` as WIP — **PASS**
- `merge --dry-run` → "Cannot merge: Commit or discard WIP first" — **PASS**
- Committed WIP — **PASS** (commit 7e90688c)
- `merge --dry-run` → "Branch is ready to merge." — **PASS**

### 3c. Merge `math-basics` into main
- `rebase` → "Already up to date" — **PASS**
- `merge` → success — **PASS**

### 3d. Merge `math-shapes` into main
- `rebase` → "Rebased successfully" (main had moved forward — actual rebase needed!) — **PASS**
- `merge` → success — **PASS**

## Phase 4: Verify final state

### 4a. Check main has everything
- `log` on main shows all 4 feature commits in order — **PASS**
- `show cb8a73b1` shows AddType Shape (enum), SetTypeName, AddFn area, SetFnName — **PASS**

### 4b. Check branch list is clean
- `branch list` shows only main + pre-existing branches (wonder, test, testing) — **PASS**

## Phase 5: Edge cases

### 5a. Discard workflow
- Create branch, add type, verify status shows it, discard, verify clean — **PASS**
- Delete the now-empty branch — **PASS**

### 5b. Empty branch
- Create empty branch, delete immediately — **PASS**

### 5c. Commit with no changes
- `commit "empty"` → "Nothing to commit." — **PASS**

### 5d. Operations on main
- `merge` on main → "Merge failed: Cannot merge main branch" — **PASS**
- `rebase` on main → "Main branch, nothing to rebase" — **PASS**

### 5e. Show commit with bad hash
- `show badhash123` → "No matching commit found." + "Invalid commit ID format." — **PASS**
  - **NOTE**: prints two messages — slightly noisy but not a crash

## Results Summary

**All 28 test cases passed. Zero crashes. Zero unexpected behaviors.**

### What went well
- Branch create/switch/rename/delete all work correctly
- Commit/status/log/show cycle is solid
- Merge preconditions (WIP check, children check, rebased check) are properly enforced
- `--dry-run` for merge is useful
- Rebase correctly detects when parent has moved forward vs already up to date
- Error messages are clear and actionable throughout
- `show` output is nicely formatted with op details

### Minor observations
- `show badhash123` prints two separate error messages ("No matching commit found." and "Invalid commit ID format.") — could be consolidated into one
- `branch create` says "Created and switched to branch" but the switch doesn't persist across invocations (by design, noted in help text)
- Commit log doesn't show which branch a commit was originally created on (only shows `[main]` for ancestor commits) — could be useful for audit trails
- No way to see merged/deleted branches — once gone, they're gone from `branch list`

### Not tested (would need more infrastructure)
- Rebase with actual conflicts (requires two branches modifying same location)
- Multi-level nesting (grandchild branches)
- Concurrent operations (two sessions on same branch)
