---
title: Auto-loaded CLAUDE.md template
section: 3.1 Discovery
priority: P1
harness_signal: tokens-to-first-fn (T-tier headline)
---

# `darklang/CLAUDE.md` auto-loaded template

**Problem**: agents start cold. Every session begins with the agent calling `dark tree`, `dark status`, `docs for-ai`, etc. — 3–5 orientation calls before any task work. (`Agent Next Steps.md` items #3, #11.)

**Proposed fix**: Dark ships a `CLAUDE.md` (and `AGENTS.md` symlink) template at any Dark project root, picked up automatically by Claude Code. Includes (a) a one-line workflow reminder (`fn → run → commit`), (b) a freshly-rendered `dark tree` snapshot, (c) the current branch from `dark status`, (d) a pointer to `docs for-ai` for deep dives. Refresh hook: `dark commit` rewrites the snapshot section.

**Harness signal**: median *first-tool-call type* shifts from "orient" (`tree`/`status`/`docs`) to "task" (`view`/`fn`/`run`). Tokens-to-first-fn drops materially on the trivial tier.
