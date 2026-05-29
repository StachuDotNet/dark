# cron-lite

**Goal:** Run a long-running job scheduler that fires configured jobs on their schedules, persists last-fired state across restarts, and shuts down cleanly.

**Kind:** greenfield

## Acceptance criteria
- [ ] `cron-lite --config jobs.yaml --state state.json` starts the daemon and loops until SIGINT/SIGTERM.
- [ ] The config lists jobs as `{id, schedule, command}` triples; the schedule supports `*/N * * * *`, `M H * * *`, and `@every <duration>`.
- [ ] On startup it loads existing state, or initializes empty if none exists.
- [ ] On each tick (default 1s) it fires any job whose interval has elapsed since its last run, via spawned subprocess, and updates the last-fired state.
- [ ] State writes are atomic — a crash mid-write does not corrupt the state file.
- [ ] Single-instance protection: a second invocation against the same state file fails to acquire a lock and exits non-zero.
- [ ] `--catch-up` fires a job missed during downtime exactly once on resume (not once per missed interval).
- [ ] `--list` prints jobs and their next-fire times and exits 0 without starting the daemon.
- [ ] `--once` processes a single tick of due jobs and exits.
- [ ] SIGINT and SIGTERM both finish the current tick's jobs, write state, and exit 0.
- [ ] A job that exits non-zero is logged but does not crash the daemon.
- [ ] `cron-lite --help` exits 0.
