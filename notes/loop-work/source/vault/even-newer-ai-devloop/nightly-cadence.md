# Nightly cadence

> Extracted from plan.md §4.9 (iter 118) — operational spec for the cron-fired nightly core sweep, with the concurrency policy, dependency on prior night's sweep, sweep_id naming, and failure handling. Cross-references `multi-orchestration.md` (queue handoff), `launch-checklist.md` (tonight's kickoff), and `dashboard-spec.md` (where the per-sweep numbers land).

The bench runs on a recurring schedule, not just on demand. Tonight kicks off the cadence; subsequent nights become routine.

#### Schedule

| Sweep type | Cadence | Cron | Why this minute |
|---|---|---|---|
| **Core-only** (Dark, 5 projects, ~10 min) | Every night | `27 1 * * *` (01:27 local) | Off-the-zero-minute (§iter-9 cadence-jitter principle); coincides with low API-load window |
| **Full Dark sweep** (23 projects × pass@2, ~1.5 h) | Every night | `47 2 * * *` (02:47 local) | 80 min after core ends; core must succeed before full launches |
| **Cross-language baseline** (TS+Py × 23 projects × pass@1, ~1 h) | Quarterly + on-demand | `33 4 1 1,4,7,10 *` (Jan/Apr/Jul/Oct 1st at 04:33) | TS/Py results are cached per §4.3.1; only re-run when the dependency snapshot or runtime pin changes |
| **Core-on-PR** (Phase 4+) | On every Dark PR | webhook | Per-PR signal, fast feedback for §3 improvements; defer until Phase 4 |

The core sweep runs **before** the full sweep so the day's "is the harness even working?" signal lands first. If core fails (any startup flake, any auth error), the full sweep is **skipped** that night — no point burning $20 of API tokens on a broken pipeline.

#### Tonight specifically — first run

Tonight is **not** the first nightly cron firing — it's a manual kickoff to validate the pipeline. Plan:

1. **20:00 local** (or whenever ready): `python -m harness sweep --core-only --languages dark`. Core-only Dark sweep, ~10 min wall, ~$0.65 budget cap.
2. **20:15 local**: `python -m harness report <sweep_id>` + `python -m harness dashboard`. Per-sweep report.md + dashboard/index.html generated.
3. **20:25 local**: Eyeball the outputs. Iterate on report shape if rough.
4. **21:00 local**: First *full* sweep — `python -m harness sweep --languages dark,ts,py`. ~$18 budget cap. ~2 h wall.
5. **23:00 local**: Final report + dashboard regeneration. Tonight's deliverable lands.
6. **01:27 next morning**: First scheduled core sweep fires from cron. Confirms the cadence works.

Tomorrow morning, the user wakes up with: tonight's full-sweep dashboard + a fresh core sweep from cron.

#### Cost cap per sweep

Hard cap as a per-sweep configuration, enforced by the wrapper before each Multi-task is enqueued:

| Sweep type | Cap | Rationale |
|---|---|---|
| Core-only | $1.50 (API-equivalent) | 5 projects × $0.30 / task headroom |
| Full Dark sweep (pass@1) | $7 (API-equivalent) | 23 projects × $0.30 |
| Full Dark sweep (pass@2) | $14 (API-equivalent) | Doubles for the retry-with-feedback attempt |
| Cross-language baseline | $20 (API-equivalent) | TS+Py adds 46 runs (23 × 2 langs × pass@1) |
| Phase 3 A/B wave | $14 baseline + $14 candidate = $28 max | Only Dark re-runs (per §7 Phase 3); TS/Py carry forward |

**Note (iter 51)**: caps are in *API-equivalent dollars* — see §4.7's cost-tracking-without-API-billing subsection. The bench runs on Claude Code subscription auth, so actual marginal cost is $0. The cap is enforcing a *quality-and-quota proxy*, not real-money spend on tonight's run.

