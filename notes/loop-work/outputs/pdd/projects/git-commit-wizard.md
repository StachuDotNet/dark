# git-commit-wizard

**Goal:** Guide a conventional-commit message via prompts with a diff preview.

**Kind:** greenfield

## Acceptance criteria
- [ ] Prompts for commit type/scope/description and builds a conventional-commit message.
- [ ] Canned answers fed via an expect-style harness drive the flow to the expected result.
- [ ] SIGINT (Ctrl+C) does not corrupt on-disk state.
- [ ] Running twice is idempotent — the same inputs yield the same tree.
- [ ] `--help` / `-h` prints usage and exits 0.
