# dep-graph

**Goal:** Walk a project's dependencies and render a DOT graph.

**Kind:** greenfield

## Acceptance criteria
- [ ] Emits a DOT graph of the project's module/dependency relationships.
- [ ] Produces correct output against a golden-tree fixture.
- [ ] Is idempotent where applicable.
- [ ] Bad UTF-8 bytes do not cause a panic.
- [ ] Completes on a 1000-file fixture within a soft time budget.
- [ ] `--help` prints usage and exits 0.
