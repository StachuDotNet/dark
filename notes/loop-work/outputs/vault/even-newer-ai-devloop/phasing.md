# Phasing — Phase 0 → Phase 5+

> Extracted from plan.md §7 (iter 118) — the project-phasing plan with the A/B improvement protocol that anchors how Dark improvements get accepted-or-reverted. Cross-references `plan.md` (north-star + harness spec) and `improvements.md` (the wave catalogue Phase 3 cycles through).


### Phase 0 (this doc) — specify what we're building.

### Phase 1 — Skeleton harness, end-to-end (target: 1–2 days)

Goal: collect *one row* of real data on Dark vs TS vs Py for 3 projects, with all the §6 headline metrics populated. **Does not need to be parallel, fast, or pretty.** It needs to produce trustworthy numbers we can iterate on.

**Project subset (chosen to expose maximum harness surface with minimum project work)**:
- `hello-cli` (T) — smoke-tests the whole pipeline end-to-end.
- `csv-to-json` (S) — parsing + JSON, no infra. Catches stdlib-discovery friction.
- `url-shortener-cli` (S) — Dark's persistence-without-setup differentiator. The only Phase 1 project that puts Dark in a winning posture.

Deliberately *skipping* HTTP for Phase 1 — health-polling adds complexity that should land in Phase 2.

**Punch list (files that exist when Phase 1 ships)**:

```
evals/
  projects/hello-cli/        spec.md, rubric.{dark,ts,py}, gold/{dark,ts,py}/
  projects/csv-to-json/      spec.md, rubric.{dark,ts,py}, gold/{dark,ts,py}/
  projects/url-shortener-cli/spec.md, rubric.{dark,ts,py}, gold/{dark,ts,py}/
  harness/
    __init__.py
    main.py                  # `python -m harness sweep|report|verify-rubrics`
    runner.py                # spawns claude-code with `--output-format json`
    metrics.py               # parses transcript + telemetry.jsonl into metrics.json
    report.py                # generates evals/report.md
  bin/
    dark-wrapped             # tee exit code + cmd into cli-invocations.jsonl
  results.jsonl              # one row per run, append-only
  report.md                  # most recent sweep summary
```

**Commands that work**:

```
# Run one project, one language, one attempt
python -m harness single --project hello-cli --language dark

# Full Phase 1 sweep: 3 projects × 3 languages × 1 attempt = 9 runs
python -m harness sweep --projects hello-cli,csv-to-json,url-shortener-cli \
                        --languages dark,ts,py --attempts 1

# Verify a rubric: mutation-test it against its gold reference
python -m harness verify-rubrics --project url-shortener-cli

# Re-generate the report from results.jsonl
python -m harness report <sweep_id>
```

**Phase 1 metric coverage** (matched to §6.0 tiers):

| Tier | Metric | Source for Phase 1 |
|---|---|---|
| Headline | Pass rate at $0.50 budget | rubric runner exit codes; cap enforced by wrapper |
| Headline | Pass rate unbounded | rubric runner exit codes |
| Headline | Fix-iteration delta | **deferred to Phase 2** (Phase 1 is pass@1 only) |
| Headline | Trace adoption rate (Dark only) | count of `traces …` invocations in `cli-invocations.jsonl` |
| Supporting | Median tokens per pass | transcript JSON `usage.{input,output,cache_*}_tokens` |
| Supporting | Dollars-per-pass | tokens × hard-coded current API price |
| Supporting | Median wall time per pass | wrapper `start_ts`/`end_ts` |
| Supporting | Edit-to-first-green | count of `commit` events in telemetry.jsonl (Dark) / count of file-write tool calls in transcript (TS/Py) |
| Supporting | Rework ratio | derived from above |
| Supporting | Artifact-size ratio | `du -sb` per language artifact dir |
| Supporting | Followup-edit cost | **deferred to Phase 2** (needs a follow-up prompt) |
| Diagnostic | First-parse-success attempts (Dark) | count of `dark fn` calls before the first that exits 0 |
| Diagnostic | Constraint-escape attempts | count of agent attempts at `bash`/`sh`/etc. (wrapper rejects + counts) |
| Diagnostic | Edit-format compliance | count of agent turns producing syntactically valid output per language |

