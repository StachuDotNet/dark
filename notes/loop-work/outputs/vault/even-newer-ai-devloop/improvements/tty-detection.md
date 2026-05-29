---
title: Bare `dark` defaults to agent-friendly orientation (TTY/non-TTY auto-detection)
section: 3.1 Discovery
priority: P1
harness_signal: median tokens for first turn
---

# Bare `dark` invocation defaults to agent-friendly orientation

**Problem**: verified iter 13 — `dark` (no args) prints an ASCII-art banner + command groupings with ANSI codes. Lovely for humans; noisy when the agent is iterating. The tokens for that banner are paid every time.

**Proposed fix**: detect non-TTY callers (no terminal capabilities) and emit a terse plaintext orientation: 1 line per command group, no banner, no ANSI. Behind the scenes the same logic that already powers `--no-color` envvars in many CLIs. No agent prompt change needed; the savings are automatic.

**Harness signal**: median tokens for the agent's first turn drops on every project (small per-run gain × N runs = real money over a sweep).

---

**Round-2 cross-reference**: this proposal is the basis of the user's first concrete improvement request in feedback-plan.md round-2 P2 ("TTY/non-TTY auto-detection"). The question of whether default output should be plaintext or `--json` for non-TTY callers is *unresolved*. JSON is parser-friendly for some commands (search, deps) but bracket-overhead-heavy for others (tree, list); dense plaintext might be the better non-TTY default in those cases. The audit that informs this decision is the §3.1 #6 [JSON rollout](json-rollout.md) item.
