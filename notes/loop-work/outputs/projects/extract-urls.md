# extract-urls

**Goal:** Extract and normalize URLs from a tree of text files.

**Kind:** greenfield

## Acceptance criteria
- [ ] Collects unique, normalized URLs found across the input files.
- [ ] Operates over a scratch directory of known contents with correct results.
- [ ] A 10 MB+ file does not exhaust memory.
- [ ] Symlinks are not followed into infinite loops.
- [ ] UTF-8 in filenames and file bodies round-trips correctly.
- [ ] Exit codes follow convention (0 success, non-zero on error).
- [ ] `--help` / `-h` prints usage and exits 0.
