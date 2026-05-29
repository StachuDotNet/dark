# backup

**Goal:** Archive a directory via shell-out tar, checksum it, and optionally sync to a target.

**Kind:** greenfield

## Acceptance criteria
- [ ] Produces a tar archive, records its sha256, and optionally pushes to a remote target.
- [ ] A child process exiting non-zero surfaces as a typed error.
- [ ] A command exceeding its timeout is killed and reaped.
- [ ] Captured stdout and stderr are separable.
- [ ] Secrets passed via environment are masked from logs.
- [ ] `--help` prints usage and exits 0.
