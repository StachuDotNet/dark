# heartbeat

**Goal:** Ping an uptime service on an interval with jitter.

**Kind:** greenfield

## Acceptance criteria
- [ ] Sends a heartbeat at the configured interval with randomized jitter.
- [ ] A file lock prevents two concurrent instances from overlapping.
- [ ] Schedule math is testable with a virtualized clock.
- [ ] Transient HTTP failures are retried within a budget.
- [ ] SIGTERM lets the in-flight job finish, then exits.
- [ ] Logs self-rotate and never fill the disk.
- [ ] `--help` prints usage and exits 0.
