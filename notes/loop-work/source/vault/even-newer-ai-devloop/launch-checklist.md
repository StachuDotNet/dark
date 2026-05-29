# Tonight's launch checklist

> Extracted from plan.md §4.10 (iter 111) — this is the implementer's operational runbook for the kickoff sweep, distinct from the spec content (§4.8/§4.9/§6.2). Bridges spec → execution.

The bridge between specs (§4.8/§4.9/§6.2) and execution. This is the implementer's actual to-do list for tonight.

#### Phase A — Before 20:00 (setup, ~3 hours)

**A1. Project specs + rubrics + gold references** for the 5 core projects (§projects.md). For each core:

```
evals/bench/projects/<core-name>/
  spec.md                       # frontmatter (title, tier, modules, languages); body has Description + Behaviours + Smoke commands
  rubric.{dark,ts,py,go,rust}   # rubric per language, exits 0 on pass / non-0 on fail
  # No gold/ dir committed (round-2 P0 #5):
  # Per-language gold references are ephemeral, built fresh per sweep and purged after.
  # Dark gold = a tagged git branch `bench-gold-<project>`. TS/Py/Go/Rust gold = sweeps/<id>/gold-cache/<lang>/<project>/.
```

Time budget: **~30 min/core × 5 = ~2.5 h**. Trivial-tier `password-gen` is fastest; M-tier `parser-combinators` is slowest. **Gold references for tonight are optional** — the bench can run without them and just compare the agent's outputs across languages directly. Add gold per-quarter when re-baselining.

**A2. Python harness skeleton** under `evals/harness/`:

```
evals/harness/
  __init__.py
  main.py                       # entry point: `python -m harness <subcommand>`
  runner.py                     # spawns claude-code with --output-format json; manages sweep-lock; cap accounting
  metrics.py                    # parses transcript + telemetry.jsonl into metrics.json; ANSI-strip per iter-44
  report.py                     # Jinja2 + matplotlib; renders report.md, report.html, dashboard/index.html
  prompts/
    system.dark.md              # per §4.7
    task.md                     # Jinja template
    retry.md                    # Jinja template (deferred — pass@2 is Phase 2)
```

Time budget: **~3 h** for a working skeleton. Iter 45's matplotlib + Jinja choice keeps this minimal — no JS, no DB, no service.

**A3. Configs**:

```
evals/bench/pricing.json        # per §6.0 cost-attribution (Opus 4.7, Sonnet 4.6, Haiku 4.5)
```

Pre-populate with current Anthropic rates. Wrapper refuses to start without this (§4.9 hard requirement).

**A4. Verify Multi + Claude Code are running**:

```
multi --version                 # smoke check
multi ls                        # list any existing branches
ps -ef | grep multi             # is the queue processor running?
claude --version                # Claude Code CLI present?
claude --print "ping"           # tests OAuth subscription auth (NO ANTHROPIC_API_KEY usage)

# A4b — fork check (added iter 52, surfaced from inspecting actual queue.json)
cat ~/.config/dark-multi/github-fork                  # should print git@github.com:<user>/dark.git
# If empty/wrong: multi set-fork git@github.com:<user>/dark.git
```

If Multi isn't running, start it: `multi` (TUI) or just verify via `multi proxy status`. The bench wrapper drives Multi's CLI; it doesn't need the TUI to be foreground.

**Auth note (corrected iter 51)**: the bench uses Claude Code's *subscription* auth (host-side OAuth), not `ANTHROPIC_API_KEY`. If `claude --print "ping"` returns successfully *without* an env-var key set, you're good. If it prompts for login, run `claude` interactively once to OAuth-authenticate, then re-test. Don't `export ANTHROPIC_API_KEY=...` for the bench — it'd switch to metered API billing instead of subscription.

