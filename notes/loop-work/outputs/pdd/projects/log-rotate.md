# log-rotate

**Goal:** Rotate a directory of logs by size or age.

**Kind:** greenfield

## Acceptance criteria
- [ ] Rotates logs exceeding the size/age threshold and prunes old generations.
- [ ] A file lock prevents two concurrent instances from overlapping.
- [ ] Schedule math is testable with a virtualized clock.
- [ ] Transient HTTP failures are retried within a budget.
- [ ] SIGTERM lets the in-flight job finish, then exits.
- [ ] Logs self-rotate and never fill the disk.
- [ ] `--help` prints usage and exits 0.
