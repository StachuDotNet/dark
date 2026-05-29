---
title: Promote gen-test <trace-id> in agent docs
section: 3.3 Verification
priority: P1
harness_signal: trace adoption rate (§6 #4) + test-coverage proxy
---

# Promote `gen-test <trace-id>` in agent docs

**Problem**: `traces gen-test <id>` exists today (verified iter 6 via `help traces`); the agent has no reason to know it does. So agents who do reach for traces don't necessarily capture them as regression tests, even though Dark provides the one-line affordance.

**Proposed fix**: a line in `docs for-ai` saying "after any successful `run` of a non-trivial fn, consider `traces gen-test <id>` to capture it as a regression test" turns "did I write a test?" into a one-token answer for the agent.

**Harness signal**: trace adoption rate (§6 #4) rises; per-project test coverage proxy improves on Dark vs other languages where the agent has to author tests by hand.

---

**Note**: zero-Dark-code-change. Pure prompt-template / docs change. Bundles into the wave-1 prompt-only Phase 3 wave (§7).
