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

## Dropped: `dark suggest`

The natural-language `dark suggest "<intent>"` discovery affordance was dropped — the user dislikes it. Its goal (mapping intent like "parse JSON" to a module) is partly served by better search ranking above and by the composed `docs for-ai` doc in agent-workflow.md.
