---
title: Surface merge --dry-run and rebase --status in agent prompt
section: 3.4 Iteration
priority: P1 (zero-Dark-code)
harness_signal: agent abandonment in cross-branch ops
---

# Surface `merge --dry-run` and `rebase --status` in the agent prompt template

**Strength to surface, not a gap**: verified iter 21 that both already exist. `merge --dry-run` checks "is this merge clean?" without doing it; `rebase --status` checks for conflicts without rebasing. **Phase 3's A/B wave workflow** ([plan.md](../plan.md) §7) hits these naturally — the bench's `--dark-revision` flag wants to know "will my candidate branch cleanly pull onto main?" before running.

**Proposed fix**: a §4.7-template prompt addition that mentions both commands when the agent is about to `merge` or pre-flight a branch move. **Costs nothing** — pure prompt-template change, no Dark code.

**Harness signal**: agent abandonment in cross-branch operations drops; cleaner Phase 3 protocol execution.

---

**Note**: zero-Dark-code-change. Bundles into the wave-1 prompt-only Phase 3 wave alongside [gen-test-promotion.md](gen-test-promotion.md) and similar surface-existing-strengths items.
