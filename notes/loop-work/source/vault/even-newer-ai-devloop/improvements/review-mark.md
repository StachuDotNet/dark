---
title: dark review-mark — explicit "I reviewed up to here" pointer
section: 3.6 Review
priority: P4 (deferred unless cheap)
harness_signal: enables median reviewer time-to-decision metric
---

# `dark review-mark` — explicit "I reviewed up to here" pointer

**Problem**: with the current SCM model, there's no equivalent of GitHub's "this PR was reviewed by X." A reviewer who took 20 min through the existing `dark review` TUI can't record that decision; the next reviewer starts from scratch.

**Proposed fix**: `dark review-mark <ref>` writes a tiny SCM artifact ("reviewer: <git-config-name>, ts: …, ref: <commit-hash>") into the package tree. The existing `dark review` TUI (per [`dark-review.md`](dark-review.md)) defaults to *bounding its window* by the most-recent `review-mark`. Lightweight; no PR concept needed.

**Harness signal**: enables the "median reviewer time-to-decision" metric to actually be measured (we know when review starts/ends).

---

**Cross-reference**: needs a small SCM addition (the `review-mark` artifact). Defer to Phase 4 unless cheap. Composes with [`dark-review.md`](dark-review.md) `--since <ref>` flag.
