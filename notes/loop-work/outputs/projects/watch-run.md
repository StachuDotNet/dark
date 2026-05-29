# watch-run

**Goal:** Watch paths and rerun a command on change, like `entr`.

**Kind:** greenfield

## Acceptance criteria
- [ ] Reruns the given command when a watched path changes.
- [ ] Exits cleanly on interrupt without leaving stray child processes.
- [ ] Operates over a scratch directory of known contents with correct results.
- [ ] A 10 MB+ file does not exhaust memory.
- [ ] Symlinks are not followed into infinite loops.
- [ ] UTF-8 in filenames and file bodies round-trips correctly.
- [ ] Exit codes follow convention (0 success, non-zero on error).
- [ ] `--help` / `-h` prints usage and exits 0.
