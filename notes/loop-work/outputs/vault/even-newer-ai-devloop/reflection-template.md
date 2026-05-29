# Reflection template — what each agent's `SUMMARY.md` includes

Per round-2 P0 #11 (and §3.6 #3): every agent run, in every language, produces a `SUMMARY.md` at the end of its work. This file is what the wrapper extracts to build the **CLI-UX-friction digest** that feeds back into `improvements/` for the next round.

The template below is what the agent fills in as its last turn (just before emitting `<phase>DONE</phase>`). The bench's prompt template (§4.7 `task.md`) instructs the agent to follow this structure.

---

## The template (agent fills this in as its last turn)

```markdown
---
project: <kebab-case>
language: <dark|ts|py|go|rust>
sweep_id: <auto-YYYY-MM-DD-HHMM>
run_id: <bench-<sweep_id>-<project>-<lang>-attempt<N>>
agent_self_confidence: <high|medium|low>
phases: { plan_min: <Float>, impl_min: <Float>, verify_min: <Float> }
---

# Goal as I understood it

<1–3 sentences. The agent's paraphrase of the spec's Description section. No copy-paste from the spec.>

# Approach

<3–5 bullets. The agent's own narrative of *how* they implemented this. Not what code they wrote — what shape of approach.>

- bullet 1
- bullet 2
- ...

# Self-verification results

<Per the spec's Self-verification section, what the agent checked + what it found.>

- Smoke commands: <ran / skipped / hit issue>
- Behaviours coverage: <X of N — list any gaps the agent acknowledges>
- Library-port API spot-check (if applicable): <matched / partial / mismatched>
- Mutation-style invariant test (if applicable): <ran / skipped>

# Deprecated / removed / left out

<Things the agent decided NOT to do, and why. Empty if N/A.>

- e.g. "I considered adding a `--config` flag but the spec doesn't mention it; out of scope."
- e.g. "Tried hash-deterministic slugs but URL canonicalization was complex; pivoted to random+collision-retry."

# Gave up on

<Things the agent attempted and abandoned. Empty if N/A.>

- e.g. "First tried Stdlib.Json.parse for the input but Dark's parser doesn't handle trailing commas; switched to a manual parser."

# Dark CLI UX feedback (Dark runs only — round-2 P0 #11)

<This is the headline section. The agent reflects on what *would have made this run easier*. Aggregated across many runs, this is the tightest signal we get on §3 improvement priority.>

- **Friction**: a thing that cost time/tokens unnecessarily. e.g. "Spent 2 turns figuring out that `dark fn` requires two args, not one. The error message didn't suggest the right form."
- **What would have helped**: e.g. "Had the parse-error excerpt suggested `did you mean two args?` I would have unblocked instantly."
- **Discovery question**: a thing the agent didn't know to look for. e.g. "I didn't know `traces gen-test` existed until I happened to read `help traces`."
- **Stdlib gap**: a thing the agent expected to find and didn't. e.g. "Wanted Stdlib.Csv but it didn't exist; hand-rolled."

If nothing of substance to say, write "Nothing to flag." Empty section is wrong; "Nothing to flag" with one sentence on why is right.

# Notes for the reviewer

<Anything the agent thinks the human reading the dashboard tomorrow morning should know. Especially: surprises, deviations from the obvious approach, tradeoffs the agent picked.>
```

---

## Why each section

- **Frontmatter** — machine-parsed by the bench wrapper's reflection-aggregator. Self-confidence + phase timings feed §6 metrics directly.
- **Goal as I understood it** — catches misreads early. If the agent's paraphrase doesn't match the spec, the rubric is about to fail and the agent is admitting why.
- **Approach** — the agent's narrative, not the code. The reviewer doesn't have to dive into the diff to understand the shape.
- **Self-verification results** — closes the loop on the agent's own work. Tells the reviewer *what the agent already knows* about its own gaps before they look.
- **Deprecated / removed** — "what I left out, and why." Catches over-implementation and surfaces design choices.
- **Gave up on** — explicit failure flags. The agent saying "I couldn't do X" is hugely valuable signal that a stuck project doesn't need diagnosis.
- **Dark CLI UX feedback** — *the per-project Dark improvement signal*. Per-run frictions aggregate across runs into the strongest §3 backlog input we have.
- **Notes for the reviewer** — escape hatch for anything else.

## How the wrapper aggregates

After every sweep, the wrapper runs `python -m harness reflect <sweep_id>` which:
1. Reads every `SUMMARY.md` under `evals/bench/sweeps/<sweep_id>/runs/`.
2. Extracts frontmatter (machine-parseable phase timings + confidence levels).
3. Greps the "Dark CLI UX feedback" section across all Dark runs.
4. Writes `evals/bench/sweeps/<sweep_id>/cli-friction-digest.md` — a deduplicated, frequency-counted summary of the frictions agents hit this sweep.
5. The digest auto-files into `improvements/` candidates the next time the §3 backlog is reviewed.

**Headline metric for the digest**: the most-cited friction across runs is the highest-leverage §3 priority. **Validate and ship it next.**

## Self-confidence calibration

`agent_self_confidence` is a 3-value enum (`high | medium | low`). After enough sweeps:
- If `high` runs pass at >90% rubric rate, agent calibration is good.
- If `high` runs pass at <70%, the agent is overconfident — adjust the §4.7 prompt to encourage more skepticism.
- If `low` runs surprise-pass at >50%, the agent is underconfident — encourage it to self-trust.

Self-confidence is **not used for scoring** — only for prompt calibration over time. Reviewer's actual bench number is the rubric outcome, not the agent's claim.

## Phase timings

`phases.{plan_min, impl_min, verify_min}` capture how long the agent spent in each. Useful for noticing pathologies: an agent that spent 30 min planning a 5-min impl probably has the wrong ratio. Aggregate across runs to see if Dark's planning phase is unusually long (indicates Discovery friction).

## Cross-language template parity

The template above is **language-agnostic on purpose**. TS / Py / Go / Rust runs fill in the same sections (with the "Dark CLI UX feedback" relabeled as "Language CLI UX feedback" — same idea, different target). This way the digest aggregator can compare friction-counts across languages: *"Dark agents flagged 14 frictions tonight; TS agents flagged 3."* Direct measurement of the gap our improvements are trying to close.
