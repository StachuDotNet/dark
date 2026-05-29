# grep-lite

**Goal:** Search text across files with glob selection and colored output.

**Kind:** greenfield

## Acceptance criteria
- [ ] Reports matching lines with file and line context across globbed files.
- [ ] Exit code is 0 on matches, 1 on no matches, >1 on error.
- [ ] ANSI color is suppressed when stdout is not a terminal unless forced.
- [ ] Operates over a scratch directory of known contents with correct results.
- [ ] A 10 MB+ file does not exhaust memory.
- [ ] Symlinks are not followed into infinite loops.
- [ ] UTF-8 in filenames and file bodies round-trips correctly.
- [ ] Exit codes follow convention (0 success, non-zero on error).
- [ ] `--help` / `-h` prints usage and exits 0.
