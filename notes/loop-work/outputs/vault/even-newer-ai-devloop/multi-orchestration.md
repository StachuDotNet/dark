# Orchestration via Multi

> Extracted from plan.md §4.8 (iter 118) — the bench reuses Multi's queue/processor/rate-limit/turn-budget infrastructure. This file specs the integration in detail. Cross-references plan.md §4.4 (parallelism) and §4.5 (storage), `queue-mechanism.md` (per-task lifecycle), and `nightly-cadence.md` (cron firing).

The bench reuses [`~/code/dark-multi/`](~/code/dark-multi/)'s queue/processor/rate-limit/turn-budget infrastructure. Multi runs the *agent loop*; the bench layer (`evals/harness/`) provides specs, rubrics, gold references, metric extraction, A/B comparison, and dashboards.

#### Why extend Multi rather than fork or build fresh

Verified iter 41 against `queue/queue.go` + `queue/processor.go`: Multi already implements 70% of what the bench's orchestration layer needs. The Status enum (`needs-prompt → ready → running → waiting → rate-limited → budget-hit → done → paused`) is *exactly* the per-attempt lifecycle. Rate-limit detection + 5-min backoff + auto-restart are battle-tested. Turn budgets per task (default 30, configurable) map to our $0.50 north-star cap. Per-instance port mapping (`bwd_port = 11001 + 100*id`) eliminates the iter-26 startup-flakiness class of failure architecturally. The `summary/summary.go` AI-powered-Claude-session-summarizer is *exactly* what §3.6 #3 (agent SUMMARY.md) needs — already written.

Forking would duplicate ~1000 LOC of queue/processor/rate-limit code we'd otherwise inherit. Building fresh would re-derive the same lifecycle. **Extend in-place** with a `bench` mode.

#### How the bench rides on Multi (no Multi schema change required)

Multi's `Task` struct (`queue/queue.go:30`) is sufficient as-is for bench attempts. The bench keeps its own metadata in `evals/`, keyed by Multi's `Task.ID`:

| Multi field | Bench use |
|---|---|
| `ID` | `bench-<sweep_id>-<project>-<language>-attempt<N>` (a stable, parseable tag) |
| `Name` | Display name (`csv-to-json (dark, a1)`) |
| `Prompt` | Resolved Jinja substitution from §4.7 `task.md` |
| `Priority` | Lower for trivial-tier projects (run first), higher for L-tier |
| `MaxTurns` | 50 per §4.7 reproducibility settings |
| `Status` | Tracks attempt state (see mapping below) |
| `CreatedAt` / `StartedAt` / `CompletedAt` | Wall-time markers; feed §6 #7 (median wall time) |
| `RateLimitedAt` / `ResumeAfter` | Rate-limit handling for free |
| `Error` | If agent gets stuck, message goes here |

Multi's status enum maps to bench attempt outcomes:

| Multi status | Bench interpretation |
|---|---|
| `needs-prompt` | spec hasn't rendered yet (rare; bench writes prompts at enqueue time) |
| `ready` | queued, waiting for a slot |
| `running` | agent working |
| `done` | agent emitted `<phase>DONE</phase>` (per iter 50) or hit turn budget cleanly → rubric runner picks up |
| `waiting` | agent got stuck → counts as `agent_abandoned: true` (§4.2) |
| `rate-limited` | API rate limit; auto-resume per Multi |
| `budget-hit` | exceeded MaxTurns → counts as failed-on-budget (separate from $0.50 cost cap, which the wrapper enforces independently) |
| `paused` | manual pause; bench skips, doesn't score |

**Critical separation**: Multi's `done` means *the agent stopped*. The bench's rubric runner is what produces `pass: true|false`. The bench's `metrics.json` records both: `multi_status = "done"`, `rubric_status = "pass"`, `cost_dollars = 0.43`. Conflating these is what the iter-35 Anthropic-harness survey warned about ("agents reliably skew positive when grading their own work").

#### Filesystem layout (additions to §4.0)

Multi's existing files (untouched by bench):
```
~/.config/dark-multi/
  queue.json                    # Multi's persistent queue
  overrides/<branch>/...        # Multi's per-branch metadata
```

