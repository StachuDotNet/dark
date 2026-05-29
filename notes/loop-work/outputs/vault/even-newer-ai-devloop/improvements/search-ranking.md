---
title: dark search ranking + structured output
section: 3.1 Discovery
priority: P1
harness_signal: tokens-to-first-relevant-fn
---

# `dark search` ranking + structured output

**Problem**: `dark search json` (verified iter 13) returns alphabetical groupings with emojis and ANSI colors — token-noisy and unranked. `--json` is absent (other listy subcommands have it; `traces list --json`, `traces find --json`, etc.). Agents scroll through a wall of `Darklang.JsonRPC.*` matches before getting to `Stdlib.Json`.

**Proposed fix**: (a) add `dark search --json` with the same surface as `traces find --json`. (b) Rank results by composite score = `prefix-match-bonus + exact-name-bonus + (log of usage count from package-tree refs) − path-depth`. Stdlib lifts above niche modules without manual curation.

**Harness signal**: tokens-to-first-relevant-fn drops; trace adoption rate (§6.0 #4) untouched (this is upstream of trace use).
