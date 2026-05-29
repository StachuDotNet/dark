# recipe-picker

**Goal:** Suggest recipes that match an ingredient inventory.

**Kind:** greenfield

## Acceptance criteria
- [ ] Suggests recipes whose ingredients are satisfied by the current inventory.
- [ ] Canned answers fed via an expect-style harness drive the flow to the expected result.
- [ ] SIGINT (Ctrl+C) does not corrupt on-disk state.
- [ ] Running twice is idempotent — the same inputs yield the same tree.
- [ ] `--help` / `-h` prints usage and exits 0.