#### Cut-off policy — stuck projects, especially `fail-likely` (added iter 87, round-2 P0 #9)

A `fail-likely` project (per iter 65 `expected-to-fail` framework) might never converge. The agent could spin forever in a hopeless implementation. Without a cut-off, one stuck project drains the whole sweep budget.

The cut-off is a **per-attempt** threshold combining two signals — whichever fires first triggers abandonment:

1. **Turn cap**: 50 turns (matches §4.7 reproducibility setting). Multi's queue tracks this via `MaxTurns` and writes `phase: budget-hit` when exceeded. Agent gets a chance to wrap up; doesn't get a fresh blank slate.
2. **Wall-time cap**: 15 minutes per attempt (median attempt is ~7 min per §4.10, so 15 min = 2× the typical). Wrapper polls `started_at` vs `now()`; if exceeded and Multi hasn't transitioned to a terminal phase, wrapper sends SIGTERM, then SIGKILL after 30s grace, marks `phase: timeout`.

**On abandonment**, the wrapper records:

- `outcome: cutoff-fail` (a new outcome distinct from rubric-fail / harness-flake / agent-abandoned)
- `cutoff_reason: budget-hit | timeout | manual-cancel`
- `last_known_phase: <whatever ralph wrote last>`
- The agent's partial `SUMMARY.md` (if it managed to write one) — even mid-stream reflections are valuable
- The agent's last 100 lines of transcript — captures what the agent was doing when the cut-off fired

**For `fail-likely` projects**: a cut-off is *expected behaviour*, not noise. The dashboard renders cutoff-fail on a `fail-likely` project as the *expected* dim-grey. Cutoff on an `expected_outcome: pass` project is louder — surfaces a regression *or* an unexpected agent loop.

**Block-list rule**: if the same project hits cut-off in 3 consecutive sweeps with the same `cutoff_reason`, the wrapper auto-promotes it from `fail-likely` to `fail-known` and *stops running it on future sweeps* (still counted in the catalog, just skipped). Override with `--include-fail-known` flag if the user wants to re-test (e.g. after shipping a fix that should unblock the project).

**Tonight's-kickoff override (iter 85, round-2 P0 #3)**: **drop the cost cap entirely for tonight.** Pace by concurrency only (`-j nproc/2` ≈ 4-way parallel) — that's the throttle. The iter-46/48 soft-cap-for-first-7-sweeps model assumed nightly-cron firings; the user's actual usage is manually-triggered every-day-or-two Dark runs + weekly cross-language baselines. With those cadences, calibrating per-sweep medians on a "first 7" basis is the wrong shape. Instead:

