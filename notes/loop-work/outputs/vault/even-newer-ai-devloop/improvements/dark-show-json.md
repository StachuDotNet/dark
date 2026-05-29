---
title: Structured dark show <hash> --json
section: 3.6 Review
priority: P1
harness_signal: indirect (enables review tools + CI hooks)
---

# Structured `dark show <hash> --json`

**Problem**: `dark show` emits formatted human output today; iter 18 audit confirmed `--json` missing. A reviewer's tooling (gh-style web UI, IDE plugin) can't consume the diff.

**Proposed fix**: emit `{commit_hash, author, ts, msg, ops: [{kind: "fn-add"|"fn-edit"|"fn-deprecate"|...|"rename", path, before?, after?, ...}]}`. Schema versioned (`_v: 1`).

**Harness signal**: not direct; enables [`dark-review.md`](dark-review.md) summary above to compose cleanly + enables CI hooks.

---

**Cross-reference**: this is the §3.6-specific instance of the [§3.1 #6 `--json` rollout](json-rollout.md) cross-cutting recommendation. When that audit lands, this is one row in the 9-command checklist.
