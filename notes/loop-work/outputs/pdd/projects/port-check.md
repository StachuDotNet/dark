# port-check

**Goal:** Check connectivity to a batch of host/port pairs.

**Kind:** greenfield

## Acceptance criteria
- [ ] Reports reachability for each host:port and a summary.
- [ ] A child process exiting non-zero surfaces as a typed error.
- [ ] A command exceeding its timeout is killed and reaped.
- [ ] Captured stdout and stderr are separable.
- [ ] Secrets passed via environment are masked from logs.
- [ ] `--help` prints usage and exits 0.
