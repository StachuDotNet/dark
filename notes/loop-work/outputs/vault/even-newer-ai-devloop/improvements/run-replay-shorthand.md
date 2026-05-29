---
title: run --replay <trace-id> shorthand
section: 3.3 Verification
priority: P1
harness_signal: fix-iteration delta (§6 #3)
---

# `run --replay <trace-id>` shorthand

**Problem**: today the agent has to call `traces replay <id> --diff` (long, two-step lookup) to re-run the same inputs against current code and see whether output diverged. Two commands and a manual ID-passing step.

**Proposed fix**: every failing `run` returns its trace ID prominently, and `dark run --replay <prefix>` is sugar for "re-run the same inputs against current code, diff against recorded output." Compresses the bug-fix loop to two commands (`run --replay <id>` after each fix attempt).

**Harness signal**: fix-iteration delta (§6 #3) — twin-signal with [auto-attach-trace.md](auto-attach-trace.md). Items 1+2 together are the §3.3 headline movement.
