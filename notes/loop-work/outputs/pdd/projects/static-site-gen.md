# static-site-gen

**Goal:** Convert markdown into a templated static HTML site.

**Kind:** greenfield

## Acceptance criteria
- [ ] Renders markdown pages into HTML using templates and writes the output tree.
- [ ] Produces correct output against a golden-tree fixture.
- [ ] Is idempotent where applicable.
- [ ] Bad UTF-8 bytes do not cause a panic.
- [ ] Completes on a 1000-file fixture within a soft time budget.
- [ ] `--help` prints usage and exits 0.
