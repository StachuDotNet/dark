# Dashboard + exportable reports

> Extracted from plan.md §6.2 (iter 111) — discrete artifact spec for the bench's report-generation pillar. Two outputs (per-sweep report + over-time dashboard) regenerable with one command, copy-paste-able into Slack/email/PRs.
>
> See `samples/dashboard-mock.html` for a hand-built preview of what the rendered output looks like after ~7 sweeps. Cross-references plan.md §4.5 (storage layout) and §4.10 launch checklist.

The new pillar from this loop. Two outputs:

1. **Per-sweep report** — what just happened tonight. Markdown + HTML, generated when the sweep finishes.
2. **Over-time dashboard** — how Dark is improving across sweeps. Single HTML page regenerated when any new sweep lands.

Both are **static files** committed nowhere — they live in `evals/bench/`, get regenerated, and are shared by `scp`/email/PR-comment/upload. **No server, no JS framework, no hosting story required for tonight.**

#### The three views

Picked iter 38; reaffirmed iter 45.

| # | View | Audience | Format |
|---|---|---|---|
| 1 | **Snapshot vs the competition** — per-project, per-language. Dark vs TS vs Py on each Headline metric. | Anyone wanting "where do we stack today?" | Bar charts, color-coded by tier |
| 2 | **Are we improving?** — Dark over time. X-axis = sweep date / sweep_id. Y-axis = each Headline metric. | The user + their coworker — the "are we winning?" view | Line charts, one per metric, optionally overlaid with TS/Py horizontal-lines as the "competitor's score" reference |
| 3 | **What just changed?** — most-recent sweep vs prior. Per-project regressions / wins. | Whoever's reading the morning report | Tables with delta arrows + core-projects summary box |

#### File layout

```
evals/bench/
  results.jsonl                 # global, append-only, all sweeps
  pricing.json                  # per-model $/token (§6.0)
  baselines.json                # pinned (dark_sha, sweep_id) per §7 Phase 3
  sweeps/<sweep_id>/
    manifest.json               # what was queued
    runs/<run_id>/...           # per-run artifacts (§4.8)
    results.jsonl.partial       # rolled into the global one when sweep ends
    report.md                   # ← per-sweep report (#3 view)
    report.html                 # ← per-sweep report, with charts inline
  dashboard/
    index.html                  # ← over-time dashboard (views #1 + #2)
    data.json                   # extracted from results.jsonl, used by index.html
    charts/                     # SVG chart files, regenerated each sweep
```

The over-time dashboard lives in a **single HTML file** at `evals/bench/dashboard/index.html`. Open it in a browser; everything's inline (charts as SVG, data as inline JSON). Trivially shareable: `scp evals/bench/dashboard/index.html coworker:`.

#### Per-sweep report shape (`report.md`)

```markdown
# Bench sweep <sweep_id> — 2026-05-05 23:47

**Overall**: Dark 4/5 core pass • $0.62 spent • 11 m wall

## Headline metrics

| Metric | Dark this sweep | Dark last sweep | TS baseline | Py baseline |
|---|---|---|---|---|
| Pass rate at $0.50 | 80% | 80% | 100% | 100% |
| Pass rate unbounded | 100% | 100% | 100% | 100% |
| Fix-iter delta | n/a (pass@1 only) | — | — | — |
| Trace adoption rate | 60% | 40% | — | — |

## Per-project (core-only sweep)

| Project | Tier | Status | Tokens | Wall | Cost | Notes |
|---|---|---|---|---|---|---|
| password-gen | T | ✅ pass | 1.2k | 84s | $0.04 | first-fn at turn 2 |
| cron-describe | S | ✅ pass | 4.8k | 6m | $0.18 | clean parser |
| markdown-toc | S | ✅ pass | 3.1k | 5m | $0.12 | UTF-8 path tested |
| validation-applicative | S/lib | ❌ fail | 8.3k | 11m | $0.31 | rubric: applicative didn't accumulate |
| parser-combinators | M/lib | ✅ pass | 6.0k | 9m | $0.23 | type inference fine |

## Diagnostics

- Time-to-first-fn (median): 47s ↓ from 71s last sweep — is wave 1 prompt change live? *(yes — CLAUDE.md template was added)*
- Doc-bug encounters: 0 ✓
- Constraint-escape attempts: 0 ✓
- CLI cold-start overhead: 6.3s/run avg (consistent with iter-23 baseline)

## What changed since last sweep

- **+** Trace adoption rate jumped from 40% → 60% (3/5 core projects invoked `traces`). *Wave 1 trace-tip works.*
- **−** validation-applicative regressed: previously passing, now fails on "errors must accumulate" check. *Investigate the parsing of `andThen` in the agent's output.*
- Cost is steady at ~$0.13/run.

## Run-level details

[`runs/`](sweeps/2026-05-05-2347/runs/) — full transcripts + telemetry per run. See per-run SUMMARY.md (§3.6 #3) for what the agent thought it was doing.

---

*Generated 2026-05-05 23:58 by `multi bench report` from `results.jsonl` rows.*
*Headline metrics as of [§6.0](../plan.md#60-north-star--metric-tiers) revision iter 39.*
```

