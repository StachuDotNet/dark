# changelog-gen

**Goal:** Generate a changelog from conventional commits between two tags.

**Kind:** greenfield

## Acceptance criteria
- [ ] Groups commits between tags by conventional-commit type into a changelog.
- [ ] Produces correct output against a golden-tree fixture.
- [ ] Is idempotent where applicable.
- [ ] Bad UTF-8 bytes do not cause a panic.
- [ ] Completes on a 1000-file fixture within a soft time budget.
- [ ] `--help` prints usage and exits 0.
