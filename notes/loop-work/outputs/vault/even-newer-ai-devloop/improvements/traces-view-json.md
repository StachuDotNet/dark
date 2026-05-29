---
title: JSON output for traces view
section: 3.3 Verification
priority: P1
harness_signal: parsing reliability (downstream of [json-rollout.md](json-rollout.md))
---

# JSON output for `traces view`

**Problem**: `--json` exists for `list`, `find`, `stats`, `hotspots`, `follow`. Confirmed *missing* from `view` itself per `help traces` (iter 6). Agents end up regex-ing the human-formatted view output to extract the value at a specific eval point.

**Proposed fix**: add `--json` to `traces view`, emitting structured `{trace_id, fn, args: [...], result, eval_steps: [{path, value, ...}]}`. Lets the agent parse trace bodies structurally instead of regex-ing the human view.

**Harness signal**: parsing reliability — fewer trace-extraction errors, fewer "agent misread the trace and re-fixed something that was already correct" failure modes.

---

**Cross-reference**: this is the §3.3-internal instance of the [§3.1 #6 `--json` rollout](json-rollout.md) cross-cutting recommendation. When that audit lands, this becomes one row in its 9-command checklist.
