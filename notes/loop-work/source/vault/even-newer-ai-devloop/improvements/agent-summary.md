---
title: Auto-generated change summary at end of agent run (SUMMARY.md)
section: 3.6 Review
priority: P1 (zero-Dark-code; pure agent-prompt change)
harness_signal: indirect (median reviewer time-to-decision)
---

# Auto-generated change summary at end of agent run

**Problem**: agents finish a session with zero metadata about *why*. Reviewer reads a wall of new code without context. Even good agents don't write good commit messages by default.

**Proposed fix**: as the agent's last turn (just before emitting `<phase>DONE</phase>` per the iter-50 ralph convention) — or via a manual `dark agent-summary` command — produce `<branch>/SUMMARY.md` containing:

- (a) goal as the agent understood it (from the spec/task prompt)
- (b) approach taken in 3–5 bullets
- (c) explicit list of deprecated items + the agent's reasoning for each
- (d) anything the agent gave up on

Agent generates this as part of the run's token budget. Reviewer reads SUMMARY.md first, then dives into [`dark-review.md`](dark-review.md) for the diff.

**Distinction from Multi's existing `summary/` package** (correcting iter-38's overstatement, verified iter 44 against `~/code/dark-multi/summary/summary.go`): Multi's `summary.GetSummary(branchName)` produces an 80-char "what is Claude doing **right now**?" fragment, refreshed every 60 s from tmux tail. *Live in-flight monitoring*. Different from this proposal, which is *post-hoc end-of-run* — the agent's last turn, structured (goal/approach/deprecations/gave-up-on), full paragraphs. Both useful; complementary, not redundant. Multi's `summary/` is the right thing for the dashboard's "currently running" panel (per §6.2 below); this proposal is the right thing for the post-merge reviewer.

**Reusable patterns from Multi's `summary/` for the bench's report generator**:

- (a) Haiku-3.5 (`claude-3-5-haiku-20241022`) for fast/cheap LLM summarization — same model the bench should use for its own report-summary slot.
- (b) ANSI-stripping in `cleanTerminalOutput()` (lines 231–253) — directly useful for transcript processing in `metrics.py`.
- (c) fallback-to-pattern-match when API key is absent (`getFallbackSummary()`, lines 151–196) — degrades gracefully so the report always has *something* in the summary slot, important for shareability.

**Harness signal**: indirect — should reduce the "median reviewer time-to-decision" metric materially.

---

**Cross-reference**: aligns with the iter-86 [reflection-template.md](../reflection-template.md) (per-project post-mortem) which specs the same goal/approach/deprecated/gave-up-on body shape.
