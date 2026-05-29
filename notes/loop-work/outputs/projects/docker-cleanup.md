# docker-cleanup

**Goal:** Safely remove exited Docker containers older than a threshold.

**Kind:** greenfield

## Acceptance criteria
- [ ] Removes only exited containers older than N, leaving running ones untouched.
- [ ] A child process exiting non-zero surfaces as a typed error.
- [ ] A command exceeding its timeout is killed and reaped.
- [ ] Captured stdout and stderr are separable.
- [ ] Secrets passed via environment are masked from logs.
- [ ] `--help` prints usage and exits 0.