**Fork-config precondition** *(new iter 52, real-state finding)*: inspecting `~/.config/dark-multi/queue.json` on this host showed an existing task with `error: "failed to create branch: GitHub fork not configured. Run: multi set-fork ..."`. Multi's branch-creation requires a configured fork. **The bench's full Dark sweep creates a new branch per run** (per iter-42's per-instance port-mapping rationale + per-run sandboxing). Without `set-fork`, every Dark task would fail at branch creation. Core-only sweep can dodge this if it reuses an existing branch — but that loses isolation. Better to fix the precondition once.

**Existing-tasks-coexistence note** *(iter 52)*: the same queue.json had 3 leftover dev tasks (`help`, `help-remount-ssk`, `test-task`) from April, all in `waiting` or `needs-prompt` states. The bench wrapper should:
- Never delete or modify existing tasks (they're the user's dev work).
- Use parseable task IDs prefixed `bench-<sweep_id>-...` so `multi bench status` can filter to bench-only tasks (per iter 41).
- Not be alarmed by old waiting tasks in the queue — they're dev backlog, not bench failures.

#### Phase B — 20:00–20:25 (core-only sweep)

```bash
# B1. Pre-flight (no spend, no enqueue)
python -m harness sweep --core-only --languages dark --dry-run
# Expected: prints 5 task ids (bench-2026-05-05-2000-{password-gen,cron-describe,markdown-toc,validation-applicative,parser-combinators}-dark-attempt1), expected cost ~$0.65

# B2. Real core sweep
python -m harness sweep --core-only --languages dark --sweep-id core-2026-05-05-2000
# Expected wall: ~10 min (4-way parallelism in Multi). Watch via `multi bench status`.
# Wrapper acquires evals/bench/.sweep-running flock.

# B3. While running, tail the wrapper log
tail -f evals/bench/sweeps/core-2026-05-05-2000/wrapper.log

# B4. Sweep ends — verify it landed
ls evals/bench/sweeps/core-2026-05-05-2000/runs/    # 5 run dirs
cat evals/bench/sweeps/core-2026-05-05-2000/results.jsonl.partial | wc -l   # 5 rows
```

#### Phase C — 20:25 (eyeball + iterate)

```bash
# C1. Generate the per-sweep report
python -m harness report core-2026-05-05-2000 --format md
cat evals/bench/sweeps/core-2026-05-05-2000/report.md

# C2. Generate the dashboard (single sweep, view #2 over-time will be sparse)
python -m harness dashboard
xdg-open evals/bench/dashboard/index.html   # or scp to your laptop
```

Eyeball: do the per-project rows look right? Are costs in the expected range? Is the harness-health panel green? Iterate on `report.py` if shapes are rough.

**Rough is fine** — the user said *"numbers don't have to look good — they have to be real"* (per §7 Phase 1 definition-of-done). Spending >30 min on report polish at this stage is overcooking.

#### Phase D — 21:00 (full Dark sweep, optional)

If Phase C looked clean, kick off a full sweep:

```bash
python -m harness sweep --languages dark --sweep-id dark-2026-05-05-2100
# ~1.5 h wall; ~$7 budget. Watch `multi bench status`.
```

If Phase C had issues, **fix the wrapper, re-run core, defer the full sweep to tomorrow night.** Don't spend $7 on a broken pipeline.

#### Phase E — 23:00 (the deliverable)

```bash
python -m harness report dark-2026-05-05-2100 --format both
python -m harness dashboard

# Tonight's shareable artifact:
ls -la evals/bench/sweeps/dark-2026-05-05-2100/report.md
ls -la evals/bench/dashboard/index.html
```

Send to coworker:

```bash
# Markdown for Slack/email
cat evals/bench/sweeps/dark-2026-05-05-2100/report.md | xclip -selection clipboard

# HTML for browser viewing
scp evals/bench/dashboard/index.html coworker.example.com:public_html/dark-bench-2026-05-05.html

# PDF if that's the preference
python -m harness export dark-2026-05-05-2100 --pdf
# Output: evals/bench/sweeps/dark-2026-05-05-2100/report.pdf
```

#### Phase F — 01:27 next morning (cron picks up)

The first cron firing should be visible in `evals/bench/sweeps/core-2026-05-05-2127/`. Tomorrow morning check that the sweep landed, no flock contention, harness-health panel green. **Confirms cadence.**

#### Tonight-specific failure modes

What's likely to go wrong, and the recovery:

| Symptom | Likely cause | Recovery |
|---|---|---|
| `flock` says lock held | A previous wrapper is hung | `cat evals/bench/.sweep-running` → check pid; if dead, `rm` the file |
| Multi has no `bench` subcommand | Tonight's wrapper doesn't yet need it (§4.8 tonight's-anchor: drive `multi`'s existing CLI directly) | Confirm wrapper is using `multi new <branch>` not `multi bench enqueue` |
| `failed to create branch: GitHub fork not configured` | Phase A4b skipped | `multi set-fork git@github.com:<user>/dark.git`; verify `~/.config/dark-multi/github-fork` is non-empty |
| Old waiting tasks in `multi ls` | Dev backlog from prior work — **not** bench failures (verified iter 52: ~3 leftover dev tasks pre-existed in queue.json) | Ignore tasks not prefixed `bench-`; the bench's parseable-IDs (iter 41) keep them separated |
| `pricing.json` missing | Forgot phase A3 | Copy the seed in §6.0; the wrapper refuses to start without it |
| Core sweep $0.65 actual vs $5 reported | `pricing.json` has wrong rates (off by 10× on output, common error) | Compare to Anthropic console; fix `pricing.json`; re-baseline |
| Dashboard renders empty charts | matplotlib not installed or no `data.json` | `pip install matplotlib jinja2 weasyprint`; check `evals/bench/dashboard/data.json` was generated |
| Coworker can't open the HTML | Browsers block file:// SVG embeds | Serve locally: `python -m http.server 8000 -d evals/bench/dashboard/` then share `http://<host>:8000/index.html` |
| All 5 core projects fail | Anthropic API auth issue or Dark CLI broken | Run `claude --help` and `dark help` manually; the wrapper needs both |

#### What good looks like at 23:00

- `evals/bench/sweeps/dark-2026-05-05-2100/report.md` exists and is < 100 lines, structured per §6.2 mock
- `evals/bench/dashboard/index.html` exists and renders in a browser
- 23 rows in `evals/bench/results.jsonl` (one per Dark run, pass@1)
- Cost tracked: total close to $7, drift < 5% (per §6 #23 pricing-config-drift)
- Harness health panel: 26+ metrics rendered, all green
- The user can confidently send a coworker the HTML or markdown and have them parse it without explanation

---
