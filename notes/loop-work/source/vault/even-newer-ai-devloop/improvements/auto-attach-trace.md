---
title: Auto-attach the latest trace to every failing run
section: 3.3 Verification
priority: P1
harness_signal: fix-iteration delta (§6 #3, Dark's expected biggest win)
---

# Auto-attach the latest trace to every failing `run`

**Problem**: When `dark run X args` exits non-zero (or returns a `Result.Error`), the agent has to discover this is an error and then call `traces tail` separately to see what happened. That's a discovery hop the agent doesn't always take, especially for the silently-wrong-`Result.Error` case.

**Proposed fix**: when `dark run X args` exits non-zero (or returns a `Result.Error`), the wrapper / agent prompt should follow up with `traces tail` automatically and feed that into the agent's next turn. Surfaces *for the agent* something Dark already has *for the human*.

**Harness signal**: fix-iteration delta (§6 #3) — Dark's expected biggest win — should rise materially after this lands.
