# cookie-jar

**Goal:** Save and fuzzy-select reusable snippets to copy.

**Kind:** greenfield

## Acceptance criteria
- [ ] Saves named snippets and fuzzy-selects one to output/copy.
- [ ] Snippets persist across invocations.
- [ ] Canned answers fed via an expect-style harness drive the flow to the expected result.
- [ ] SIGINT (Ctrl+C) does not corrupt on-disk state.
- [ ] Running twice is idempotent — the same inputs yield the same tree.
- [ ] `--help` / `-h` prints usage and exits 0.
