# Loop progress — 2026-05-05

> Wrap-up summary written when the loop was stopped at iter 59 (out of a planned ~36–40-iter / 3-hour budget). Cron `f0c0f677` cancelled. Sweep up of where we ended; what's still open; what to do first when you come back.

## What this loop did (iter 41–59, ~19 iterations)

The original loop (iter 0–40) produced the bench design. This loop extended that design with **Multi-as-orchestration**, **nightly cadence**, **the dashboard / shareable-reports pillar**, and **agent-facing project specs**. Nine substantive areas landed:

1. **§4.8 Orchestration via Multi** *(iter 41–42)* — extend Multi in-place with a `multi bench` mode. ~70% of what the bench needs is already in Multi's queue/processor/rate-limit/turn-budget code. No Multi schema change required; bench layers metadata in `evals/` keyed by Multi's `Task.ID`.
2. **§4.9 Nightly cadence** *(iter 46)* — sentinel sweep at 01:27, full Dark sweep at 02:47 (gates on sentinel passing), quarterly TS/Py baseline. Tonight's clock: 20:00 sentinel → 21:00 full → 23:00 deliverable.
3. **§4.10 Tonight's launch checklist** *(iter 49, 52)* — six phases (A–F) with copy-paste commands, expected outputs, 8-row failure-mode table. Caught two bugs before tonight: `multi set-fork` precondition (verified iter 52 from real `queue.json`) + `max_turns: 0` default mismatch.
4. **§6.2 Dashboard + exportable reports** *(iter 45)* — three views (snapshot vs TS/Py · over-time · last-sweep delta), matplotlib + Jinja2 (zero JS, static SVG inline), single-HTML-file output. **scp-shareable.** PDF export via weasyprint.
5. **§6.0 Cost-attribution formula + harness self-health metrics** *(iter 39, 47)* — `pricing.json` per model; 6 self-health diagnostics that always render in reports (never silently green).
6. **Auth model corrected** *(iter 51)* — bench uses host-side **Claude Code subscription auth** (Pro/Max), not metered API key. Real cost is $0 marginal; cap-figures are *API-equivalent dollars* for cross-bench comparability.
7. **5 sentinel projects, all fully specced** *(iter 43, 55–59)* — `password-gen`, `cron-describe`, `markdown-toc`, `validation-applicative`, `parser-combinators`. Each maps to a distinct §6 metric channel (tokens / fix-iter delta / first-parse-success / edit-format / rework). **Tonight's actual target set.**
8. **Spec format definitively defined** *(iter 55, 58)* — YAML frontmatter (8 fields incl. `expected_outcome` enum + `known_blockers` controlled vocabulary), 4-section body for apps, 6-section body for library ports (adds API surface + driver CLI). Materialized inline in `projects.md`; implementer extracts to `evals/projects/<name>/spec.md` per §4.0.
9. **Operational decisions** *(iter 48)* — sweep-lock via `flock`, `--dry-run` mode on every state-mutating subcommand, soft-cap for first 7 sweeps (calibration headroom) then hard-cap.

