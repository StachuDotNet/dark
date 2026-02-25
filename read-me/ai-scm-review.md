# SCM Design Review

## Current Model

Branches form a strict tree rooted at `main`. You can only merge into your parent. Rebase catches you up with parent changes. Merge requires: rebased, no WIP, no children.

### Branch type
```
{ id, name, parentBranchId, baseCommitId, createdAt, mergedAt }
```

### Invariants
- `branch create` always forks from current branch (sets parentBranchId)
- `rebase` updates baseCommitId to parent's latest commit
- `merge` moves ops to parent, sets mergedAt, branch disappears from `list()`
- `delete` only works on branches with no commits/locations (abandon empty branches)
- Merge preconditions: rebased, no WIP, no active children, not main

## What works well

- **Parent-only merge** avoids git's DAG complexity. For an ops-based package system, this is a clean simplification.
- **Rebase-before-merge** means merge is always a fast-forward — conflicts are resolved at rebase time, not merge time.
- **"No children" merge precondition** enforces bottom-up merging, keeping the tree clean.
- **Merged vs deleted** is a clear distinction: merge = success, delete = abandon.

## Concerns

### 1. No way to reparent a branch

If I fork `feature-a` from `main`, then fork `experiment` from `feature-a`, and later decide `feature-a` was a mistake — I'm stuck:
- Can't merge `experiment` into `main` (not its parent)
- Can't delete `feature-a` (has children)
- Can't reparent `experiment` onto `main`
- Only path: merge experiment → feature-a → main, dragging along feature-a's unwanted changes

A `branch reparent <branch> <new-parent>` operation would fix this. It would set a new parentBranchId, reset baseCommitId, and require a rebase onto the new parent.

### 2. No cross-branch collaboration

Two people working on separate branches off main can't share work without going through main. Person A has to merge to main first, then Person B rebases. Fine for small teams, could be friction at scale.

### 3. Merged branches are invisible

After merge, a branch disappears from `list()`. There's no `branch list --merged` or history view. Users might wonder "where did my branch go?" — especially if they want to reference what was on it.

### 4. No squash merge

All ops from the branch land individually on the parent. Ten messy commits all show up in the parent's log. A `merge --squash "single message"` option could keep history clean.

### 5. Rebase conflict resolution is underspecified

`getConflicts` returns location paths like `Owner.Module.TypeName`, but:
- No diff view showing "theirs" vs "mine"
- No accept-theirs/accept-mine tooling
- Help just says "fix these on your branch, then run rebase again"
- No way to see what the parent changed at that location

### 6. Delete is very restrictive

`delete` only works if the branch has no commits and no package locations. This means you can't abandon a branch with work on it — you have to `discard` all WIP first, and even then committed work blocks deletion. The only way to get rid of a branch with committed work is to merge it (which you might not want to do). Maybe `delete --force` could allow abandoning branches with commits.

## Suggested priorities

1. **Reparent** — biggest missing piece, makes branch hierarchy decisions reversible
2. **Delete --force** — let users abandon branches with committed work
3. **Squash merge** — nice-to-have for clean history
4. **Conflict resolution tooling** — show diffs during rebase conflicts
5. **Merged branch history** — `branch list --merged` or `log --all-branches`
