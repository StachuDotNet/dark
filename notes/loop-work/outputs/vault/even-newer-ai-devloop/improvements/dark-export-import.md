---
title: dark export / dark import (run-without-publish)
section: 3.5 Sharing
priority: P2
harness_signal: collaboration-friction (smaller-than-binary share path)
---

# `dark export` / `dark import` — run-without-publish

**Problem**: simplest possible share is "the friend has Dark installed and runs the same code." Dark's package-tree-first model already supports this — but there's no canonical "export this project as a single thing" / "import it" pair.

**Proposed fix**: `dark export <project> > project.darkpack` + `dark import < project.darkpack`. Smaller than a published binary; useful for collaboration; close cousin of `traces export`/`import` which already exists. Reuse that machinery.

**Harness signal**: collaboration friction — when the friend story is "they already have Dark," the path should be one-line on each side.

---

**Cross-reference**: complementary to [`dark-publish.md`](dark-publish.md). `publish` is for "friend has nothing"; `export`/`import` is for "friend has Dark." Both should ship; neither replaces the other.