**Decision points settled inline (so Phase 1 doesn't get blocked on bikeshedding)**:

- **How to invoke the agent**: shell out to `claude` CLI with `--output-format json` (not the SDK). Simplest possible. Switch later if we need streaming.
- **Concurrency**: none. Sequential runs. Concurrency is Phase 2.
- **Telemetry isolation**: per-run rundir (§4.2 option A). Each run sets `DARK_RUNDIR=evals/runs/<sweep_id>/<run_id>/`.
- **Prompt caching**: **off** for Phase 1 baseline. We want honest first-pass numbers. Caching-on becomes a separate sweep mode in Phase 2.
- **Mutation-testing rubrics**: not gating in Phase 1 (`verify-rubrics` exists but isn't blocking). Gate in Phase 2.
- **Constraint mode**: agent gets *only* the language-specific tooling exposed (no fallback to bash). For Dark: `dark-wrapped` is the only exposed binary; for TS: only `node`/`npm`; for Py: only `python`/`uv`. We'll measure how often agents try to escape and treat the count as a §6 metric in Phase 2.

**Time budget**:

| Half-day | Work |
|---|---|
| Day 1 AM | Write 3 `spec.md` + 9 gold references (3 projects × 3 langs). Two are trivial; budget ~30 min each. |
| Day 1 PM | Write 9 rubrics (3 specs × 3 langs). Budget ~20 min each. |
| Day 2 AM | Build the Python harness skeleton (`main.py` + `runner.py` + `metrics.py`). |
| Day 2 PM | First end-to-end sweep + report. Iterate on flakiness. Land Phase 1. |

**Definition of done**: `python -m harness sweep …` runs to completion, produces `evals/results.jsonl` with 9 rows, and `python -m harness report <sweep_id>` writes a markdown report including all 9 of the populated §6 metrics. The numbers don't have to look good — they have to be real.

### Phase 2 — Scale to 30 projects (target: 1 week)

- Parallelism (configurable -j flag; default `nproc / 2`).
- Pass@2-with-feedback attempt model (§4.6.1).
- Mutation-testing rubrics becomes a hard gate before a project enters the bench.
- HTTP projects join the bench (`http-healthz`, `webhook-echo`).
- Report generator hardens: comparison vs prior sweep, regression callouts.
- Followup-edit cost (§6 #11) populated.

### Phase 3 — First improvement wave (the A/B protocol)

The point of Phase 3 is to convert "we shipped a Dark improvement" into "we have data showing the improvement helped." Without a protocol, this slides into vibes. With one, every improvement either survives the bench or gets reverted.

**The user's framing**: *"all the dark work should probably be in a branch and whether or not I merge the work in progress or not is the determination will make later."* The protocol below makes that determination data-driven.

#### Protocol

1. **Pick a hypothesis.** One [improvements.md](improvements.md) item per improvement wave. Likeliest first: §3.2 #1 (`dark edit`) or §3.2 #2 (auto-diagnostics-after-write) — both CLI-surface only, no language change.
2. **Branch.** `git checkout -b improve/dark-edit` off `main`. The improvement lands here, not on `main`.
3. **Baseline sweep** (already in hand from Phase 2 — re-use the most recent `main` sweep_id within the last 14 days; otherwise re-run on `main`).
4. **Candidate sweep.** Bench grows a `--dark-revision <git-rev>` flag. The wrapper checks out the branch into a Dark worktree, rebuilds the CLI binary, and points the harness at it.
   - **Concretely**: `python -m harness sweep --dark-revision improve/dark-edit --projects all --languages dark`. Note: only re-run *Dark* projects when only Dark changed; TS/Py runs are unaffected, so re-using their baseline rows is honest and saves cost. Wrapper enforces "TS/Py results carry forward only if `dark-revision` is the only change."
5. **Compare.** `python -m harness ab <baseline_sweep_id> <candidate_sweep_id>` produces a markdown report:
   - Each Headline metric (§6.0): baseline value, candidate value, absolute delta, signed delta, p-value (paired bootstrap over project rows).
   - Per-tier and per-project breakdowns.
   - Regression callouts: any project that *regressed* by > 1 std deviation gets a flag, even if the aggregate moved the right way.
6. **Acceptance criterion** (proposed, tune in Phase 3 itself): improvement merges to `main` if at least 2 of the 4 §6.0 Headline metrics moved positively by > 1 std deviation **and** none regressed by > 1 std deviation. If only the *Dark-targeted* metrics moved (e.g. trace adoption rose but pass-rate didn't), that's a partial win — keep the branch open, hypothesize a follow-up, don't merge yet.
7. **If accepted**: merge, write a short retro into `evals/improvements/<branch-name>.md` (what changed, what moved, what didn't) — append-only history of every wave.
8. **If rejected**: don't merge. Branch stays alive for a possible re-attempt. Retro still gets written (negative results are still data).

#### Phase 3 deliverables (concrete files / commands)

- `python -m harness sweep --dark-revision <ref>` (new flag).
- `python -m harness ab <baseline> <candidate>` (new subcommand).
- `evals/improvements/<branch-name>.md` (one per wave).
- `evals/baselines.json` — pinned `(dark_sha, sweep_id)` pairs we accept as "current `main` baseline" without re-running. Refreshed when `main` moves.

#### What Phase 3 explicitly does NOT do

- **No MCP server.** The §2.1 tension between "constrain agent to Dark CLI only" (Phase 1–3 stance) and "MCP for ecosystem reach" (Phase 4+) is resolved here: **MCP is a Phase 4+ deliverable, not part of the eval bench's improvement waves.** Reason: the bench is measuring whether Dark can be the agent's *primary* platform. Mixing in MCP confounds the experiment — a Cursor-via-MCP win is not the same product as a Claude-Code-via-CLI win. They're complementary distribution paths but different experiments. Phase 4 ships the MCP server as ecosystem-reach work, separately from the bench.
- **No language/runtime changes** in the first 2 improvement waves. Every change in the first 2 waves is CLI-surface or agent-prompt-template only. Reason: keep the cycle time tight; language-runtime changes are slower to ship and slower to revert if the bench rejects them.
- **No Dark improvements that aren't on the [improvements.md](improvements.md) backlog.** Discipline. Speculative-feature improvements without an item don't run through the protocol.

#### Wave queue — proposed ordering for the first 5 waves (decided iter 22)

Once the [improvements.md](improvements.md) backlog has ~25 enumerated items, a queue is more useful than a backlog. Each wave below is a *coherent bundle* — items that share a Dark-code surface or all live in the prompt template, so one A/B sweep tests a single hypothesis. Ordered by ship-cost first (cheapest wave runs first), with intent that early waves *also* validate the harness's sensitivity to small changes before we commit expensive engineering.

| # | Wave name | Bundle | Cost | Hypothesis (which §6 metric) |
|---|---|---|---|---|
| 1 | **Prompt-only bundle** | CLAUDE.md template (§3.1 #1) · agent-generated SUMMARY.md (§3.6 #3) · surface `merge --dry-run` + `rebase --status` (§3.4 #4) · trace tip in `retry.md` (§4.7 / §3.3 promo) | **Zero Dark code.** Just `evals/harness/prompts/*.md` edits. | Trace adoption rate (§6 #4) ↑, fix-iter delta (§6 #3) ↑, median tokens (§6 #5) flat-or-slightly-up (SUMMARY.md adds tokens). **Also a harness sanity check**: if the bench can't detect this, it's broken. |
| 2 | **`--json` rollout** | All 9 missing flags from §3.1 #6 audit · fix `builtins --json` coercion bug | Single shared formatter; one Dark PR. Cheap CLI-surface only. | Median tokens (§6 #5) ↓ (parseable output is denser); constraint-escape attempts (§6 #13) ↓ (less shelling out to grep/awk). |
| 3 | **Authoring headliners** | `dark edit` (§3.2 #1) · auto-emit diagnostics after write (§3.2 #2) | Bundle: edit produces diagnostics. Medium-cost CLI-surface. | Median tokens (§6 #5) **big drop** on M/L tier; rework ratio (§6 #9) ↓; edit-to-first-green (§6 #8) ↓. The biggest predicted token-impact wave. |
| 4 | **Error-UX bundle** | Parse-error suggestions (§3.2 #4) · "did you mean" on miss (§3.1 #3) · auto-attach trace on fail (§3.3 #1) | Three error paths, one shared "make errors helpful" theme. CLI-surface. | First-parse-success attempts (§6 #12) ↓ materially; rework ratio (§6 #9) ↓; fix-iter delta (§6 #3) ↑. |
| 5 | **`dark publish` MVP** | §3.5 #1 (publish to a directory; `--single-file` Phase 4) · §3.5 #4 (`dark export`/`import` lightweight) | Largest of the early waves. Touches packaging + dependency closure. | Enables a new metric: "time to friend-runnable artifact." User-visible (§1 promise). Won't move §6 headlines directly, but unlocks the headline §1 narrative. |

**What's NOT in the first 5 waves** (deliberately deferred):
- `dark rename` (§3.4 #1) — graph-rewrite is expensive; defer until rename-heavy projects show up in the bench (not in the Phase 1 starter set).
- `dark suggest <NL>` (§3.1 #4) — full-text-over-docs MVP is feasible, but the embeddings stretch wants `Stdlib.LLM` working in CLI (open Q since iter 13). Defer to wave 6+.
- `dark uncommit` / `dark revert` (§3.4 #2) — hits the SCM machinery; expensive to get right; defer until bench confirms it's a real friction.
- `dark review` + `dark review-mark` (§3.6 #1, #5) — high-value for human reviewers, but doesn't move §6 *agent* metrics. Phase 4 candidate.

**Wave-1-first rationale.** Putting the prompt-only bundle first does double duty: ship cost is near-zero, *and* if the §6 metrics don't move on a prompt change the bench is too noisy to drive Dark-code investment. Cheap insurance against running expensive waves on a broken bench. (Aider's harness applies the same trick — they run a prompt-only delta as a calibration row.)

**Acceptance**: each wave runs the §7 Phase 3 protocol independently. If a wave fails its acceptance criterion, the next wave doesn't automatically start — re-baseline first.

#### Wave 1 sub-protocol: isolate which prompt change moves the metric (decided iter 37)

Wave 1 bundles 4 prompt-only sub-changes (per the wave queue table above). Running them as one composite A/B answers "did *something* move?" but not "*which something*?" When the budget allows (and prompt-only is *cheap*), a sub-A/B isolates each contribution.

**Configuration**: 5 prompt variants run as a sub-sweep. Each variant is one row in `evals/results.jsonl` tagged `wave: 1, variant: <name>`.

| Variant | CLAUDE.md template | SUMMARY.md ask | merge-tip | trace-tip in retry.md | Hypothesis |
|---|---|---|---|---|---|
| `baseline` | — | — | — | — | The pre-Phase-3 numbers (carry forward from Phase 2 if available) |
| `clauded-md` | ✓ | — | — | — | #17 time-to-first-fn ↓ alone |
| `summary` | — | ✓ | — | — | Tokens-per-pass ↑ slightly (SUMMARY.md adds tokens) but human-review-time would drop (un-tracked metric) |
| `merge-tip` | — | — | ✓ | — | Trace adoption rate (#4) untouched; expect *no movement* — this variant is the null-hypothesis test |
| `trace-tip` | — | — | — | ✓ | Trace adoption rate (#4) ↑, fix-iteration delta (#3) ↑ |
| `all` | ✓ | ✓ | ✓ | ✓ | Sum of the above (worth checking that the effects compose) |

**Cost**: 6 variants × 23 vetted projects × Dark-only (no TS/Py since this is Dark-prompt) × pass@1 = ~138 runs. At ~$0.13/run (Aider-comparable from §4.6.1), ~$18 per wave-1 sub-sweep. **Cheap**.

**Reading the results**:

- If `merge-tip` moves any §6 metric materially, something's wrong with our hypothesis (it's there for `dark merge` confidence, not for Discovery / Authoring metrics). Investigate.
- If `clauded-md` doesn't move #17 (time-to-first-fn) when the §3.1 #1 hypothesis says it should, the CLAUDE.md content needs revision *before* declaring wave 1 done.
- If `all` ≠ sum of individual movements, there's interaction between the changes. Prompt-template-only waves rarely have non-linear interactions, but record the finding.
- Pick the variants whose effect is positive enough to keep, drop the rest.

**Carry-over to wave 1 final**: only the variants with verified positive effect get bundled into the merged prompt template. Negative or null variants stay out. This is *better* than shipping all 4 changes wholesale and hoping.

**Why we don't sub-A/B every wave**: most other waves change Dark code, where sub-A/B'ing is expensive (each variant requires a separate `dark_sha`). Wave 1 is uniquely cheap because it's prompt-only.

### Phase 4 — 100 projects, sweep cadence + ecosystem reach

- Decide cadence based on Phase 3 cost data (likely weekly + per-PR for Dark improvements that touch the [improvements.md](improvements.md) surface).
- Public-facing report at this point — a leaderboard page if we want one.
- **MCP server** ships here, separately from the bench (per Phase 3 boundary above). Goal: Dark composable from Cursor / Claude Code as a tool, alongside the standalone-CLI experience the bench measures. Different product surface, different KPIs (adoption, retention) — not on the §6 dashboard.

### Phase 5+ — Iterate.

