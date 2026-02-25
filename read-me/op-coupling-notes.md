# Op Coupling and Dependency Safety

Notes on WIP op management in a hash-based world. Relevant when/if
selective op removal (beyond all-or-nothing `discard`) is added.

## The coupling

Ops are inherently coupled by dependency — you can't reference a type
that doesn't exist. With content-addressed hashing, items in a mutual
recursion SCC have stronger (bidirectional) coupling: their hashes
were computed together as a group.

## Current state

`discard` is all-or-nothing: discards ALL WIP ops on a branch
(`DELETE FROM package_ops WHERE branch_id = ? AND commit_id IS NULL`).
No selective op removal exists. All-or-nothing discard is safe.

## If selective op removal is added later

LibPM must prevent removing an op if other ops depend on it (either
directly or via SCC membership). Specifically:

- Before removing an `AddFn` op: check if any other items reference
  this item's hash (via `package_dependencies`)
- Before removing an `AddFn` op that's in an SCC: warn that all
  SCC members' hashes become invalid (they were computed together)
- Before removing a `SetLocation` op: check if anything resolves
  the name being unset

The CLI should expose dependency analysis for WIP ops:
- "What depends on this op / this item?"
- "What would break if I remove this?"
- A report of safe-to-remove vs coupled ops

## Sync safety

Ops arrive as commits, and a commit must be applied in full. SCC
members within a commit are all present. No partial application.
This is safe by construction.
