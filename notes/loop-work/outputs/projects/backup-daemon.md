# backup-daemon

**Goal:** Run a daemon that snapshots and rotates backups every N hours.

**Kind:** greenfield

## Acceptance criteria
- [ ] Takes a snapshot on schedule and rotates old snapshots by count/age.
- [ ] A file lock prevents two concurrent instances from overlapping.
- [ ] Schedule math is testable with a virtualized clock.
- [ ] Transient HTTP failures are retried within a budget.
- [ ] SIGTERM lets the in-flight job finish, then exits.
- [ ] Logs self-rotate and never fill the disk.
- [ ] `--help` prints usage and exits 0.
