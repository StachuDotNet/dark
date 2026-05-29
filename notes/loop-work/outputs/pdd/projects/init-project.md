# init-project

**Goal:** Prompt for project details and scaffold a new repository.

**Kind:** greenfield

## Acceptance criteria
- [ ] Prompts for name/license/language and writes a corresponding project skeleton.
- [ ] Canned answers fed via an expect-style harness drive the flow to the expected result.
- [ ] SIGINT (Ctrl+C) does not corrupt on-disk state.
- [ ] Running twice is idempotent — the same inputs yield the same tree.
- [ ] `--help` / `-h` prints usage and exits 0.
