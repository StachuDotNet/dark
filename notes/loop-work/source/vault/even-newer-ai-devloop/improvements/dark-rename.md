---
title: dark rename <old> <new> with auto-update of callers
section: 3.4 Iteration
priority: P1
harness_signal: median tokens per pass (M/L tier) + rework ratio (§6 #9)
---

# `dark rename <old> <new>` with auto-update of callers

**Problem**: verified iter 21 — `./scripts/run-cli help` has `branch rename` but no top-level `rename` for package items. Today an agent that picks the wrong name has to (a) `view` the bad name, (b) `fn --update` it, (c) grep for callers, (d) edit each. Token-expensive and error-prone. Validated by user-known friction in vault TODO.

**Proposed fix**: `dark rename Old.Name New.Name` updates the package-tree item *and* every reference. Errors if the new name conflicts. Behind `--dry-run` for previewing the call-site list. Closest existing analog: `traces export`/`import` machinery already moves package items between trees; `rename` is the within-tree variant.

**Harness signal**: median tokens per pass on M/L tier drops on rename-heavy runs; rework ratio (§6 #9) drops because the "I picked the wrong name and now I'm stuck" failure mode goes away.
