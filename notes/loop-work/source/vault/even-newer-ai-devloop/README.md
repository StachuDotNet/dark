# Dark as the optimal AI coding target

> 👋 **Feriel — read [`for-feriel.md`](for-feriel.md) first.** That's a one-page handoff with the actual context (state of the work, what's blocking a launch, realistic scopes). The rest of this README is the broader map.

> A user opens Claude Code, says *"build me a thing in Darklang,"* and it works better — *measurably* better — than building it in TypeScript or Python. This directory is the plan to make that real, plus the architecture for a nightly bench that measures whether we're getting there.

## Operational kickoff — start here

The most actionable thing in the doc set is **[`launch-checklist.md`](launch-checklist.md)** — Phase A → D operational runbook, copy-paste commands, expected outputs, failure-mode table, falsifiable definition of done. Read it before kicking off any sweep.

Tonight's clock:
- **20:00** — core-only Dark sweep (~10 min wall, ~$0.65 API-equivalent)
- **20:25** — eyeball the report + dashboard
- **21:00** — full Dark sweep (~1.5 h wall, ~$7 API-equivalent), conditional on Phase C looking clean
- **23:00** — tonight's deliverable lands: `report.md` + `dashboard/index.html` + optional PDF
- **01:27 next morning** — first scheduled cron firing confirms cadence

## TL;DR

Two coupled efforts:

