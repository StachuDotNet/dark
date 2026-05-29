# Benchmark targets

Two UX latency targets that are load-bearing enough to benchmark directly. These
are not design notes — they are the numbers a benchmark must hold the system to,
and the two code paths a benchmark must target specifically.

## The two paths

- **Searching for dark matter — sub-100ms.** Finding existing functions, types,
  values, and prompts in the package tree (and the value/trace store). This is the
  query surface the agent leans on constantly during materialization, so it sits
  on the hot path of nearly every loop iteration. The richer agent queries
  (search-by-type, partial-signature, callers/callees, predicate search — see
  `discovery-and-search.md`) all inherit this same bar.
- **Drafting v0 of any code — under 1s.** From a prompt-typed name to a first
  executable sketch. This is the moment the user feels the system respond at all;
  if it's slow, the whole gradual/lazy model feels dead.

## Why these specifically

Both are the load-bearing UX moments — the points where latency is felt directly
rather than amortized. Neither is fast enough today. So the bench must target
**these two paths in particular**, not aggregate end-to-end time: a benchmark that
only measures whole-task wall-clock can pass while both of these stay sluggish.
Measure dark-matter search and v0-draft latency as named, separately-tracked
metrics, against the sub-100ms and sub-1s targets respectively.
