---
title: Reproducible builds
section: 3.5 Sharing
priority: P2
harness_signal: byte-equal artifact CI gate
---

# Reproducible builds

**Problem**: agent-built projects need to be re-buildable from package-tree state alone. If `dark publish` depends on filesystem state outside the package tree, sharing breaks — the friend gets a slightly different artifact than the author tested.

**Proposed fix**: `dark publish` reads exclusively from the named project's transitive closure in the package tree. CI: re-run `publish` on a fresh clone, byte-compare the artifact.

**Harness signal**: CI-side byte-equality gate. Failures point at hidden filesystem dependencies in `publish`.

---

**Cross-reference**: this is a constraint on [`dark-publish.md`](dark-publish.md), not a separate command. The two should ship together — `dark publish` lands first; the byte-equal CI gate hardens it second.
