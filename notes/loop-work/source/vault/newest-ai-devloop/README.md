# Dark as the optimal AI coding target

> A user opens Claude Code, says *"build me a thing in Darklang,"* and it works better — *measurably* better — than building it in TypeScript or Python. This directory is the plan to make that real.

## TL;DR

Two coupled efforts:

- **[improvements.md](improvements.md)** — 30 enumerated changes to Dark (CLI, error UX, tooling, sharing) to compress the AI dev loop. Ordered by ship cost. All six sub-sections (Discovery, Authoring, Verification, Iteration, Sharing, Human review) are concrete; nothing's a stub.
- **[plan.md](plan.md)** — eval harness design. ~100 sample projects benchmarked across Dark / TS / Py. Single-Python-wrapper + language-native rubrics. Local-first.
- **[projects.md](projects.md)** — 23 vetted projects (15 apps + 8 library ports from F#/Elm/OCaml) of the 100 target. Wider candidate catalog from a vault project survey.

Coupled by **[§7 Phase 3 A/B protocol](plan.md#phase-3--first-improvement-wave-the-ab-protocol)**: every Dark improvement lands on a branch; the harness measures; bench data drives merge-or-revert.

## North-star

**Pass rate at $0.50 per agent per project** — see [§6.0](plan.md#60-north-star--metric-tiers). Single number combining pass + cost; robust to model swaps; maps to "if I give Claude $0.50 to build this, will it work?". Reported alongside **fix-iteration delta** (pass@2 − pass@1), where Dark's tight-feedback story is expected to win biggest.

## Headline 4 metrics (the dashboard front page)

1. **Pass rate at fixed cost budget** *(north-star)*
2. **Pass rate per tier @ unbounded budget** *(capability vs cost-bounded)*
3. **Fix-iteration delta** *(Dark's expected biggest win)*
4. **Trace adoption rate (Dark only)** *(behavioural leading indicator that §3.3 work changed the loop)*

## Phase 1 (1–2 days)

3 projects (`hello-cli`, `csv-to-json`, `url-shortener-cli`) × Dark/TS/Py × 1 attempt. Sequential, no parallelism. Definition of done: 9 rows in `evals/results.jsonl` + regenerated `evals/report.md`. *Numbers don't have to look good — they have to be real.* Full punch list: [§7 Phase 1](plan.md#phase-1--skeleton-harness-end-to-end-target-12-days).

## Phase 3 wave queue — what ships, in what order

| # | Wave | Bundle | Cost |
|---|---|---|---|
| 1 | Prompt-only | CLAUDE.md template + agent SUMMARY.md + merge-tip + trace-tip | **Zero Dark code** |
| 2 | `--json` rollout | 9 missing flags + `builtins` coercion fix | Single shared formatter |
| 3 | Authoring headliners | `dark edit` + auto-emit diagnostics | Medium CLI-surface |
| 4 | Error-UX bundle | parse-error suggestions + did-you-mean + auto-attach trace | CLI-surface |
| 5 | `dark publish` MVP | the §1 "share with a friend" promise becomes real | Largest of first 5 |

Wave 1 is intentionally first as a **harness sanity check** — if a prompt-only delta doesn't move §6 metrics, the bench is broken; find out cheaply before committing expensive engineering. Aider's harness uses the same trick. Wave 1 also gets a **sub-A/B** that isolates which of the 4 prompt changes contributes most.

## Strategic decisions made (with iter pointers)

- **Hybrid Python + language-native rubrics** ([§4.0](plan.md#40-harness-layout--language-decided-2026-05-02), iter 5) — don't depend on what you're measuring.
- **`results.jsonl` (not parquet) + 3-tier retention** ([§4.5.1](plan.md#451-retention-policy-decided-iter-27), iter 27) — append-only matters more than columnar speed at <100 MB/year.
- **Strict-mode bench is headline; realistic-mode is a Phase 4+ honesty check** ([§4.3.2](plan.md#432-constraint-mode-policy-decided-iter-36), iter 36) — strict measures Dark-as-platform; realistic measures Dark-vs-bash-fallback. Win condition: the strict-vs-realistic delta narrows as Dark improves.
- **Hybrid framework-pinning** ([§4.3.1](plan.md#431-framework-pinning-policy-decided-iter-16), iter 16) — pin runtimes + dependency-snapshot timestamps; no framework allowlist.
- **MCP server is Phase 4+, not part of the bench** ([§7 Phase 3](plan.md#what-phase-3-explicitly-does-not-do), iter 11) — bench measures Dark-as-primary-platform; MCP measures Dark-as-tool-from-Cursor; different experiments.
- **Reproducibility settings pinned** ([§4.7](plan.md#reproducibility-settings-decided-iter-31), iter 31) — temperature 0.0, max_tokens 16000, max_turns 50, seed = hash(run_id) where supported.
- **Library ports as a project class** ([projects.md](projects.md#library-port-candidates--different-shape-from-app-projects-added-iter-29), iter 29) — F#/Elm/OCaml port candidates are *seed crystals* for a richer Dark stdlib.

## Files

| File | Contents | Lines |
|---|---|---|
| [`README.md`](README.md) | This file — exec summary + map | ~80 |
| [`plan.md`](plan.md) | Vision (§1), thesis + competitive landscape (§2), harness design (§4), open Qs (§5), metrics (§6), phasing + A/B protocol (§7), risks (§8) | ~700 |
| [`improvements.md`](improvements.md) | Dark improvement backlog (§3), strengths-to-surface, doc bugs, runtime gaps | ~250 |
| [`projects.md`](projects.md) | 23 vetted projects (15 apps + 8 library ports) + wider candidate catalog | ~340 |
| [`research-log.md`](research-log.md) | Append-only iteration log; latest at top | ~340 |

## How the loop works

A cron-scheduled prompt fires every 5 minutes. Each iteration reads [`../ai-devloop-prompt.md`](../ai-devloop-prompt.md), picks one focused move (concretize / decide / verify / stress-test / survey / refine-metrics / compaction / restructure), and updates the relevant file + appends a log entry. Hard constraints: **no source code changes, no commits, one focused improvement per tick, cite sources, verify before claiming.**

Loop is session-only (cron `9eadf609`). To stop: `CronDelete 9eadf609` or close the session.
