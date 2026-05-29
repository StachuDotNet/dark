# tree-lite

**Goal:** Print a directory tree with a size column, filters, and depth control.

**Kind:** greenfield

## Acceptance criteria
- [ ] Renders nested directory structure with indentation and per-entry sizes.
- [ ] Depth and filter flags limit the output.
- [ ] Operates over a scratch directory of known contents with correct results.
- [ ] A 10 MB+ file does not exhaust memory.
- [ ] Symlinks are not followed into infinite loops.
- [ ] UTF-8 in filenames and file bodies round-trips correctly.
- [ ] Exit codes follow convention (0 success, non-zero on error).
- [ ] `--help` / `-h` prints usage and exits 0.
