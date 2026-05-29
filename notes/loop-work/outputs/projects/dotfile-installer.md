# dotfile-installer

**Goal:** Walk a config tree and prompt to symlink each dotfile.

**Kind:** greenfield

## Acceptance criteria
- [ ] Prompts y/n per dotfile and creates the requested symlinks idempotently.
- [ ] Canned answers fed via an expect-style harness drive the flow to the expected result.
- [ ] SIGINT (Ctrl+C) does not corrupt on-disk state.
- [ ] Running twice is idempotent — the same inputs yield the same tree.
- [ ] `--help` / `-h` prints usage and exits 0.