The Markdown is the **shareable report** — Slack/email/PR-comment friendly. The HTML version is the same content with embedded SVG charts (rendered from `data.json` via the dashboard generator).

#### Over-time dashboard shape (`dashboard/index.html`)

Single HTML file, opened locally or shared. Layout:

```
[Bench dashboard — Dark vs TS vs Py — last updated 2026-05-05 23:58]

[NORTH-STAR PANEL]
Pass rate at $0.50/project, last 14 sweeps
[line chart: Dark trending up, TS/Py horizontal reference lines]
Latest: Dark 80%, TS 100%, Py 100%. 14-sweep delta: Dark +30%.

[FIX-ITER-DELTA PANEL]
[line chart: Dark only, the metric we expect to climb]
Latest: 12% (was 4% at sweep 1)

[TRACE ADOPTION PANEL]
[line chart: Dark only, behavioral metric]
Latest: 60% (was 0% at sweep 1, +20% this sweep)

[CORE PROJECTS PANEL]
Per-core pass rate over time:
  password-gen ✅✅✅✅✅
  cron-describe ✅✅✅✅✅
  markdown-toc ✅✅✅✅✅
  validation-applicative ✅✅✅✅❌
  parser-combinators ✅✅✅✅✅

[COST PANEL]
[bar chart: $/sweep over last 30 days]
Total this month: $42. Per-sweep avg: $0.65.

[LAST-SWEEP DELTA PANEL]
Same content as the per-sweep report's "What changed since last sweep" section.

[FOOTER]
Methodology: link to README.md in the repo
Pricing: link to pricing.json
Source data: link to results.jsonl (or "[copy data.json](data.json)")
```

#### Generator command (illustrative)

```
python -m harness report <sweep_id>          # Per-sweep report (md + html)
python -m harness dashboard [--last N]       # Over-time dashboard
python -m harness export <sweep_id> --pdf    # Generates PDF for sharing (uses chrome-headless or weasyprint)
```

#### Implementation choice for tonight: **matplotlib + Jinja**

For the simplest possible "tonight: visual lands" path:

- **Chart engine**: matplotlib in Python. Renders SVG inline. Zero JS dependency. Static files only.
- **Templating**: Jinja2 templates (already in §4.7 for the prompt template — same library). One `report.md.j2` template + one `index.html.j2` template.
- **Data**: read `evals/bench/results.jsonl` directly; pandas for aggregation if useful (not strictly required).
- **PDF export**: `weasyprint` (Python lib, takes HTML + CSS, produces PDF) or punt to `~/bin/print-md` as the user already uses.

Why not Plotly / Observable / Streamlit / a SPA framework: they all require either a server, a build step, a JS runtime, or a hosting story. The bench's report should be *self-contained* and *un-hostable* — copy the HTML file anywhere, it works. Matplotlib + inline SVG is the minimal path.

**Future**: once the bench is stable for 3+ sweeps and the §3.5 #1 `dark publish` ships, port the dashboard generator to Dark using `Stdlib.HttpServer` + `Stdlib.Html`. Same pattern as the §4.0 future-port-to-Dark milestone for the wrapper. Dogfooding.

#### Shareability — three concrete paths

1. **scp / rsync**: `scp evals/bench/dashboard/index.html coworker:public_html/dark-bench-tonight.html`. Coworker opens in their browser. **Works tonight.**
2. **GitHub Pages or static-hosted bucket**: `cp evals/bench/dashboard/* ~/dark-bench-pages/` then push that repo. Works once we want a stable URL — *not blocking tonight.*
3. **Email PDF**: `python -m harness export <sweep_id> --pdf` → email the PDF. Works for "send the morning report at 8am as an email" once we wire that up — *not blocking tonight.*

#### What the dashboard does NOT include

- **No interactive filtering** beyond what the static HTML supports (could add `<details>`/`<summary>` collapsibles, but no JS-driven filters)
- **No real-time updating** — re-running `python -m harness dashboard` is the refresh
- **No login / auth** — the report is freely shareable; if the user wants to gate access, that's a hosting concern, not a bench concern
- **No historical drill-into-a-specific-run** — those live in `evals/bench/sweeps/<id>/runs/<run_id>/`, accessible by file path. The dashboard *links* to them but doesn't render them
- **No aggregations across sweep_ids with different `prompt_template_hash`** — the cross-sweep dashboard groups by hash and labels regime changes. (Same discipline as iter 31 reproducibility settings: changing the prompt → new measurement series.)