1. Tonight has no cap — let it spend whatever the agents need.
2. Wrapper computes per-project / per-language median costs from tonight's data.
3. Subsequent runs use those medians × 2 as a *soft* cap (warns but doesn't kill).
4. Hard cap only kicks in if the user explicitly requests `--cap-mode hard $X`.

Concurrency stays the throttle: at 4-way parallel × ~7-10 min/run × 50 attempts ≈ 2.5h total. If the user wakes up at 07:00 and the budget was naturally bounded by wall time, that's the right shape. **Cost-as-quality-signal** stays — every dashboard cell still shows API-equivalent dollars — but cost-as-blocker comes off for calibration.

The wrapper aborts the sweep when the running total crosses the cap. **Caveat**: Multi's existing rate-limit / turn-budget logic doesn't know about the bench's *cost* cap (it knows turn count, not dollars). The wrapper computes cost from per-run `usage` blocks (per §6.0 cost-attribution formula) and feeds the running total into `multi bench status`. If the cap trips mid-sweep, in-flight tasks finish but no new ones launch. **Pre-flight estimate**: before launch, the wrapper estimates cost based on prior-sweep medians; if the estimate exceeds the cap by >20%, it warns and asks for confirmation.

#### Failure modes — what happens if a sweep fails partway

- **Rate-limit hit on N tasks**: Multi auto-resumes per its existing logic (5-min backoff). Wrapper extends the sweep deadline by `5min × <count of rate-limited tasks>`.
- **One task fails entirely** (agent abandonment, container crash): logged as `agent_abandoned: true` + `harness_flake: false` in metrics; sweep continues.
- **Container fails to start** (Dark only): logged as `harness_flake: true` (per iter-26 mitigation in §4.4.1); sweep continues, marked for retry next night.
- **Cost cap tripped**: in-flight tasks finish, sweep marked partial-completion in `manifest.json`. Dashboard renders what's there with a partial banner.
- **Pricing config missing** (`pricing.json` absent or stale): wrapper refuses to start. Hard requirement — without prices, the cost cap is meaningless. (Note iter 51: pricing.json is for *API-equivalent* tracking even when the bench runs on subscription auth — the report still wants comparable dollar figures.)
- **Subscription quota exhausted** (added iter 51): Claude Code Pro/Max has message-volume limits. If the bench's runs have eaten through the user's monthly quota, the agent's `claude` invocations will start hitting `usage_policy_violation` or rate-limit-style errors that are *not* token-rate-limits. Mitigation: track approximate quota burn per sweep; warn if the bench has consumed > 30% of a monthly quota in a single night. §8 risk added.
- **Multi crashes mid-sweep**: Multi's queue.json is persistent (per iter-41 verification); restart resumes from last known state.
- **Wrapper crashes mid-sweep**: per-run dirs already on disk; restart wrapper, it resumes from `manifest.json`. Idempotent on re-run.

#### TS/Py caching policy (closes iter-45 open Q)

Caching the TS/Py baseline rows is **always on** by default; the snapshot view shows them with a `(cached from <sweep_id>)` annotation per project.

Cache invalidation triggers (any of these forces a re-run):
- A project's `spec.md` changes (its hash differs from the cached run's hash)
- `pricing.json` changes (numbers shift; need to recompute cost-per-pass)
- `evals/runtime-snapshots/{node,python}-versions.json` changes (per §4.3.1)
- `prompt_template_hash` changes (different `task.md` etc.)
- More than 90 days have passed since the cached run

Per the §4.3.1 quarterly snapshot cadence, the 90-day cap aligns with the snapshot refresh — TS/Py runs once per quarter at minimum. If anything else changes earlier, cache invalidates earlier.

Implementation: cache lookup before enqueuing each TS/Py task. Pseudocode:

```
for project in projects:
  for lang in [ts, py]:
    cached_row = lookup_cached_baseline(project, lang)
    if cached_row and not invalidated(cached_row):
      copy_cached_row_to_current_sweep()
    else:
      enqueue_task(project, lang)
```

This avoids re-running TS/Py for every nightly Dark sweep. Net effect: tonight's full sweep runs 23 Dark + 23 TS + 23 Py = 69 tasks; subsequent nightly Dark sweeps run only 23 Dark tasks (TS/Py rows carry forward).

#### Cost projection — annualized

| Cadence | Per-sweep | Per-month | Per-year |
|---|---|---|---|
| Nightly core | $0.65 | ~$20 | ~$240 |
| Nightly full Dark | $7 | ~$210 | ~$2,520 |
| Quarterly TS/Py | $20 | $7 amortized | $80 |
| **Total (steady state)** | | **~$237/mo** | **~$2,840/yr** |

Manageable for a research bench. Phase 4+ Phase 3 A/B waves add ~$28/wave; ~10 waves/year ≈ +$280/year.

#### Tonight's scope reminder

Tonight is one **core-only Dark sweep** (~$0.65, ~10 min) followed by *optionally* one **full Dark sweep** (~$7, ~1.5 h). TS/Py baselines are *not* required for tonight — the snapshot view will show TS/Py columns as `--` (unrun) until the next quarterly baseline runs. The dashboard renders cleanly with that absence; the user can ask the implementer to run TS/Py manually later if desired.

