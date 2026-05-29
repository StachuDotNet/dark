---
title: dark review — augment existing TUI with structured/headless output
section: 3.6 Review
priority: P1
harness_signal: median reviewer time-to-decision (Phase 4+ metric)
---

# `dark review` — augment the existing TUI with structured/headless output

**Discovery (final-validation pass)**: `dark review` *already exists* — verified at `packages/darklang/cli/core.dark:227` ("Review branch changesets"). It's an **interactive TUI** with `--all` flag (show all branches), navigation keys (Up/Down/Right/Enter for detail/diff/commits, Left/Esc for back). The original §3.6 #1 framing called it "the headline missing tool" — that was wrong; the tool exists. The augmentation below is what's actually needed.

**Problem (refined)**: the TUI works for an interactive human reviewer but is unusable from a script, CI, or agent prompt. There's no `--json` output, no `--since <ref>` filter to bound a review window to "since I last looked," no way to feed the review surface back into a different tool (e.g. an agent that wants to summarise what another agent did).

**Proposed fix**: extend `dark review` with three new flags:

- `--json` produces the same surface as the TUI but as a structured tree (commits / changed items / deprecation list / fan-in counts) — rides on the [`json-rollout.md`](json-rollout.md) pattern.
- `--since <ref>` bounds the review window (defaults to parent branch; respects the [`review-mark.md`](review-mark.md) if it ships).
- `--include-traces` attaches the most recent traces against changed fns (so the reviewer sees real exec, not dead code).

Keep the TUI mode as the default; flags are additive. **Lower cost than originally claimed** because the underlying review machinery exists.

**Harness signal**: this is human-time, not agent-time, so off-§6. Track separately as "median reviewer time-to-decision" once we instrument it. Phase 4+ metric.
