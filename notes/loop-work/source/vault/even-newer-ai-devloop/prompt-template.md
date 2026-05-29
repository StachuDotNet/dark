# Agent task prompt template

> Extracted from plan.md §4.7 (iter 110) — this is a discrete artifact (the literal template files the harness wrapper sends to the agent), so it gets its own home rather than living inline in the spec.
>
> **Reproducibility-critical**: `prompt_template_hash` is part of every `sweep_id` (per [`plan.md` §4.5](plan.md#45-storage--reporting)), so this template's contents are load-bearing — change it and you've started a new measurement series.

What the wrapper actually sends to Claude Code per run. Reproducibility-critical: `prompt_template_hash` is part of every `sweep_id` (§4.5), so this template's contents are load-bearing — change it and you've started a new measurement series.

**Three messages per attempt**: a system-prompt augmentation (CLAUDE.md), an initial task user message, and (only on pass@2) a feedback user message. The agent's `claude --output-format json` writes its full transcript to `evals/runs/<sweep_id>/<run_id>/transcript.json`.

#### Template files (versioned in-repo)

```
evals/harness/prompts/
  system.{dark,ts,py}.md      # language-specific CLAUDE.md content
  task.md                     # initial task message (Jinja-templated over spec.md)
  retry.md                    # pass@2 feedback message (Jinja over rubric output)
```

The wrapper hashes the concatenation of `system.<lang>.md` + `task.md` + `retry.md` to produce `prompt_template_hash` for the sweep_id triple.

#### `system.dark.md` (the agent's CLAUDE.md for Dark runs)

```
You are building software in Darklang only. Use the Darklang CLI, not bash.

## Workflow
1. fn <name>      — create a function
2. run <fn>       — test it
3. commit "<msg>" — save it (idempotent within a session)

## First-look commands
- ./scripts/run-cli docs for-ai
- ./scripts/run-cli tree           (current package tree)
- ./scripts/run-cli stdlib overview

## Rules
- Full path on `fn`: e.g. fn "Darklang.MyProject.main (n: Int64): Unit = …"
- Lists use semicolons: [1L; 2L; 3L]
- String concat is ++, interpolation is $"…{x}…"
- Match patterns drop the type: `| Some x ->`, not `| Option.Some x ->`
- No nested `let` defining a function. Top-level only.
- Pipes need parens around complex LHS: `(complex expr) |> fn`

## When you're done
Emit `<phase>DONE</phase>` on its own line. The harness polls for it.
If you need human input, emit `<phase>AWAITING_ANSWERS</phase>` instead.
If you're stuck, emit `<phase>NEEDS_HELP</phase>`.
```

`system.ts.md` and `system.py.md` are the analogues — point at `node`/`npm` (or `python`/`uv`), no bash, same XML phase-tag convention.

**Why XML tags, not `__HARNESS_DONE__` as the iter-17 spec proposed**: verified iter 50 against `~/code/dark-multi/scripts/claude-loop.sh`'s `check_phase_transition()` (lines 73–114) — Multi's ralph loop *already* parses these exact XML tags from agent output and writes the resolved phase to `<branch>/.claude-task/phase`. Adopting Multi's convention means: (a) the bench inherits ralph's parsing logic for free, (b) the bench wrapper polls the phase file (a single-line text file) rather than regex-matching the live transcript stream, (c) less false-trigger risk than a magic string. **Correction**: iter-17's `__HARNESS_DONE__` is superseded; use `<phase>DONE</phase>`.

The 6 ralph phases the bench cares about, with bench-side mapping:

| Ralph phase tag | Phase-file value | Bench interpretation |
|---|---|---|
| `<phase>DONE</phase>` | `done` | Agent declared complete → run rubric, score |
| `<phase>READY_FOR_REVIEW</phase>` | `ready-for-review` | Same as DONE for bench purposes (rubric runs) |
| `<phase>AWAITING_ANSWERS</phase>` | `awaiting-answers` | Agent stuck, needs human input → `agent_abandoned: true` (no human in nightly bench) |
| `<phase>NEEDS_HELP</phase>` | `awaiting-answers` | Same as above |
| `<phase>READY_TO_EXECUTE</phase>` | `executing` | Mid-loop signal; not terminal; ignore |
| `<phase>CLEANUP</phase>` | `cleanup` | Mid-loop signal; not terminal; ignore |

Plus 3 ralph-side states the bench observes:
| Phase-file value | Source | Bench interpretation |
|---|---|---|
| `auth-error` | ralph detects auth failure in agent output | §6 #26 `harness_flake: true`, subclass `auth-error`. Don't count as agent failure. |
| `max-iterations-reached` | ralph hit `MAX_ITERATIONS=100` cap | §4.9 budget-hit (separate from cost-cap). Marks run as failed-on-budget. |
| `error` | ralph hit 5 consecutive failures | §6 #26 `harness_flake: true`, subclass `agent-process-crash`. |

**Done-detection in the bench wrapper** (replaces iter-17's open polling-cadence Q):
- Poll `<branch>/.claude-task/phase` every 5 s — same cadence ralph uses internally for its own state-file writes; aligns with Multi's existing tempo.
- Terminal states: `done`, `ready-for-review`, `awaiting-answers`, `error`, `auth-error`, `max-iterations-reached`. Wrapper records the terminal phase + transitions to scoring.
- No need to poll the transcript — ralph already does that work.

**Auth wiring** (corrected iter 51 — user feedback overrides iter 50):

Use **Claude Code subscription auth (Pro / Max), not the metered API key.** Reason: per-token API ($3–$15/M input, $15–$75/M output) is materially more expensive than the flat-rate subscription for a nightly bench at this volume; the subscription is already paid for other work, so its marginal cost-to-the-user is zero.

Resolution of the apparent conflict with iter 50: `clear_oauth_tokens()` in `claude-loop.sh:37–43` *clears OAuth inside the Dark devcontainer* — that's the container-side path Multi's `next-prompt.md` chose for *Multi's own dev work*. The bench runs Claude **on the host**, not inside the container (per Multi's architecture comment in CLAUDE.md: *"Claude runs on the host (not inside containers) with `--dangerously-skip-permissions`"*). Host-side OAuth is what we want and what's already configured for the user's daily Claude Code use.

Concretely:
- The bench wrapper does **not** set `ANTHROPIC_API_KEY`. Don't override it; don't pass it through to subprocess invocations.
- Bench wrapper invokes `claude --dangerously-skip-permissions` with cwd pinned to the per-run workspace dir. Inherits the host's OAuth token from `~/.claude/.credentials.json`.
- TS / Py / Dark all use the same host-side auth — no per-language differences.

**Cost-tracking-without-API-billing** — the §6 cost metrics still work, just reframed:
- The bench *measures* tokens (input/output/cache_*) per run. Same as before — `claude --output-format json` reports them either way.
- The bench *converts* tokens-to-equivalent-API-dollars using `pricing.json` (per §6.0 cost-attribution formula). This is the **shareable cost number** — comparable to Aider's leaderboard, useful for "if you ran this on the API instead, you'd spend $X."
- The user's *actual* spend = subscription monthly fee, amortized however they amortize it. **Not the bench's concern.**
- §4.9 cost-cap math is now expressed in *API-equivalent dollars* (e.g. "$0.50 per project"). It's a quality-and-budget proxy, not a real-money cap on tonight's run.
- Subscription quota exhaustion *is* a real concern. Claude Pro / Max have message-volume caps. If the bench burns through the user's monthly quota, daily Claude Code work suffers. **Mitigation**: §6 #23 pricing-config-drift alarm + iter-48 soft-cap-for-first-7-sweeps gives early warning. Add to the §8 risk register: "Bench exhausts user's Claude subscription quota."

**Practical: tonight's launch and onward** — the wrapper's auth-related preconditions are simpler:
- Verify `claude` CLI is on PATH and authenticated (run `claude --print "ping"` once at the start; if it prompts for OAuth, the user does that interactively, then the bench proceeds).
- No `ANTHROPIC_API_KEY` setup. No env-var management.
- No need to copy/mount credentials into containers — the bench doesn't run Claude *in* containers (only Multi's Dark dev work does that).

For Multi's own nightly task work (separate from the bench), the user's `next-prompt.md` decision to use API-key-in-container can stand — that's a Multi-internal choice, decoupled from the bench. The bench *uses* Multi's queue but runs Claude itself host-side.

#### `task.md` (initial task message; Jinja over spec.md)

```
Build the project specified below in {{language}}. The artifact must satisfy
every behaviour bullet in the spec.

You have a budget of ${{budget_dollars}} (north-star metric §6.0).
Tools available: {{language_tools}}. No bash, no network unless noted.

When the rubric runner accepts your artifact, you'll know — but you may also
emit `<phase>DONE</phase>` on its own line to declare done early.

---
{{spec_md_body}}
---

Begin.
```

Substitution variables (filled by `harness.runner`):
- `{{language}}` — `Dark` / `TypeScript` / `Python`
- `{{budget_dollars}}` — the §6 north-star cap (currently `0.50`)
- `{{language_tools}}` — `dark` / `node, npm` / `python, uv`
- `{{spec_md_body}}` — the project's `spec.md` minus its frontmatter

The agent never sees the rubric file (§4.0 enforcement: rubric path is permission-stripped from the cwd).

#### `retry.md` (pass@2 message; Jinja over the failing rubric output)

```
Your first attempt didn't pass. Here's what the rubric reported:

{{rubric_failure_output}}

You have ${{budget_remaining_dollars}} left. Apply a fix and try again.
{% if language == "Dark" %}
Tip: `traces tail` shows the most recent execution (input + per-fn calls).
`traces replay <id> --diff` re-runs against current code and diffs against
the recorded output.
{% endif %}

Begin.
```

The Dark-specific tip is the [improvements.md](improvements.md) §3.3 promotion of trace primitives — surfacing them in the retry prompt is the cheapest way to boost the **fix-iteration delta** (§6 #3) without shipping any Dark code change. **This is itself a candidate Phase 3 improvement wave**: A/B the prompt with vs without the trace tip and see how big the delta moves.

#### What the template deliberately excludes

- **No example solutions.** Showing a "here's how to structure it" hint would compress the cross-language signal we're trying to measure.
- **No mention of specific rubric tests.** Spec describes *behaviours*; rubric encodes *checks*. Agents that game the rubric should fail to satisfy behaviours, and §8 risk #1 (rubric mutation testing) catches the inverse.
- **No "be terse" / "be verbose" instruction.** We measure tokens; we don't optimise for them via the prompt. If Dark wins on tokens, we want it to be because of the *language*, not because we told the agent to write less.
- **No CoT scaffolding** ("first plan, then code"). Claude Code already has its own scaffolding; layering ours would distort the cross-language comparison.

#### What the template deliberately includes — tool-affordance hints (not CoT)

Distinct from CoT scaffolding above: pointing the agent at *available tools* is fair game, since the tools are an objective property of each language environment, not a thinking style. Surveyed iter 25 against [Anthropic's Claude Code best practices](https://code.claude.com/docs/en/best-practices), which finds "Claude mostly did well at verifying features end-to-end once explicitly prompted to use [testing tools]." Validates our existing trace-tip in `retry.md`.

For language-specific tool affordances, the system prompt mentions the tool, the task prompt does not prescribe *when* to use it. Examples that are okay:
- Dark: "`traces tail`, `traces replay <id> --diff`" (already in `retry.md`)
- TS: mention `node --watch` is available for iteration
- Py: mention `pytest` is available if the agent writes a test

Rule of thumb: *an instruction is okay if it's true regardless of which approach the agent takes*. "Use traces if a `run` fails" is okay (objective fact about the toolchain). "First write a test, then implement" is not okay (prescribes thinking).

#### Template versioning rules

1. Any text change → `prompt_template_hash` changes → new `sweep_id` → results are not directly comparable to prior sweeps. Acknowledge in the report.
2. If a Phase 3 improvement wave changes only the prompt (e.g. adds the trace tip), that's a *prompt-template-only* wave: keep `dark_sha` constant, change `prompt_template_hash`, run the bench.
3. The wrapper writes the resolved final prompt (after Jinja substitution) into `evals/runs/<sweep_id>/<run_id>/prompt.txt` for after-the-fact inspection.

#### Reproducibility settings (decided iter 31)

Cross-referencing `Darklang.LLM.Examples.CodeAgent` (Dark's own in-tree coding agent — prior art surveyed iter 31). CodeAgent pins `withTemperature 0.2`, `withMaxTokens 2000L`, `withMaxTurns 10L`. Our harness needs analogous knobs to keep `prompt_template_hash` actually meaningful — without them, sweep-to-sweep variance from random sampling would swamp the signal we're trying to measure.

**Pinned harness defaults**:

| Knob | Value | Reason |
|---|---|---|
| temperature | **0.0** | Stricter than CodeAgent's 0.2. Eval bench wants determinism: same `(dark_sha, prompt_template_hash, model_id, project, attempt_n)` should produce the same artifact, modulo provider non-determinism. Higher temperatures multiply variance. |
| max output tokens per turn | **16000** | Per vault `Agent Next Steps.md` item #4: CodeAgent's 2000 is "too low" for complex tasks; "16000+" was the proposed bump. We adopt 16K. |
| max turns per attempt | **50** | CodeAgent uses 10 (small-task assistant); Aider uses ~50. Our tasks span trivial → large; 50 covers the L tier without runaway. The §6 north-star ($0.50 budget) is the *real* cost cap; max-turns is a runaway-detector. |
| top_p, top_k | not set | Defer to provider defaults. With temperature=0, top_p/top_k don't materially change determinism. |
| seed (where supported) | derived from `run_id` | Anthropic SDK doesn't expose seed today; OpenAI does. When supported, seed = `hash(run_id)` to make individual runs reproducible. |

These knobs are part of the `prompt_template_hash` input — change a knob, change the hash, start a new measurement series.

**Why not match CodeAgent's settings exactly**: CodeAgent is an *interactive* coding assistant for ad-hoc tasks; our harness runs *non-interactive* batch evaluations with a budget cap and a rubric runner. Different optimization targets:
- Interactive wants quick turnarounds (low max_tokens), some creative variance (temperature=0.2), short loops (max_turns=10).
- Eval wants reproducibility (temperature=0), task completion (large max_tokens), and runaway protection (max_turns=50).

**`withFileTools`-style bundling as future migration target**: CodeAgent's `withFileTools` exposes file-ops as a *single grouped tool* in one Agent.create call. When the harness eventually ports to Dark (per §4.0 future migration path), the equivalent would be a `withDarkCliTools` bundle that exposes `dark fn`, `dark run`, `dark traces`, etc. as one grouped tool. Out of scope for Phase 1–4; flag for the migration milestone.

#### ~~Open question~~ Resolved iter 50

~~What's the right `__HARNESS_DONE__` polling cadence?~~ Resolved iter 50: the bench polls Multi's `<branch>/.claude-task/phase` file every 5 s (matching ralph's internal cadence). Agent emits `<phase>DONE</phase>`, ralph's `check_phase_transition()` writes the resolved phase to that file, the bench wrapper reads it. Single source of truth; no transcript regex needed.

