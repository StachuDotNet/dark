# code-stats

**Goal:** Report per-extension and per-author lines changed over time across a repo.

**Kind:** greenfield

## Acceptance criteria
- [ ] Aggregates change counts by extension and author over the repo history.
- [ ] Produces correct output against a golden-tree fixture.
- [ ] Is idempotent where applicable.
- [ ] Bad UTF-8 bytes do not cause a panic.
- [ ] Completes on a 1000-file fixture within a soft time budget.
- [ ] `--help` prints usage and exits 0.
