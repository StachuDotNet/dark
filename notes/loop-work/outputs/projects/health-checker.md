# health-checker

**Goal:** Curl a set of URLs and alert via webhook when status changes.

**Kind:** greenfield

## Acceptance criteria
- [ ] Polls URLs and posts an alert webhook only on a status transition.
- [ ] A file lock prevents two concurrent instances from overlapping.
- [ ] Schedule math is testable with a virtualized clock.
- [ ] Transient HTTP failures are retried within a budget.
- [ ] SIGTERM lets the in-flight job finish, then exits.
- [ ] Logs self-rotate and never fill the disk.
- [ ] `--help` prints usage and exits 0.
