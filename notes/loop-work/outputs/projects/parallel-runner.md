# parallel-runner

**Goal:** Run N commands with a concurrency cap, faking parallelism via spawned processes.

**Kind:** greenfield

## Acceptance criteria
- [ ] Runs commands with at most N in flight at once.
- [ ] Wall-clock time scales roughly with total work divided by the concurrency cap.
- [ ] A child process exiting non-zero surfaces as a typed error.
- [ ] A command exceeding its timeout is killed and reaped.
- [ ] Captured stdout and stderr are separable.
- [ ] Secrets passed via environment are masked from logs.
- [ ] `--help` prints usage and exits 0.
