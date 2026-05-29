# linecount-by-ext

**Goal:** Aggregate line counts by file extension across a tree, like a `cloc`-lite.

**Kind:** greenfield

## Acceptance criteria
- [ ] Reports total lines grouped by extension with a summary.
- [ ] Operates over a scratch directory of known contents with correct results.
- [ ] A 10 MB+ file does not exhaust memory.
- [ ] Symlinks are not followed into infinite loops.
- [ ] UTF-8 in filenames and file bodies round-trips correctly.
- [ ] Exit codes follow convention (0 success, non-zero on error).
- [ ] `--help` / `-h` prints usage and exits 0.
