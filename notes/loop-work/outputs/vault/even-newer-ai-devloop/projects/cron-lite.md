---
title: cron-lite
tier: M
class: daemon
modules: [Stdlib.Cli.Posix, Stdlib.Cli.Process, Stdlib.Cli.File, Stdlib.DateTime, Stdlib.Json]
languages: [dark, ts, py, go, rust]
expected_outcome: stretch
known_blockers: [no-async-primitives]
framework_hint: null
core: false
---

# Description

A long-running job scheduler. Reads a config file listing jobs (with cron-like schedules + a command to run); on every tick, fires any jobs whose schedule has elapsed since the last run. State (per-job last-fired timestamps) persists in a JSON file so a daemon restart doesn't lose its place.

The point of this project is **long-running daemon behavior**: continuous loops with timers, atomic state updates, restart-safety, signal handling, and idempotent catch-up semantics. It exercises a different class of skills than a one-shot CLI — graceful shutdown, file locking, clock-monotonic vs wall-clock decisions.

The schedule format is a small subset of cron: `*/N * * * *` (every N minutes), `M H * * *` (specific time daily), or `@every <duration>` (e.g. `@every 30s`). The complete cron grammar is *not* required — that's the `cron-describe` core's territory.

For TS, natural implementation uses `node-cron` or hand-rolled `setInterval`. For Py, `apscheduler` or `schedule` package. For Go, `github.com/robfig/cron` or hand-rolled `time.Ticker`. For Rust, `tokio-cron-scheduler` or hand-rolled. **For Dark today**: `Stdlib.Cli.Posix.sleep` blocks the loop; `Stdlib.Cli.File.withLock` provides single-instance protection per project-survey §1; `Process.spawn` runs jobs without blocking the daemon. Daemon mode is feasible *sequentially* — the daemon ticks every N seconds, fires due jobs, sleeps. No async needed at the daemon-loop level (the workaround for `no-async-primitives` is "don't need it for this shape").

`expected_outcome: stretch` because: feasible without async primitives, but signal handling (clean SIGTERM) is a known Dark gap that affects daemon shutdown.

# Behaviours

- `cron-lite --config jobs.yaml --state state.json` starts the daemon. Loops indefinitely until SIGINT/SIGTERM.
- Config file lists jobs as `{id, schedule, command}` triples (JSON or YAML; pick one).
- On startup: load `state.json` if it exists; otherwise initialize empty.
- On each tick (default 1s): for each job, check if `now() - last_fired >= schedule_interval`. If yes, run the command via `Process.spawn`, update `last_fired` in state.
- State writes are atomic — a crash mid-write doesn't corrupt the state file. Use `File.writeAtomic` (Dark) or equivalent.
- Single-instance protection: a second `cron-lite --config jobs.yaml` invocation against the same state file fails to acquire a lock and exits non-zero.
- `--catch-up` flag: if a job was due during a downtime, fire it once on resume (not multiple times if multiple intervals were missed).
- `--list` flag: print jobs and their next-fire times; exit 0 (does not start the daemon).
- `--once` flag: process one tick worth of due jobs and exit. (Useful for testing.)
- SIGINT (Ctrl+C): finish the current tick's jobs, write state, exit 0.
- SIGTERM: same as SIGINT.
- Job that fails (exit non-zero) is logged but doesn't crash the daemon.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Create a config file with 2 jobs: one `@every 5s` writing to `/tmp/job1.log`, one `@every 12s` writing to `/tmp/job2.log`.
2. Run `cron-lite --config jobs.yaml --state /tmp/state.json &` in background. Wait 30 seconds.
3. Check `/tmp/job1.log` has ~6 entries (5-second cadence over 30s ≈ 6); `/tmp/job2.log` has ~2-3 entries.
4. Send SIGTERM (`kill <pid>`). Process should exit 0 within ~1 second; state file should reflect the last successful runs.
5. Restart the daemon. New entries continue accumulating; *no duplicate entries* from the resume.
6. **Single-instance test**: while one is running, start a second `cron-lite --config jobs.yaml --state /tmp/state.json`. The second should exit non-zero with "already running" or similar.
7. **Crash test**: `kill -9` mid-tick. Restart. State should be readable (atomic-write should have prevented corruption); daemon resumes.
8. **For Dark specifically**: examine the source. Did the agent reach for `File.withLock` (correct per project-survey §1)? Did they reach for `File.writeAtomic` for state writes? Did they handle SIGTERM cleanly via `Stdlib.Cli.Posix.signals`-or-equivalent? Each of these is a *Dark stdlib discovery test* — failure to reach for them shows up as a self-check step that fails. Note in `SUMMARY.md`.
9. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- cron-lite --config jobs.yaml --once
- cron-lite --config jobs.yaml --list
- cron-lite --help
- cron-lite --config /no/such/file

---

**Why this spec is the daemon breadth pick**: covers Class K (long-running, state-persistent, signal-handled). The catalog has 5 daemon candidates (`cron-lite`, `backup-daemon`, `log-rotate`, `health-checker`, `heartbeat`) — `cron-lite` is the simplest and exercises the most of the daemon-shape capabilities (timer, state, catch-up, locking, signals).

**Cross-reference to expected-to-fail #4 `realtime-roguelike`**: both projects exercise signal handling. They're independently spec'd because the shape is different (TUI vs daemon) but the underlying gap (`no-signal-handling`) is shared. When that gap closes, both specs benefit.

**Idiomaticity expectation**: a Dark agent who reaches for `Stdlib.Cli.File.withLock` + `File.writeAtomic` + `Posix.sleep` is doing exactly the right thing — these primitives exist for this shape of project. **§6 #24 (idiomaticity ratio) for this spec should be near-100%**; a workaround would mean the agent failed Discovery (§3.1).
