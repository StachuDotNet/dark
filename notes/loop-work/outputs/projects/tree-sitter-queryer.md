# tree-sitter-queryer

**Goal:** Query source files with s-expression tree queries.

**Kind:** greenfield

## Acceptance criteria
- [ ] Runs an s-expr query against parsed source and prints the matched nodes.
- [ ] Produces correct output against a golden-tree fixture.
- [ ] Is idempotent where applicable.
- [ ] Bad UTF-8 bytes do not cause a panic.
- [ ] Completes on a 1000-file fixture within a soft time budget.
- [ ] `--help` prints usage and exits 0.
