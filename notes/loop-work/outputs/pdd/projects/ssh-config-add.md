# ssh-config-add

**Goal:** Prompt for host details and append an entry to `~/.ssh/config`.

**Kind:** greenfield

## Acceptance criteria
- [ ] Appends a well-formed host block to the SSH config from prompted values.
- [ ] Canned answers fed via an expect-style harness drive the flow to the expected result.
- [ ] SIGINT (Ctrl+C) does not corrupt on-disk state.
- [ ] Running twice is idempotent — the same inputs yield the same tree.
- [ ] `--help` / `-h` prints usage and exits 0.
