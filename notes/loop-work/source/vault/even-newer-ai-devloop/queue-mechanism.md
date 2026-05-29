# Queue mechanism — the per-project / per-language lifecycle (round-2 P0 #4)

The bench processes a list of projects across multiple languages. Each `(project, language)` pair runs through a fixed lifecycle: **plan → impl → verify → reflect → extract → delete**. The queue is **stateful** (resumable on user interrupt), **concurrent** (~`nproc/2` parallel), and **rides on Multi's existing queue infrastructure** (per §4.8) where Multi handles rate-limit / turn-budget / phase tracking.

## The unit of work

One **bench task** = one `(project, language, attempt_n)` triple. For tonight: 10 projects × 5 languages × 1 attempt = 50 bench tasks. Each task is independent of the others; they don't share state. **A bench task is what gets enqueued, run, scored, reflected on, and torn down.**

Multiple bench tasks can run concurrently — they just need separate workspaces (per round-2 P0 #5: ephemeral `bench-<sweep>-<project>-<lang>-<attempt>` Dark branch, or `evals/bench/sweeps/<id>/runs/<rid>/artifact-<lang>/` for TS/Py/Go/Rust).

## State (what the queue persists across interruptions)

Two state files:

**1. `evals/bench/sweeps/<sweep_id>/queue-state.json`** — per-sweep, source of truth for THIS sweep's task lifecycle:

```json
{
  "sweep_id": "kickoff-2026-05-05-2100",
  "started_at": "2026-05-05T21:00:12Z",
  "tonight_queue_path": "ai-devloop/tonights-queue.md",
  "concurrency": 4,
  "tasks": {
    "bench-kickoff-2026-05-05-2100-password-gen-dark-attempt1": {
      "project": "password-gen",
      "language": "dark",
      "attempt": 1,
      "phase": "verify",                     // queued | planning | implementing | verifying | reflecting | extracting | deleting | done | failed | cutoff
      "started_at": "2026-05-05T21:01:03Z",
      "completed_at": null,
      "phase_timings": { "plan_min": 1.2, "impl_min": 5.4, "verify_min": null },
      "multi_task_id": "bench-kickoff-2026-05-05-2100-password-gen-dark-attempt1",
      "branch": "bench-kickoff-2026-05-05-2100-password-gen-dark-attempt1",
      "workspace_dir": null,                 // null for Dark; populated for TS/Py/Go/Rust
      "artifact_kept": false,                // post-delete this stays false; pre-delete true
      "outcome": null,                       // pass | fail | cutoff-fail | harness-flake | (null while running)
      "rubric_status": null,
      "cost_usd_api_equivalent": 0.18,
      "tokens": { "input": 1200, "output": 450, "cache_read": 0, "cache_creation": 0 }
    },
    "...49 more...": "..."
  },
  "summary": {
    "total": 50, "queued": 0, "running": 4, "done": 32, "failed": 6, "cutoff": 2
  }
}
```

**2. `evals/bench/sweeps/<sweep_id>/.lock`** — `flock(2)` file (per iter-48 sweep-lock decision). Held by the wrapper for the duration of the sweep. Second wrapper instance fails to acquire and exits.

## The lifecycle (per bench task)

Each task moves through these phases. Multi's `queue.Status` enum (`needs-prompt → ready → running → done | waiting | rate-limited | budget-hit | paused`) maps onto the *implementing* phase only; the wrapper's lifecycle is broader.

### 1. `queued`

Task exists in `queue-state.json` but hasn't been picked up yet. Wrapper's scheduler watches concurrency: if `running < concurrency` and there's a `queued` task, transition to `planning`.

**Pick order**: priority field (per iter-42 mapping — core projects=5, T=10, S=20, M=30, L=40), then by language order (Dark first, then Py, TS, Go, Rust per iter-83 tonights-queue.md).

### 2. `planning`

Wrapper sets up the workspace:

- For Dark: `multi new <branch>` (creates the `bench-<sweep>-...` branch in Multi's tracking).
- For TS/Py/Go/Rust: `mkdir -p evals/bench/sweeps/<id>/runs/<rid>/artifact-<lang>/` and copies in a minimal scaffold (e.g. `package.json` for TS, `pyproject.toml` for Py, `go.mod` for Go, `Cargo.toml` for Rust).

Resolves the spec via Jinja substitution per §4.7 (`task.md` template → final prompt). Writes the resolved prompt to `evals/bench/sweeps/<id>/runs/<rid>/prompt.txt`. Transitions to `implementing`.

### 3. `implementing`

Wrapper invokes the agent:
- Dark: `multi <branch> claude` (spawns ralph loop on host with `--dangerously-skip-permissions`).
- TS/Py/Go/Rust: `cd <workspace-dir> && claude --dangerously-skip-permissions <prompt>` (host-side, cwd-pinned).

Agent runs autonomously through the ralph loop. Wrapper polls Multi's `<branch>/.claude-task/phase` every 5s (per §4.7 done-detection). Phase transitions to `verifying` on `phase: done | ready-for-review`. Phase transitions to `cutoff` on `phase: budget-hit | timeout | error` per §4.9 cut-off policy.

### 4. `verifying`

Wrapper runs the rubric:
- For Dark: `dark --branch <branch> run @<RubricFn>` (or equivalent — the rubric is in Dark too, scoped to the same branch).
- For TS/Py/Go/Rust: shells out to the artifact under the workspace dir and inspects stdout / exit codes / files / HTTP responses per the spec's smoke commands + Behaviours.

Records `rubric_status: pass | fail` and per-bullet pass/fail breakdown.

### 5. `reflecting`

Wrapper extracts:
- The agent's `SUMMARY.md` (per §3.6 #3 + iter-86 reflection-template) — frontmatter parsed, body sections preserved.
- Per-phase timings (planning/implementing/verifying minutes).
- Per-Dark-run `Stdlib.Cli.Process.exec` workaround count (per iter-70 metric #23).

Writes `evals/bench/sweeps/<id>/runs/<rid>/metrics.json` capturing all of it.

### 6. `extracting`

Wrapper appends one row to `evals/bench/results.jsonl` (the global, all-sweeps, append-only file per §4.5). Row contents: project, language, sweep_id, run_id, outcome, all §6 metrics, link to artifacts.

### 7. `deleting`

Per round-2 P0 #5: purge the workspace.

- For Dark: `multi rm <branch>` (Multi-side cleanup) + `dark branch archive <branch>` (Dark-side archive).
- For TS/Py/Go/Rust: `rm -rf <workspace-dir>`.

The artifact's transcript / metrics / summary are *kept* (under `evals/bench/sweeps/<id>/runs/<rid>/`); only the *built artifact* (the workspace dir or branch's tip) is purged. **No peeking at the next attempt's prior artifact** — every new impl starts clean.

### 8. `done` (or `failed` / `cutoff`)

Terminal phase. Task no longer occupies a concurrency slot. Wrapper's scheduler picks up the next `queued` task.

## The scheduler

Single-process, watch-loop:

```
while there exist queued tasks AND wrapper has not been interrupted:
    if running < concurrency:
        next = pick highest-priority queued task
        spawn-async lifecycle for next   # threads / asyncio / subprocess pool
    sleep 5s
    poll all running tasks for phase transitions
    persist queue-state.json after each transition
```

Concurrency = `nproc/2` per the iter-46 default. Tonight: 4. **Per round-2 P0 #3**: no cost cap; concurrency is the throttle.

## Resumability (user interrupts; wrapper resumes)

User hits Ctrl+C, or the wrapper crashes, or the machine reboots. State is in `queue-state.json`. On restart:

1. Wrapper checks `<sweep>/.lock` — if held by a dead PID, take it.
2. Reads `queue-state.json`.
3. For each task in `running` phase: re-poll Multi for the actual current state. If Multi says the task is still in-flight, keep tracking. If Multi says it's terminal, advance the wrapper's lifecycle accordingly.
4. For tasks in `queued`: continue normally.
5. For tasks in `done | failed | cutoff`: skip.

This means a 3am machine reboot doesn't lose progress — when the user wakes at 7am, the sweep has already resumed and may even be done.

## CLI surface

Read-only proposal (per the iter-41 "thin shells over Python wrapper" pattern):

```
python -m harness sweep --queue tonights-queue.md
  Starts a sweep. Reads tonights-queue.md, builds the 50-task queue-state.json, kicks off the scheduler.
  Holds the .lock; exits 0 on completion, exits non-zero on partial completion.

python -m harness sweep --queue tonights-queue.md --resume
  Resumes a previously-interrupted sweep. Same flow but skips done/failed/cutoff tasks.

python -m harness sweep --dry-run --queue tonights-queue.md
  Prints what would happen; touches no state.

python -m harness status [--sweep <id>]
  Reads queue-state.json; prints summary table (queued/running/done/failed/cutoff).
  Default --sweep: most-recent active sweep.

python -m harness cancel [--sweep <id>] [--task <task_id>]
  Mark task(s) as paused. Wrapper's scheduler stops picking them up.
  --sweep with no --task: pause all running. (Use sparingly — running tasks consume tokens until they finish.)

python -m harness reflect <sweep_id>
  Reads all SUMMARY.md files; produces cli-friction-digest.md (per iter-86).
  Run automatically at end of sweep; can re-run manually.

python -m harness report <sweep_id> [--format md|html|both]
  Generates the per-sweep report (per §6.2). Default --format=both.

python -m harness dashboard [--last N]
  Regenerates the over-time dashboard (per §6.2).
```

## How this maps onto Multi

Multi's `queue.Task` is the *implementing-phase* unit. The wrapper's lifecycle wraps it:

| Wrapper phase | Multi state |
|---|---|
| `queued` | (task not yet `queue.Add`'d to Multi) |
| `planning` | `queue.Add` happens partway through; sets Multi to `ready` |
| `implementing` | Multi: `ready → running → done | rate-limited | budget-hit | waiting` |
| `verifying` | (Multi sees task as `done`; wrapper does its own verification work) |
| `reflecting` / `extracting` / `deleting` | (Multi-side: task stays `done`. Wrapper drives.) |
| `done` (final) | (wrapper updates `queue-state.json`; doesn't touch Multi state) |

Multi's queue.json gets the task added during `planning` and never deleted (per round-2 P0 #5: only the *branch* is deleted, not the queue record). The bench's parseable task IDs (`bench-<sweep>-...`) ensure Multi's own dev tasks aren't disturbed.

## How this differs from the iter-48 sweep-lock decision

Iter 48 specced sweep-lock as `flock(2)` on `evals/bench/.sweep-running`. **Updated iter 89**: per-sweep lock at `evals/bench/sweeps/<sweep_id>/.lock`. This way two sweeps with different IDs can coexist (rare, but useful for the user testing-on-the-side while a real sweep runs). Same flock mechanism; different file path.

The original `.sweep-running` global lock can stay too — for the case where the user *wants* to prevent any concurrent sweep. The wrapper checks both: per-sweep AND global. Either-held = exit.

## Tonight's specific scheduler config

```yaml
sweep_id: kickoff-2026-05-05-2100
concurrency: 4
queue: ai-devloop/tonights-queue.md   # 10 projects × 5 langs = 50 tasks
cost_cap: null   # round-2 P0 #3: no cap for the kickoff
turn_budget_per_attempt: 50
wall_cap_per_attempt_min: 15
auto_reflect_at_end: true
auto_dashboard_at_end: true
```

## Failure modes (cross-reference §4.9 failure-modes table)

The queue mechanism layers on top of §4.9's existing handling. New failure modes the queue mechanism specifically handles:

- **Wrapper crash mid-task**: queue-state.json is the recovery point. Resume from `--resume`.
- **Multi crash mid-task**: Multi's queue.json is persistent (per iter-41 verification); restart Multi, wrapper re-polls.
- **Disk full mid-write**: `queue-state.json` writes are atomic (temp-file + rename). Crash mid-write doesn't corrupt.
- **One task drains the queue**: the cut-off policy (per iter-87) prevents single-task runaway.
- **All tasks failing in same way**: harness-health metrics (#26 harness_flake by subclass, per iter-47) surface the pattern. Wrapper logs but doesn't auto-stop the sweep — the user wakes up to "47 of 50 failed with same error" and knows what to fix.

## What this gives the user

At 21:00 tonight: `python -m harness sweep --queue tonights-queue.md`, walk away.

At 07:00 tomorrow: `evals/bench/sweeps/kickoff-2026-05-05-2100/results.jsonl` has 50 rows. `report.md` and `dashboard/index.html` are regenerated. `cli-friction-digest.md` aggregates the agents' Dark-CLI-UX feedback. **Real numbers.**

If the sweep stalled or partial-completed, `python -m harness status` shows where it stopped. `python -m harness sweep --queue ... --resume` picks up.