#### Tonight's specific dashboard scope

For tonight (core-only sweep + first dashboard render):

- Skip view #2 (over-time) — only one sweep exists. Just show "this is sweep 1" in a header.
- View #1 (snapshot vs TS/Py) renders, but TS/Py rows may be empty if we haven't run them yet — show as `--` rather than 0.
- View #3 (what just changed) renders with no "last sweep" comparison; show a core pass-list and the per-project table.
- Single HTML file at `evals/bench/dashboard/index.html`. **The visual deliverable.**
- Markdown report at `evals/bench/sweeps/<id>/report.md`. **The shareable artifact.**

Both regenerable with one command. Both copy-paste-able into Slack or email or a PR comment. Done.

## Multi-language cost attribution + cross-cadence display *(round-2 P2 #17 + #18, iter 116)*

The §4.3 cross-language extension to 5 languages (Dark/TS/Py/Go/Rust) and the §4.3 cadence split (weekly cross-language baselines vs every-day-or-two Dark runs) introduce two display concerns the dashboard has to handle.

### Cost attribution (resolves round-2 P2 #17)

**Pricing is per-model, not per-language.** All 5 languages run against the same Anthropic API endpoints; cost differences come from token volume, not from the language label. So `pricing.json` stays language-agnostic — its keys are model names (`claude-sonnet-4-6`, `claude-haiku-4-5`, etc.) and its values are `{input_per_mtok, output_per_mtok, cache_read_per_mtok, cache_write_per_mtok}`.

What the dashboard *does* need: a **per-run language tag** that joins to the run's cost field at display time. Each row in `runs.jsonl` already has `language: <dark|ts|py|go|rust>` from the §4.5 storage spec. The dashboard's cost-attribution formula:

```python
run_cost_usd = (
    pricing[run.model].input_per_mtok * run.tokens_input / 1e6 +
    pricing[run.model].output_per_mtok * run.tokens_output / 1e6 +
    pricing[run.model].cache_read_per_mtok * run.tokens_cache_read / 1e6 +
    pricing[run.model].cache_write_per_mtok * run.tokens_cache_write / 1e6
)
# Per-language aggregations group by run.language; pricing.json is unchanged.
sweep_cost_by_lang = groupby(runs, key=lambda r: r.language).map(sum(.cost_usd))
```

Dashboard panels that need this: per-language cost-per-pass column in view #1; per-language cost over time in view #2 (one line per language). No `pricing.json` change required.

### Cross-cadence display (resolves round-2 P2 #18)

The two cadences are intentionally asynchronous — weekly TS/Py/Go/Rust baselines move slowly (the reference impls don't change much); every-day-or-two Dark runs move quickly (each new improvement wave triggers one). View #1 (snapshot vs other-languages) and view #2 (Dark over time) need to display these without misleading the reader.

**Display rules**:

1. **Each cell shows its sweep_id timestamp**, not just a value. View #1's "TS pass-rate: 87%" becomes "TS pass-rate: 87% *(2026-04-28 baseline)*" so the reader sees that the comparison column is from a week ago.
2. **The Dark column always reflects the most recent Dark sweep**; the other-language columns reflect the most recent baseline of that language. They are **not synchronized** to the same sweep_id triple. The dashboard's header banner explicitly notes this: *"Dark vs TS/Py/Go/Rust comparison uses the latest baseline of each. Click for sweep_ids."*
3. **View #2 (over time) plots each language as a separate line**. Dark gets ~30 data points/month (every-2-day cadence); other-language baselines get ~4 (weekly cadence). Sparser baselines render as a step function (last value held until next sample) — which is fine because the reference impls genuinely don't change between baseline runs.
4. **View #3 (what just changed) is Dark-only by construction** — only Dark sweeps trigger this view. The other-language baselines don't get a "what changed since last sweep" panel because they don't change often enough to warrant one. The harness re-runs other-language baselines on a quarterly cadence (per [plan-analysis.md §8 risk #4](plan-analysis.md): "Reference implementations rot"); when one of those quarterly re-baselines fires, view #3 *can* render the cross-language delta as a one-off.
5. **The "current cost cap" header** is a tonight's-config display, not a per-cadence concern: same number across all languages within a sweep.

**The pitfall this avoids**: a naive dashboard would join "this week's Dark sweep" with "this week's TS sweep" and produce empty cells when the cadences don't align — making it look like Dark hadn't run when it had, or vice versa. The "use latest baseline of each" rule keeps every cell populated; the timestamp annotation prevents the reader from inferring synchrony that isn't there.
