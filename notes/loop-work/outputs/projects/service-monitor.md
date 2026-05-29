# service-monitor

**Goal:** Periodically check service status and notify on change.

**Kind:** greenfield

## Acceptance criteria
- [ ] Polls service status on an interval and emits a notification when state changes.
- [ ] A child process exiting non-zero surfaces as a typed error.
- [ ] A command exceeding its timeout is killed and reaped.
- [ ] Captured stdout and stderr are separable.
- [ ] Secrets passed via environment are masked from logs.
- [ ] `--help` prints usage and exits 0.
