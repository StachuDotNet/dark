# Tonight's queue (2026-05-05, 21:00 kickoff)

The 10 projects to run tonight, in priority order. Each runs across all 5 languages (Dark, TS, Py, Go, Rust). 10 projects × 5 languages = 50 attempts. With `nproc/2` concurrency (~4 parallel), and ~7-10 min wall per attempt, total time ≈ 1.5–2.5 h. **Wake-up target ~07:00 → comfortable margin.**

## The 10

| # | Project | Tier | Class | Outcome | Why this slot |
|---|---|---|---|---|---|
| 1 | `password-gen` | T | app | pass | Core #1 — smoke + RNG/Crypto, fastest |
| 2 | `cron-describe` | S | app | pass | Core #2 — pure parsing, fix-iter-delta canary |
| 3 | `markdown-toc` | S | app | pass | Core #3 — Regex + UTF-8 byte-vs-codepoint stress |
| 4 | `validation-applicative` | S | library-port | pass | Core #4 — ADT semantics + edit-format compliance |
| 5 | `parser-combinators` | M | library-port | stretch | Core #5 — type-system stress |
| 6 | `url-shortener-cli` | S | app | pass | **The Dark UserDB differentiator** — `Stdlib.DB` no-setup-required story |
| 7 | `csv-to-json` | S | app | pass | Phase-1 parsing project; tests the `no-csv-stdlib` gap |
| 8 | `mvu-runtime` | M | library-port | pass | Library port + ethos-validation (vault flags MVU as core) |
| 9 | `http-healthz` | M | app | pass | **The HTTP requirement** per round-2 P0 #6. (Needs full spec materialization before tonight — see "Spec-pending" below.) |
| 10 | `parallel-downloader` | M | app | fail-likely | First expected-to-fail gap-tracker — async/concurrency blocker |

## Why this composition

- **5 core projects** (#1–5) give the metric-channel coverage by design (per iter 43): tokens / fix-iter-delta / first-parse-success / edit-format / rework-ratio.
- **Both halves of P0 #6** covered: `url-shortener-cli` (#6) is UserDB-bound; `http-healthz` (#9) is HTTP-bound. Combined coverage in 2 projects.
- **Tier spread**: 1 T / 6 S / 3 M. No L (paste-bin would be the L; defer for a less-calibration-heavy night).
- **Class spread**: 7 apps / 2 library-ports / 1 expected-fail. TUI / daemon / mcp-server / llm-cli all deferred — covered in the broader catalog, not in tonight's "broad spectrum but bounded scope" set.
- **Outcome spread**: 8 pass / 1 stretch (parser-combinators) / 1 fail-likely (parallel-downloader). Calibrates the dashboard's expected-vs-actual rendering on day-1.

## Spec-pending

`http-healthz` (#9) has only a compact entry in the iter-2 starter-set; no fully-shaped spec exists in `projects.md` yet. **This is the only spec gap for tonight.** A future loop iter (or the implementer) writes the spec following the iter-55 format. Estimated time: ~20 min of writing.

If the spec doesn't get written before 21:00, drop `http-healthz` and run 9 projects across 5 langs = 45 attempts. The HTTP coverage requirement loosens a bit but the bench still launches.

## What's NOT in tonight's queue and why

- `hello-cli` — too trivial; serves as bench-infra smoke, not a measurement target. Run once at the very start of tonight as a pipeline check; don't include in the 10.
- `pretty-printer` (library port) — high value but partially-spec'd; defer to next sweep.
- `redis-driver` / `jwt-rs256` / `tar-zip-creation` / `realtime-roguelike` — deeper expected-to-fail projects; one is enough for day 1 (`parallel-downloader`). Add the others to subsequent sweeps once we know how `fail-likely` rendering looks in the dashboard.
- `cron-lite` (daemon) — long-running tests don't fit cleanly in a 7–10 min budget per attempt.
- `mcp-fs` (mcp-server) — security/path-guard subtlety; first sweep should optimize for cleaner signal.
- `pr-titler` (llm-cli) — would need fixture-mode wired up tonight; not bench-reflex.

These are all high-quality projects; they go into the next sweep (week-of cross-language baseline) or a Dark-only sweep when a relevant improvement ships.

## Cross-language run order

Per project, languages run in parallel where the wrapper allows. Priority order within a project:
1. Dark (the one we care about most + has the most setup variance)
2. Python (most familiar to the agent — likely fast)
3. TypeScript (also familiar)
4. Go (less familiar — token cost may be higher)
5. Rust (least familiar — type-system + lifetime headaches)

If the wrapper hits a parallelism limit, drop in this priority. Dark always runs.

## Cost projection (API-equivalent dollars)

- 10 projects × 5 languages × 1 attempt = 50 runs.
- ~$0.15–$0.30 per run × 50 = **$7.50–$15 total** for tonight's API-equivalent cost.
- Real spend = $0 marginal (subscription auth, per iter 51).
- Round-2 P0 #3 dropped the soft cap for tonight; pace by concurrency only.

## What "good" looks like at 07:00 tomorrow

- 50 rows in `evals/bench/results.jsonl`.
- A regenerated `dashboard/index.html` showing the snapshot view (no over-time view yet — we have 1 sweep).
- `report.md` per language summarizing pass/fail per project.
- A `reflections/` directory with one entry per Dark run capturing "what could Dark CLI UX do better" (per round-2 P0 #11).
- Total wall time ~2 h; total API-equivalent cost under $20.
