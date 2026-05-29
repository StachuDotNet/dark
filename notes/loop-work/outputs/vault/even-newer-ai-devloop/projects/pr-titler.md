---
title: pr-titler
tier: M
class: llm-cli
modules: [Darklang.LLM.Agent, Stdlib.Cli, Stdlib.Cli.Process, Stdlib.String]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: false
---

# Description

A command-line tool that reads a git diff (from stdin or a `--file` flag) and produces a conventional-commit-style title via an LLM call. Output format: `<type>(<scope>): <description>` (single line, ≤72 chars).

The point of this project is **LLM-Agent integration**: the artifact uses an LLM-tool framework (Dark's `Darklang.LLM.Agent`, TS's Anthropic SDK, Py's `anthropic` package, Go's `github.com/anthropics/anthropic-sdk-go`, Rust's `anthropic-sdk`) to make a single bounded LLM call and post-process the response into the conventional-commit format.

The tool must also support an **offline fixture mode** (`--fixture <path>`) that bypasses the LLM and returns a canned response. This is essential for testing — the rubric uses fixture mode for reproducibility, and live mode is for manual verification only.

For Dark, `Darklang.LLM.Agent` exists with `withSystemPrompt`, `withMaxTokens`, `withMaxTurns`, `withTemperature` — verified iter 30/31 in projects.md and `Darklang.LLM.Examples.CodeAgent`. All 5 languages have first-class support.

# Behaviours

- `pr-titler --diff <path>` reads a unified-diff file and prints a single line of the form `<type>(<scope>): <description>` (or `<type>: <description>` when scope isn't obvious).
- `cat diff.txt | pr-titler` reads diff from stdin (no `--file`).
- The output line is ≤72 characters total.
- `<type>` is one of: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `chore`. The agent must pick one based on the diff (no `unknown:` fallback in the rubric's expected output).
- For a diff touching `README.md`, `<type>` is `docs:`.
- For a diff that adds a new function in a non-test file, `<type>` is `feat:`.
- For a diff that touches `*.test.*` files only, `<type>` is `test:`.
- `<scope>` is optional — present if the diff is contained within a single module/dir, omitted if cross-cutting.
- **`pr-titler --fixture fixtures/sample-1.json`** returns the response from the JSON fixture instead of calling the LLM. Fixture format: `{"diff": "...", "expected_output": "feat(auth): add OAuth flow"}`. The CLI returns `expected_output` verbatim; this is what the rubric uses.
- `pr-titler --help` exits 0.
- LLM call failure (network error, rate limit) is logged to stderr; CLI exits non-zero with a clear error.
- `pr-titler --diff /nonexistent` exits non-zero.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Set up auth: `claude --print "ping"` succeeds (host-side OAuth, per §4.7 iter-51 correction). Bench wrapper inherits this; pr-titler uses the same.
2. **Live test**: run `git diff main | pr-titler` on a real git diff. Expect a sensible conventional-commit title.
3. **Determinism check** (live mode): run twice with the same diff. With `temperature 0.0` (per the spec's reproducibility settings), the two outputs should be identical.
4. **Fixture test**: `pr-titler --fixture tests/fixtures/sample-add-auth.json`. The expected output should print verbatim. Used by the rubric for reproducible scoring.
5. **Type-classification test**: feed it a docs-only diff. Output should start with `docs:`. Feed it a test-only diff: should be `test:`.
6. **For Dark specifically**: examine the source. Did the agent reach for `Darklang.LLM.Agent.create() |> withSystemPrompt "..." |> withMaxTokens N |> withTemperature 0.0`? *They should* — that's the iter-31 reproducibility setup directly applied. **Failure to use `Darklang.LLM` is a Discovery failure (§3.1)** — agents who hand-roll HTTP requests to the Anthropic API are doing it wrong. Note in `SUMMARY.md`.
7. **Cost-guard test**: examine the source for `withMaxTokens` / `withMaxTurns` calls. The Dark agent should bound LLM cost; an unbounded call against a 10K-line diff could exhaust the budget. *Spec specifically expects this defensive coding.*
8. Look at the prompt the agent gives the LLM. It should fit in a system prompt + a single user-message containing the diff. **Multi-turn agent loops are wrong here** — this is a one-shot translation, not a conversation.
9. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- pr-titler --fixture tests/fixtures/sample-feat.json
- echo "" | pr-titler   # empty diff — implementation-defined behavior
- pr-titler --help
- pr-titler --diff /nonexistent

---

**Why this spec belongs in the breadth picks**: covers Class H (LLM-powered CLIs). The catalog has 8 LLM-CLI candidates (`commit-msg` already exists in Dark; `pr-titler`, `pr-review`, `code-explainer`, `bug-triage`, `natural-shell`, `daily-standup`, `docstring-filler`). `pr-titler` is the simplest — single bounded LLM call, deterministic with fixture mode, easy rubric.

**Why `expected_outcome: pass`**: Dark has `Darklang.LLM.Agent` (verified iter 30 — 11 example modules incl. `Darklang.LLM.Examples.CodeAgent`); other languages have mature SDK packages. No language is structurally disadvantaged. The differentiator is *how easily the agent reaches for the LLM library* — the §3.1 Discovery audit catches Dark agents who hand-roll HTTP instead.

**Cost-guard is a spec requirement**: per iter-31 reproducibility settings (temperature 0.0, max_tokens 16000, max_turns 50), the agent's LLM call should bound itself. **Self-check step 7 verifies this** — agents who fire unbounded LLM calls fail the spec even if the output looks right.

**Bench-vs-fixture mode**: the rubric uses fixture mode for reproducibility (no live LLM calls during a sweep — too expensive, non-deterministic). Live mode is for the human reviewer's manual verification only. **Cross-spec note**: this fixture pattern can be reused for any future LLM-CLI specs (`commit-msg`, `pr-review`, etc.) — saves cost on every sweep.
