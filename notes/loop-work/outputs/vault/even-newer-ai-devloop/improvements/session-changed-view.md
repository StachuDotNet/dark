---
title: Session-scoped "what have I changed?" view
section: 3.4 Iteration
priority: P1
harness_signal: turn count per project (telemetry-derived)
---

# Session-scoped "what have I changed?" view

**Problem**: agents iterating across many `fn` calls lose track of their own working set, especially when the package tree is large. Today they can `dark log` for commits or `dark status` for uncommitted, but there's no "show me everything I've touched in this session, regardless of commit boundary."

**Proposed fix**: `dark since <ref>` (where `<ref>` defaults to the start of the current session, recorded in `rundir/.dark-session-start`) lists every package item created or modified since `<ref>`, with a `view` snippet for each. Cross-pollinates with §3.6 #1 `dark review`, but session-scoped (agent-facing) vs review-scoped (human-facing).

**Harness signal**: drop in turn count per project (telemetry-derived); the agent stops re-discovering its own work.

---

**Cross-reference**: pairs naturally with [`dark review`](dark-review.md) (§3.6 #1) — same underlying machinery; agent-facing vs reviewer-facing is the only delta.
