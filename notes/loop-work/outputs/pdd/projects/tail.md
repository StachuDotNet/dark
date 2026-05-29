# tail

**Goal:** Print the last N lines of input, like `tail`.

**Kind:** greenfield

## Acceptance criteria
- [ ] Output matches coreutils `tail` for given line counts.
- [ ] Operates over a scratch directory of known contents with correct results.
- [ ] A 10 MB+ file does not exhaust memory.
- [ ] Symlinks are not followed into infinite loops.
- [ ] UTF-8 in filenames and file bodies round-trips correctly.
- [ ] Exit codes follow convention (0 success, non-zero on error).
- [ ] `--help` / `-h` prints usage and exits 0.
