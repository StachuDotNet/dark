# env-doctor

**Goal:** Check for required binaries and environment variables and propose fixes.

**Kind:** greenfield

## Acceptance criteria
- [ ] Reports missing required tools/envs and suggests remediation.
- [ ] Canned answers fed via an expect-style harness drive the flow to the expected result.
- [ ] SIGINT (Ctrl+C) does not corrupt on-disk state.
- [ ] Running twice is idempotent — the same inputs yield the same tree.
- [ ] `--help` / `-h` prints usage and exits 0.
