---
title: "Did you mean" on miss
section: 3.1 Discovery
priority: P1
harness_signal: rework ratio + first-parse-success
---

# "Did you mean" on miss

**Problem**: verified iter 13 — `dark search xqzbf` outputs `No results found for: xqzbf` and stops. `Stdlib.NoSuchModule.foo` errors with `not found`, no fuzzy-match candidate. Two of the loudest "agent gives up and re-implements" triggers.

**Proposed fix**: (a) on empty `search` results, suffix the response with "Did you mean: `<top 3 fuzzy matches by trigram or Levenshtein>`?" (b) the same for "X not found" runtime errors. Heuristic must be cheap (trigram index over the package tree, refreshed on commit).

**Harness signal**: rework ratio (§6 supporting metric) drops; first-parse-success attempts (§6 diagnostic) drops on Dark.
