# finder

**Goal:** Find files matching globs and optionally run a command per match, like `find -exec`.

**Kind:** greenfield

## Acceptance criteria
- [ ] Matches files by glob/predicate and supports `-exec`-style command invocation.
- [ ] Operates over a scratch directory of known contents with correct results.
- [ ] A 10 MB+ file does not exhaust memory.
- [ ] Symlinks are not followed into infinite loops.
- [ ] UTF-8 in filenames and file bodies round-trips correctly.
- [ ] Exit codes follow convention (0 success, non-zero on error).
- [ ] `--help` / `-h` prints usage and exits 0.
