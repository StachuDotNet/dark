---
title: Audit trail for deprecate / discard moves
section: 3.6 Review
priority: P1
harness_signal: review-caught regressions (Phase 4+ metric)
---

# Audit trail for `deprecate` / `discard` moves

**Problem**: memory `project_deprecation_visibility` says deprecated items are hidden from `ls`/`tree`/`search` unless still referenced. *Great* for the agent's working set; *dangerous* for review — the reviewer might miss that an agent quietly deprecated `Critical.Auth.checkUser` and replaced it with a less-restrictive version. Today no canonical "show me what got hidden" view.

**Proposed fix**: `dark deprecated [--since <ref>] [--all]` lists all deprecation events with kind / reason / replacement. [`dark-review.md`](dark-review.md) surfaces this prominently as a separate section (deprecations get a louder visual treatment than additions). The reviewer must see them, not opt-in.

**Harness signal**: doesn't move §6 metrics directly. Catches a class of *silent regressions* — fold into a Phase 4+ "review-caught regressions" metric.

---

**Cross-reference**: pairs with the existing `deprecate --kind {obsolete, harmful, superseded-by}` discipline (verified iter 34, see Strengths section). Agent should reach for `deprecate --kind superseded-by --replacement <new>` rather than overwriting, and the audit trail above is what makes that discipline reviewable.