Plus: ralph-loop integration (iter 50 — agents emit `<phase>DONE</phase>`, bench polls `.claude-task/phase` every 5 s; correction of iter-17's `__HARNESS_DONE__` magic string), library-port format extension (iter 58), README rewrite (iter 54), one compaction tick (iter 53).

## State of the doc set

5 files, ~3000 lines total:

| File | Lines | What's in it |
|---|---|---|
| [`README.md`](README.md) | ~120 | Exec summary, file map, Tonight's clock, 18 strategic decisions w/ anchor links |
| [`plan.md`](plan.md) | ~1500 | Vision, harness design (§4.0–4.10), metrics (§6 — 26 metrics across 5 tiers), phasing (§7 — 5 waves with sub-A/B), risks (§8) |
| [`improvements.md`](improvements.md) | ~270 | 30 §3 items across 6 sub-sections + cross-cutting + doc bugs + runtime gaps |
| [`projects.md`](projects.md) | ~700 | 23 vetted projects + spec-format spec + **5 sentinels with full agent-facing prompts** (~3000 chars each) |
| [`research-log.md`](research-log.md) | ~770 | Append-only iteration history, 60+ entries |

## What's still open

### High-priority before tonight

- **8 more project specs** to materialize per the iter-54 roadmap, in this order (loop ran out of time on these — implementer or follow-up loop iteration handles):
  - 3 Phase-1 projects: `hello-cli`, `csv-to-json`, `url-shortener-cli`
  - 2 library ports: `mvu-runtime`, `pretty-printer`
  - 5 expected-to-fail (Class M): `parallel-downloader`, `jwt-rs256`, `tar-zip-creation`, `realtime-roguelike`, `redis-driver`
  - 4 breadth picks: 1 TUI, 1 daemon, 1 MCP server, 1 LLM-CLI
- **Tonight's wrapper code** — Python harness skeleton (`evals/harness/main.py`, `runner.py`, `metrics.py`, `report.py`). Spec'd in §4.10 Phase A2, ~3 h estimated.
- **`pricing.json`** — populate with current Anthropic rates (per §6.0). Hard requirement; wrapper refuses to start without it.
- **`multi set-fork` precondition** — verified iter 52 as a real gate (your `queue.json` has a task that already failed for this).

### Medium-priority follow-ups

- Subscription quota burn estimation (open since iter 51). Anthropic billing endpoint for Pro/Max isn't well-documented; might need heuristics.
- Auto-dashboard at sweep-end vs manual (iter 49). Probably auto for cron, manual tonight.
- Provider billing endpoint check for §6 #23 pricing-config drift (iter 47).
- Red/yellow threshold logic for the harness-health panel (iter 47).

### Lower-priority strategic

- The MCP server (Phase 4+) — agents-via-Cursor distribution path.
- Port the dashboard generator to Dark itself once `dark publish` exists (iter 45 future-migration milestone).
- The ~10 unspec'd projects of the 23 vetted — fill in compact form post-loop.

## My thoughts on what to do first

1. **Read tonight's clock first** — [plan.md §4.10](plan.md#410-tonights-launch-checklist-concretized-iter-49). It's the single most actionable thing in the doc set.
2. **Then read the 5 sentinel specs** in projects.md. Tonight's launch runs *those* projects, not the catalog at large. If any spec feels off, fix it before 20:00 — they're the source-of-truth the agent will see.
3. **Don't try to write the wrapper from scratch in 3 hours** — copy as much shape as possible from §4.10 Phase A2 + §4.7 prompt template + §4.8's "thin layer over Multi's queue API" pattern. The wrapper is mechanical at this point because the specs are concrete.
4. **Skip TS/Py for tonight.** The §4.9 tonight-scope-reminder is explicit: TS/Py columns can show `--`. Tonight is about validating the Dark pipeline, not cross-language comparison. That ships at the next quarterly baseline.
5. **Soft-cap the first 7 sweeps.** Tonight's expected $0.65 sentinel sweep + $7 full sweep are projections, not guarantees. If one of them spikes 5×, that's data — not a reason to abort. The iter-48 soft-cap-for-first-7 is exactly this case.
6. **The sentinels are the win-condition for tonight.** "Tonight's deliverable" = report.md + dashboard/index.html + 5 rows in `results.jsonl` + harness-health panel green. *Numbers don't have to look good. They have to be real.* (Per §7 Phase 1 definition-of-done.)

Two things I'd flag for "after tonight":

- **Sentinel-as-canary semantics need calibration.** If tonight's `parser-combinators` (the `expected_outcome: stretch` one) fails, that's not a "Dark broken" signal — it's expected. The dashboard should render expected-fails distinctly so a coworker reading the report doesn't conclude "Dark can't do anything."
- **The §3 backlog → A/B-wave protocol is only valuable if the bench is sensitive to small changes.** Wave 1 (prompt-only) is intentionally first as a calibration. If wave 1 doesn't move §6 metrics, the bench is too noisy. **Run a sentinel-only wave-1 A/B as the *first improvement experiment* once the baseline lands.** That's tonight + ~48 hours.

## What I think went well, what didn't

**Went well**:
- Multi turned out to be ~70% of the orchestration work. The fork-or-extend question resolved cleanly into "extend in place."
- Catching the auth correction (iter 51) before code got written — your feedback saved a substantial wrong-direction.
- The spec format extension for library ports (iter 58) — the thin-CLI-driver pattern preserves cross-language fairness without forcing the rubric to import the artifact.
- Sentinels mapping 1:1 to §6 metric channels — clean by design.

**Didn't quite finish at the time of this snapshot, then did**:
- This PROGRESS.md captured loop state at iter 59 (the original 3-hour-budget endpoint). At your prompting (*"keep iterating"*), the loop continued through iter 74 and finished **all 18 planned specs** — see `projects.md` for the full set, organized as 5 sentinels + 3 Phase-1 + 4 library ports + 5 expected-to-fail + 3 additional breadth picks. The "only 5 of 13" framing in this paragraph reflects the iter-59 snapshot; the loop's actual completion is in the research log.
- Subscription quota burn estimation still never specced — that's a real gap when running nightly on a Max plan.
- No survey of Aider/SWE-Lancer's actual *report shapes* — could have informed §6.2 better. Might have saved a wave of report-design churn.

**Trust-but-verify reminders**:
- The cost projection assumed ~$0.13/run from iter-3's Aider data. Your 2026-05 actual costs may differ; the iter-48 soft-cap accounts for this but be ready.
- Multi's queue currently has 3 stale dev tasks from April. Bench's parseable-task-IDs (`bench-<sweep>-...`) keep them separate, but eyeball `multi ls` after tonight's first sweep to confirm.

---

*Generated by the loop at iter 59 (final). Cron `f0c0f677` cancelled.*
