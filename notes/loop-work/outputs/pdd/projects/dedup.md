# dedup

**Goal:** Walk a directory, compute sha256 per file, and report duplicate groups plus reclaimable space.

**Kind:** greenfield

## Acceptance criteria
- [ ] Walks a directory tree, hashing each file with sha256.
- [ ] Reports duplicate groups, one line per group as `sha256: file1, file2, …`.
- [ ] Prints a summary line `N duplicate groups, M total bytes reclaimable`.
- [ ] Symlinks do not follow into infinite loops; a self-referential symlink fails safely.
- [ ] A large file (e.g. 10 MB) does not exhaust memory.
- [ ] Exit code is 0 on success.
- [ ] `dedup --help` prints usage and exits 0.