- **[improvements.md](improvements.md)** — 30 enumerated changes to Dark (CLI, error UX, tooling, sharing) to compress the AI dev loop. Ordered by ship cost. All six sub-sections (Discovery, Authoring, Verification, Iteration, Sharing, Human review) are concrete; nothing's a stub.
- **[plan.md](plan.md)** — eval harness design. ~100 sample projects benchmarked across Dark / TS / Py. Single-Python-wrapper + language-native rubrics. **Built on Multi's queue/processor/rate-limit infrastructure** ([§4.8](plan.md#48-orchestration-via-multi-concretized-iter-41), iter 41). Local-first.
- **[projects.md](projects.md)** — 23 vetted projects (15 apps + 8 library ports from F#/Elm/OCaml) of the 100 target. **5 core projects** picked as fix-iter-delta canaries. **22 specs fully materialized** as per-file documents under [`projects/`](projects/) — each is the agent-facing prompt (frontmatter + Description + Behaviours + Self-verification + Smoke commands). Wider candidate catalog from the vault project survey.

Coupled by the **[§7 Phase 3 A/B protocol](plan.md#phase-3--first-improvement-wave-the-ab-protocol)**: every Dark improvement lands on a branch; the harness measures; bench data drives merge-or-revert.

## North-star

**Pass rate at $0.50 (API-equivalent) per agent per project, over `expected-pass` rows only** — see [§6.0](plan.md#60-north-star--metric-tiers). Single number combining pass + cost; robust to model swaps; maps to "if you ran this on the API, would Claude succeed in $0.50?". Reported alongside **fix-iteration delta** (pass@2 − pass@1), where Dark's tight-feedback story is expected to win biggest.

The bench actually runs on **Claude Code subscription auth** (Pro/Max), not the metered API key — so real spend is $0 marginal. The cost cap is a quality-and-quota proxy that maps to API-equivalent dollars for cross-bench comparability ([iter 51 correction](plan.md#auth-wiring-corrected-iter-51--user-feedback-overrides-iter-50)).

## Headline 4 metrics (the dashboard front page)

1. **Pass rate at fixed cost budget** *(north-star, over expected-pass rows)*
2. **Pass rate per tier @ unbounded budget** *(capability vs cost-bounded)*
3. **Fix-iteration delta** *(Dark's expected biggest win)*
4. **Trace adoption rate (Dark only)** *(behavioural leading indicator that §3.3 work changed the loop)*

§6 has **32 metrics across 6 tiers** (Headline 4 / Supporting 7 / Diagnostic 6 / Sweep-level 6 / Harness self-health 6 / Workaround tracking 3). The Headline 4 stay locked at 4 — discipline.

## Specs materialized (22 of 22)

| Set | Specs |
|---|---|
| **Core projects** (run every nightly sweep, priority=5) | password-gen · cron-describe · markdown-toc · validation-applicative · parser-combinators |
| **Phase-1** (tonight's actual targets) | hello-cli · csv-to-json · url-shortener-cli |
| **Library ports** (from F#/Elm/OCaml/Haskell) | mvu-runtime · pretty-printer · url-builder-parser · json-pointer · pcg-random · csv (+ validation-applicative + parser-combinators in core projects) |
| **Expected-to-fail** (longitudinal gap-tracking) | parallel-downloader · jwt-rs256 · tar-zip-creation · realtime-roguelike · redis-driver |
| **Breadth picks** (different classes) | cron-lite (daemon) · mcp-fs (MCP server) · pr-titler (LLM-CLI) |

Each spec has YAML frontmatter (`expected_outcome` enum, `known_blockers` controlled vocab, `class`, etc.), Description, Behaviours, Self-verification (no human in loop — the agent runs through it before declaring `<phase>DONE</phase>`), Smoke commands. Library-port specs add 2 sections (Library API surface + Driver CLI). Each fail-likely spec uses a distinct **external-verifier** strategy (wall-clock / jwt.io / GNU tar / terminal observation / redis-cli).

**Dashboard preview**: see [`samples/dashboard-mock.html`](samples/dashboard-mock.html) — a hand-built HTML mock of what tonight's deliverable will look like after ~7 nightly sweeps. Includes a gap-detection banner (parser-combinators flipped), per-core pass-status table, sweep-7-vs-sweep-6 delta, harness-self-health panel, workaround-tracking table, per-run SUMMARY.md sample. Open it in a browser.

## Dashboard + reports

**[§6.2](plan.md#62-dashboard--exportable-reports-concretized-iter-45)** — three views (snapshot vs TS/Py · Dark over time · what just changed) rendered as **static HTML** + **shareable Markdown**. matplotlib + Jinja2 only; no JS framework, no server. `scp evals/bench/dashboard/index.html coworker:` is the sharing protocol. PDF export via weasyprint.

**Gap-detection events** (iter 70): when a `fail-likely` spec passes, the dashboard banners the gap-closure ("🎉 Dark closed the no-async-primitives gap").

## Phase 3 wave queue — what ships, in what order

| # | Wave | Bundle | Cost |
|---|---|---|---|
| 1 | Prompt-only | CLAUDE.md template + agent SUMMARY.md + merge-tip + trace-tip | **Zero Dark code** |
| 2 | `--json` rollout | 9 missing flags + `builtins` coercion fix | Single shared formatter |
| 3 | Authoring headliners | `dark edit` + auto-emit diagnostics | Medium CLI-surface |
| 4 | Error-UX bundle | parse-error suggestions + did-you-mean + auto-attach trace | CLI-surface |
| 5 | `dark publish` MVP | the §1 "share with a friend" promise becomes real | Largest of first 5 |

Wave 1 is intentionally first as a **harness sanity check** + sub-A/B'd to isolate which prompt change contributes most.

## Strategic decisions

22 strategic decisions logged across the two loops, each with iter pointer + section anchor. See `research-log.md` for the full chronology, or scan plan.md for the headers (each tagged with `decided iter N`).

Highlights:
- **Extend Multi in place with `multi bench`** — Multi's queue/processor is ~70% of orchestration we need.
- **`results.jsonl` (not parquet) + 3-tier retention.**
- **Strict-mode bench is headline; realistic-mode is Phase 4+ honesty check.**
- **Hybrid framework-pinning** — pin runtimes + dependency-snapshot; no framework allowlist.
- **MCP server is Phase 4+ ecosystem-reach, not part of the bench.**
- **Reproducibility settings pinned**: temperature 0.0, max_tokens 16K, max_turns 50.
- **Library ports are seed crystals** for a richer Dark stdlib.
- **5 core projects mapped 1:1 to §6 metric channels.**
- **Static-HTML dashboard, matplotlib + Jinja2** — zero JS, scp-shareable.
- **Nightly cadence**: core `27 1 * * *` then full `47 2 * * *` (full gates on core passing).
- **Sweep-lock** via `flock` + `--dry-run` mode + soft-cap-for-first-7-sweeps.
- **Done-detection via Multi's phase file** (`<phase>DONE</phase>`, poll `.claude-task/phase`).
- **Auth: Claude Code subscription, not API key** — host-side OAuth, $0 marginal.
- **Cost-attribution formula in `pricing.json`** — API-equivalent dollars for cross-bench comparability.
- **Spec format defined**: YAML frontmatter (8 fields incl. `expected_outcome` + `known_blockers`), 4-section body for apps, 6-section body for library ports.
- **Expected-to-fail specs run, they don't get skipped** — bench tracks gap-closure as positive signal.
- **External-verifier-per-failure-class** — each fail-likely spec has a distinct external-verifier the agent can't fake.
- **Workaround tracking (iter 70)** — count `Stdlib.Cli.Process.exec` invocations; idiomaticity ratio alarm at <80%.

## Files

| File / dir | Contents | Lines |
|---|---|---|
| [`README.md`](README.md) | This file — exec summary + map | ~145 |
| [`plan.md`](plan.md) | The spec: harness design (§4.0-§4.6), metrics (§6 — 32 metrics across 6 tiers) | ~450 |
| [`plan-analysis.md`](plan-analysis.md) | The why: thesis + competitive landscape (§2), open Qs (§5), risks (§8), references | ~85 |
| [`prompt-template.md`](prompt-template.md) | §4.7 extracted: the literal `system.{lang}.md` / `task.md` / `retry.md` template files the harness wrapper sends to the agent (reproducibility-critical — `prompt_template_hash` is part of every sweep_id) | ~200 |
| [`multi-orchestration.md`](multi-orchestration.md) | §4.8 extracted: Multi-extension spec (`multi bench` subcommands, queue.json schema deltas, branch isolation, telemetry correlation, rate-limit handoff) | ~180 |
| [`nightly-cadence.md`](nightly-cadence.md) | §4.9 extracted: cron-fired nightly sweep operational spec (cron expression, ordering, sweep_id naming, failure handling, cost-cap escalation) | ~175 |
| [`launch-checklist.md`](launch-checklist.md) | §4.10 extracted: Phase A-D operational runbook for the kickoff sweep (core projects first, full bench second, dashboard render, share protocol) | ~175 |
| [`dashboard-spec.md`](dashboard-spec.md) | §6.2 extracted: discrete artifact spec for the bench's report-generation pillar (per-sweep report + over-time dashboard + cost attribution + cross-cadence display) | ~245 |
| [`phasing.md`](phasing.md) | §7 extracted: Phase 0 → Phase 5+ project plan + A/B improvement protocol | ~195 |
| [`improvements.md`](improvements.md) | Dark improvement backlog (§3) — index of 29 per-recommendation files in `improvements/` + appendix sections (cross-cutting / strengths / doc bugs / runtime gaps) | ~165 |
| [`improvements/`](improvements/) | 29 per-recommendation standalone files (one per fix proposal across §3.1-§3.6) | 29 files |
| [`projects.md`](projects.md) | Catalog: 23 vetted projects + spec-format spec + **22-row index linking to `projects/`** + wider candidate catalog | ~510 |
| [`projects/`](projects/) | 22 fully-materialized agent-facing specs (one per project) | 22 files |
| [`feedback-plan.md`](feedback-plan.md) | Round-2 P0/P1/P2 checklist; rolling status of the 5-min loop's progress | ~115 |
| [`samples/dashboard-mock.html`](samples/dashboard-mock.html) | Hand-built mock of what the static-HTML dashboard will look like after ~7 nightly sweeps — 7 panels + 5 tables + the gap-detection banner | ~150 |
| [`samples/historical/PROGRESS.md`](samples/historical/PROGRESS.md) | First-loop wrap-up snapshot (iter 59); kept for historical reference | ~95 |
| [`samples/historical/research-log-loops-1-3.md`](samples/historical/research-log-loops-1-3.md) | Original append-only iteration log (loops 1-3, 80+ iters); archived iter 109 | ~1010 |
| [`research-log.md`](research-log.md) | "Retired" stub explaining where iter-findings now live (feedback-plan / improvements / projects / plan-analysis / reflection-template) | ~30 |

## How the loop works

A cron-scheduled prompt fires every 5 minutes. Each iteration reads [`../ai-devloop-prompt.md`](../ai-devloop-prompt.md), picks one focused move (concretize / decide / verify / stress-test / survey / refine-metrics / compaction / restructure), and updates the relevant file + appends a log entry. Hard constraints: **no source code changes, no commits, one focused improvement per tick, cite sources, verify before claiming.**

Four loop sessions have run:
- **Loop 1** (iter 0–40, 2026-05-02): the original bench design.
- **Loop 2** (iter 41–59, 2026-05-05 morning): Multi-extension + nightly cadence + dashboard + Phase-1 launch checklist + 5 core specs.
- **Loop 3** (iter 60–74, 2026-05-05 mid-morning): all 13 remaining specs initially in the roadmap + workaround-tracking metrics + README synthesis.
- **Loop 4 add-on** (iter 75–80, 2026-05-05 late-morning): the 4 library-port specs I'd flagged as "could keep doing but skipped" + dashboard HTML mock + this update.

All cron jobs cancelled. Mechanical work is exhausted *(again — and now the spec catalog is genuinely complete)*.
