---
title: hotspots as a built-in code-review pass
section: 3.3 Verification
priority: P1
harness_signal: perf-review-at-no-extra-agent-cost (catches >10ms fns)
---

# `hotspots` as a built-in code-review pass

**Problem**: agents finish a project without considering performance. `traces hotspots` (verified iter 6) lists the slowest fns from recent traces, but agents never call it — there's no prompt cue and no harness affordance.

**Proposed fix**: when the agent says `<phase>DONE</phase>`, the wrapper runs `traces hotspots` over the recent traces and surfaces any fn taking >10 ms. Becomes free perf-review at no extra agent cost (the wrapper does the work, not the agent).

**Harness signal**: catches a class of "ships but slow" results that today pass the rubric silently. Could become a Phase 4+ "perf-quality regressions caught" metric.

---

**Note**: harness-side change, no Dark CLI change. Adds <1 turn to every successful run. Combine with the agent-summary pattern (§3.6 #3) so the SUMMARY.md includes a "fns >10ms" callout that the human reviewer sees first.
