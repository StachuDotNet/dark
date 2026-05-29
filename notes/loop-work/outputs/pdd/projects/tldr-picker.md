# tldr-picker

**Goal:** Pick a command and display its tldr page with paging.

**Kind:** greenfield

## Acceptance criteria
- [ ] Selects a command and renders its tldr page.
- [ ] Canned answers fed via an expect-style harness drive the flow to the expected result.
- [ ] SIGINT (Ctrl+C) does not corrupt on-disk state.
- [ ] Running twice is idempotent — the same inputs yield the same tree.
- [ ] `--help` / `-h` prints usage and exits 0.
