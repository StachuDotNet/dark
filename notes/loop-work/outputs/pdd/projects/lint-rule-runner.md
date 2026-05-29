# lint-rule-runner

**Goal:** Run a set of regex lint rules across a codebase and report violations.

**Kind:** greenfield

## Acceptance criteria
- [ ] Reports each rule violation with file and line, and exits non-zero on any violation.
- [ ] Produces correct output against a golden-tree fixture.
- [ ] Is idempotent where applicable.
- [ ] Bad UTF-8 bytes do not cause a panic.
- [ ] Completes on a 1000-file fixture within a soft time budget.
- [ ] `--help` prints usage and exits 0.