New bench artifacts (live under the repo's `evals/`, per §4.0):
```
evals/
  bench/
    sweeps/
      <sweep_id>/
        manifest.json           # which projects × languages × attempts; cost cap
        runs/<run_id>/
          metadata.json         # multi_task_id, project, language, attempt
          prompt.txt            # the resolved §4.7 Jinja-substituted task prompt
          transcript.json       # Claude Code session output
          telemetry.jsonl       # symlink/copy from per-run rundir (§4.2)
          artifact/             # the agent's output (workspace dir)
          metrics.json          # rubric_status, cost, tokens, wall, etc.
          cli-invocations.jsonl # exit-code wrapper output (Dark only)
        results.jsonl.partial   # per-run rows, append during the sweep
        report.md               # generated when the sweep completes
        report.html             # exported visual (§6.2)
    results.jsonl               # global, append-only, all sweeps (§4.5)
    pricing.json                # per-model prices (§6.0 cost-attribution)
    baselines.json              # pinned `(dark_sha, sweep_id)` per §7 Phase 3
    improvements/<branch>.md    # one per A/B wave retro
```

The `evals/bench/sweeps/<sweep_id>/` shape is **per-sweep self-contained** — every artifact for that night's run is in one directory. Tarball-friendly for the §4.5.1 retention policy.

#### `multi bench` subcommand surface (proposed)

CLI is read-only proposal here; actual implementation is downstream. All commands operate on the bench harness; Multi's existing `multi <branch>` commands continue to manage dev branches independently.

```
multi bench enqueue <sweep_id> [--projects all|p1,p2,...] [--languages dark,ts,py] [--attempts 1|2] [--core-only] [--dark-revision <ref>]
  Resolves project specs into Multi tasks; populates queue.json with bench tasks.
  Default <sweep_id>: "auto-YYYY-MM-DD-HHMM" using local time.
  --core-only: only run core projects (§4.1.X — TBD); ~5 projects, fastest signal.
  --dark-revision: per §7 Phase 3 A/B; checks out a worktree, builds the CLI, points bench at it.

multi bench status [--sweep <id>]
  Prints current sweep progress. Default: most-recent.
  Output: per-status counts, ETA, current cost, top-3 failing projects.

multi bench wait [--sweep <id>] [--timeout 14400]
  Blocks until the sweep finishes or timeout. For cron-driven nightly runs.

multi bench score [--sweep <id>]
  Runs the rubric runner on every Done task in the sweep, fills metrics.json.
  Idempotent — re-runs only un-scored tasks.

multi bench report [--sweep <id>] [--out <path>] [--format md|html|both]
  Generates the per-sweep report (§6.2). Default --format=both → report.md + report.html.

multi bench dashboard [--out <path>] [--last N]
  Regenerates the over-time dashboard from results.jsonl (§6.2).
  --last N: include only the last N sweeps. Default: all.

multi bench cancel [--sweep <id>]
  Marks running tasks as paused, preserves partial results.

multi bench retain [--keep-uncompressed N]
  §4.5.1 retention sweep: tarball older sweeps, flag tarballs > 12mo.
```

Each subcommand is a thin layer over Multi's existing queue API. `enqueue` calls `queue.Add()`. `status` reads `queue.GetByStatus()` and overlays bench-specific data from `metadata.json`. `score` and `report` and `dashboard` operate purely on `evals/bench/` artifacts and don't touch Multi's queue.

#### What stays out of Multi entirely

These are bench-layer concerns; Multi never sees them:

- **Spec / rubric / gold-reference** (`evals/projects/<name>/`)
- **Metric extraction** (`metrics.py` — parses transcript + telemetry into `metrics.json`)
- **A/B comparison** (`python -m harness ab` per §7 Phase 3 — purely operates on results.jsonl)
- **Dashboard / report** (§6.2 — static HTML, never in Multi's TUI)
- **Pricing config** (`pricing.json` — per-model $/token)
- **Constraint-mode enforcement** (PATH-whitelist sandboxing per §4.3.2 — done by the wrapper around Claude Code, not Multi)

The principle: **Multi orchestrates agent loops; bench evaluates agent outputs.** Clean line.

#### How TS/Py runs work without devcontainers

Multi today is Dark-devcontainer-shaped. TS/Py runs don't need a Dark devcontainer; they need a workspace dir. The bench bypasses Multi's container-spinup for non-Dark languages:

- **Dark run** *(refined iter 84 per round-2 P0 #5)*: Dark is universally installed on the host. Each Dark task scopes to a `--branch <bench-<sweep_id>-<project>-<run_id>>`. The agent uses `dark --branch <name> fn ...` etc. No workspace dir. After analysis: `multi rm <branch>` + `dark branch archive <branch>` to purge the branch. **Same purge model for TS / Py / Go / Rust workspace dirs** — `evals/bench/sweeps/<id>/runs/<rid>/artifact-<lang>/` is created fresh, used, scored, then `rm -rf`'d before the next impl on that project (no peeking).
- **TS run**: bench wrapper creates `evals/bench/sweeps/<id>/runs/<rid>/artifact-ts/` with no container. Claude on host, cwd pinned to that dir, `PATH` whitelisted to `node`/`npm` per §4.3.2. Multi tracks it as a task that runs without container coordination — Multi's queue still gives us rate-limit handling and turn budgets. **Purged before next attempt.**
- **Py / Go / Rust runs**: same shape as TS, different language tooling (`python`/`uv`, `go`/`cargo`).

**Verified iter 42 — Multi's `task/` package is host-rooted, container is Dark-only**:
- `task.Task.BranchPath` (`~/code/dark-multi/task/task.go:100`) — points at a host filesystem dir (`~/code/dark/<branch>/`). Not container-internal.
- Task state lives on the host in `<BranchPath>/.claude-task/{phase, turns, rate-limited-until, todos.md}` — no container needed for tracking.
- `tmux.StartRalphLoop()` (`~/code/dark-multi/tmux/tmux.go:236`) — explicitly comments *"Start the Ralph loop on the HOST (not in a container)."* Verified.
- `queue/processor.go:177-182` is the *only* place the container is required: `// Start container if not running (still needed for the dev environment)`. That step is a conditional on Dark's task type — for TS/Py, skip it.

Implementation shape (downstream of this spec; small):
```go
// queue/processor.go (illustrative — no edits made here)
if t.NeedsContainer {  // new field, defaults true for back-compat with dev tasks
    if err := container.Start(...); err != nil { ... }
}
if err := taskObj.InjectTaskContext(); err != nil { ... }  // host-side; same for all
if err := tmux.StartRalphLoop(...); err != nil { ... }     // host-side; same for all
```

For tonight's launch, **the bench wrapper drives `multi`'s existing CLI directly** without changing Multi's processor — by creating a "branch" pointed at a non-Dark workspace dir and stopping the container manually. Container is wasted but cheap; the lift to "real" container-less support happens later.

#### Priority assignment (decided iter 42)

`multi bench enqueue` derives priority from project tier:

| Tier | `Task.Priority` | Why |
|---|---|---|
| T (trivial) | 10 | Run first — fastest signal that the harness works at all |
| S (small) | 20 | Bulk of the bench |
| M (medium) | 30 | HTTP / DB-shaped work; harness flake more likely |
| L (large) | 40 | Most expensive per run; run last when other tiers are de-risked |
| core (any tier) | 5 | Always run *first*, regardless of tier — these are the §6.0 fix-iter-delta canaries |

Lower number = higher priority per Multi's queue convention (`queue.go:Task.Priority`). Core projects override their tier-default and always run first.

#### Tonight's anchor

`multi bench enqueue tonight --projects core-only --languages dark,ts,py --attempts 1` should work end-to-end and produce baseline numbers + an HTML dashboard within 1–2 hours. The bench-mode wrapper, the 5 core projects' specs+rubrics+gold, and the report generator are the critical-path implementation work for tonight. **Per-Multi-extension implementation order**:

1. Stand up core project specs + rubrics (no harness wrapper yet — just the markdown).
2. Wrapper script (`evals/harness/main.py`) that takes a sweep_id and resolves specs → Multi tasks, polls Multi for status, runs rubric on done tasks, writes metrics.
3. Report/dashboard generator (`evals/harness/report.py`).
4. The `multi bench` Cobra subcommands are *thin shells* around the Python wrapper — Phase 2+ if `python -m harness` works manually first.

This means tonight's path doesn't *strictly require* Multi changes — the wrapper can use `multi`'s CLI directly (`multi new <branch>`, etc.) without new subcommands. Speeds tonight's launch.