#### Operational decisions (decided iter 48 — closes iter-46 open Qs)

Three small operational decisions that affect tonight's launch path, packaged together:

**1. Sweep-lock: yes, file-based, at `evals/bench/.sweep-running`.**

Why: a manual `python -m harness sweep` at 21:00 and the cron's 01:27 core could overlap if the manual one runs long. Multi's queue is process-singleton; two wrappers fighting it would corrupt task state.

Mechanism:
- Wrapper acquires the lock at start: `flock evals/bench/.sweep-running` (Linux `flock(2)`); writes `{pid: N, sweep_id: ..., started_at: ISO}` to the file.
- If acquisition fails, wrapper logs `another sweep is running (sweep_id=X, pid=Y, started Zmin ago)` and exits 1 — does *not* queue tasks.
- Lock released on graceful exit (`flock` releases on close).
- Stale lock cleanup: if `pid` in lock file is dead, the next wrapper takes over with a `WARNING: cleaned stale lock from pid=Y` log line.
- Tracked as harness-health metric §6 #25 (sweep-lock contention count).

For tonight: file doesn't exist yet; first manual run creates it. Cron firing at 01:27 will respect the lock if the manual full sweep hasn't finished by then.

**2. `--dry-run` mode: yes, on every wrapper subcommand that mutates state.**

Why: tonight's first launch wants pre-flight visibility — *what would this enqueue?* — without spending. Ongoing operational use too: someone investigating "what's the next sweep going to do?" shouldn't have to run it.

Specific behaviors:
- `python -m harness sweep --dry-run` resolves project specs into the would-be Multi tasks; prints the list (id, language, priority, expected cost) but never calls `multi new <branch>` or queues anything.
- `python -m harness retention --dry-run` lists what would tarball / what would be flagged for deletion under §4.5.1, but doesn't tar or rm.
- `python -m harness ab <baseline> <candidate> --dry-run` reports whether the comparison can run (both sweeps exist, metrics align) without actually generating the report.
- `--dry-run` returns exit 0 if the operation would succeed, exit 2 if it would fail (e.g. baseline doesn't exist), exit 1 on the wrapper's own bug.

Pre-flight cost estimate (per §4.9 cost-cap pre-flight): always uses `--dry-run` semantics internally. The `--dry-run` flag just lets a human see the same thing.

**3. Cost cap: soft-warn for the first 7 sweeps, hard-stop after.**

Why: tonight's first sweep is calibration — we don't yet know the median cost-per-run on this machine, with this Anthropic key, against this Dark version. Hitting a hard cap on calibration would forfeit data we need to set the cap properly.

Behavior:
- First 7 sweep (core-only or full) per `pricing.json` epoch (i.e. since the last pricing.json change): wrapper logs `WARNING: cost cap exceeded by $X (soft mode, sweep continues)` and continues.
- Sweep 8 onwards: hard cap. Wrapper aborts in-flight tasks at the cap; partial results in `manifest.json`.
- "First 7" tracked in `evals/bench/.cap-mode-soft-count` (a small file holding an int). Reset on `pricing.json` change.
- `--cap-mode hard` flag forces hard behavior even before sweep 8 (for safety-critical sweeps).
- `--cap-mode soft` flag forces soft behavior (for re-calibration sweeps after a major change).

For tonight specifically: tonight is sweep 1 of 7. Soft mode active. If tonight's costs are wildly off-projection (say >2× the iter-46 estimate), we know the projection was wrong — adjust §4.9 caps and `pricing.json`.

These three decisions together mean: tonight's manual launch can pre-flight via `--dry-run`, can run without fear of cron-overlap (`flock`-protected), and can soft-warn through cost surprises while we're still calibrating. **All three are wrapper-level** — no Multi extension required. Land in the Python harness.

