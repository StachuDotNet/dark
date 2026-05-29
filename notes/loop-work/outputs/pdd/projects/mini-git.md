# mini-git

**Goal:** Store blob/tree/commit objects and support `log` and `checkout`.

**Kind:** greenfield

## Acceptance criteria
- [ ] Persists content-addressed objects and reconstructs a working tree on checkout.
- [ ] `log` lists the commit history.
- [ ] Produces correct output against a golden-tree fixture.
- [ ] Is idempotent where applicable.
- [ ] Bad UTF-8 bytes do not cause a panic.
- [ ] Completes on a 1000-file fixture within a soft time budget.
- [ ] `--help` prints usage and exits 0.
