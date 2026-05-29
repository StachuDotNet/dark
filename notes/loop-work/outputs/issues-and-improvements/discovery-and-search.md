# Discovery and search

Discovery is the first friction every agent loop hits. If the agent can't find what already lives in the package tree, it re-implements badly or burns a turn searching. These items are about turning the package tree into a fast, ranked, agent-consumable **projection** rather than a wall of human-formatted output.

(Related friction lives elsewhere: non-TTY orientation and `--json` output are in cli-ergonomics.md; the "did you mean" miss-handling is shared with diagnostics-and-errors.md; the composed `docs for-ai` doc is in agent-workflow.md.)

## Auto-loaded `CLAUDE.md` template

**Issue**: agents start cold. Every session begins with three-to-five orientation calls — `dark tree`, `dark status`, `docs for-ai` — before any task work.

**Candidate fix**: Dark ships a `CLAUDE.md` (with an `AGENTS.md` alias) template at any Dark project root, picked up automatically. It carries a one-line workflow reminder (`fn → run → commit`), a freshly-rendered `dark tree` snapshot, the current branch from `dark status`, and a pointer to `docs for-ai` for deep dives. A `dark commit` hook rewrites the snapshot section. The win: the agent's first tool call shifts from "orient" to "task," and tokens-to-first-fn drops on the trivial tier.

## `dark search` ranking + structured output

**Issue**: `dark search json` returns alphabetical groupings with emojis and ANSI colors — token-noisy and unranked — and has no `--json` (sibling listy commands do). Agents scroll past a wall of `Darklang.JsonRPC.*` matches before reaching `Stdlib.Json`.

**Candidate fix**: add `dark search --json` matching the structured shape of the other listy commands, and rank results by a composite score — prefix-match bonus, exact-name bonus, log of usage count from package-tree refs, minus path depth. Stdlib lifts above niche modules with no manual curation, and tokens-to-first-relevant-fn drops.

## "Did you mean" on a search miss

**Issue**: `dark search xqzbf` prints `No results found` and stops. A `Stdlib.NoSuchModule.foo` reference errors with `not found` and no fuzzy candidate. Both are loud "agent gives up and re-implements" triggers.

**Candidate fix**: on an empty `search` result, suffix the response with the top three fuzzy matches (trigram or Levenshtein over the package tree, refreshed on commit). The same applies to "X not found" runtime errors — see diagnostics-and-errors.md, where the runtime-error side of this is grouped.

## Search-by-type and other agent query helpers

**Issue**: name-and-arity search is enough for a human skimming, but an agent
materializing a body needs to interrogate the package tree (and the trace/value
store) along axes the current surface doesn't expose. "What returns a
`List<List<String>>`?" or "what already touched this fn?" are dead ends today, so
the agent re-implements instead of reusing.

**Candidate fix**: extend the search/query surface — over the same fast,
ranked **projection** of the package tree — with agent-shaped predicates:

- **Search values by type** — find package items (and stored values) whose type
  matches a given type.
- **Partial-signature match** — find items matching a partial signature, not just
  a name (e.g. `_ -> Result<Json, _>`).
- **Traces touching a fn** — given a fn, find the traces that exercised it. (This
  is a predicate query over the stored eval-event stream; see the traces-as-values
  framing in `design/event-bus.md`.)
- **Callees / callers** — find functions called *by* X, and functions that call
  X, straight off the dependency-graph projection.
- **Predicate search** — find functions satisfying an arbitrary predicate over
  their signature/metadata.

These are the queries the coordinator's corpus-search and sig-consensus strategies
lean on (see `design/algorithm.md`), so they share the same latency bar as
discovery: whatever the agent needs at materialization time, it needs fast — the
sub-100ms dark-matter-search target in `results/benchmark-targets.md`.

## Dropped: `dark suggest`

The natural-language `dark suggest "<intent>"` discovery affordance was dropped — the user dislikes it. Its goal (mapping intent like "parse JSON" to a module) is partly served by better search ranking above and by the composed `docs for-ai` doc in agent-workflow.md.
