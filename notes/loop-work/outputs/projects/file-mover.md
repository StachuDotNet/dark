# file-mover

**Goal:** Move and normalize files into a directory hierarchy derived from regex captures.

**Kind:** greenfield

## Acceptance criteria
- [ ] Builds destination paths from regex capture groups and moves files accordingly.
- [ ] Name collisions are handled rather than silently overwriting.
- [ ] Operates over a scratch directory of known contents with correct results.
- [ ] A 10 MB+ file does not exhaust memory.
- [ ] Symlinks are not followed into infinite loops.
- [ ] UTF-8 in filenames and file bodies round-trips correctly.
- [ ] Exit codes follow convention (0 success, non-zero on error).
- [ ] `--help` / `-h` prints usage and exits 0.
