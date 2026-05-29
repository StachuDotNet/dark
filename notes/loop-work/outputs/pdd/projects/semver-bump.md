# semver-bump

**Goal:** Read a package manifest, compute the next semantic version, and write it back.

**Kind:** greenfield

## Acceptance criteria
- [ ] Bumps major/minor/patch correctly and updates the manifest.
- [ ] Produces correct output against a golden-tree fixture.
- [ ] Is idempotent where applicable.
- [ ] Bad UTF-8 bytes do not cause a panic.
- [ ] Completes on a 1000-file fixture within a soft time budget.
- [ ] `--help` prints usage and exits 0.
