---
title: dark uncommit / dark revert <commit>
section: 3.4 Iteration
priority: P1
harness_signal: edit-to-first-green (§6 #8) + rework ratio (§6 #9) + agent-abandonment
---

# `dark uncommit` / `dark revert <commit>` — undo a *committed* change

**Problem**: verified iter 21 — `dark discard` only handles uncommitted changes (per `docs scm`). Once an agent commits a bad change, there's no equivalent of `git reset HEAD^` or `git revert`. The agent has to manually re-author the prior version.

**Proposed fix**: (a) `dark uncommit` — pop the most-recent commit on the current branch back into uncommitted state, preserving work. (b) `dark revert <commit>` — create a new commit that undoes a prior one. The two together cover "I just committed a typo" and "this old commit was wrong."

**Harness signal**: edit-to-first-green (§6 #8) drops on iteration tasks; rework ratio (§6 #9) drops; agent-abandonment (Phase 1 metric) decreases when agents previously got stuck because they couldn't unwind a commit.
