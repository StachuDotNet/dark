# dotfiles-sync

**Goal:** Fetch, diff, and merge a dotfiles repo and report the result.

**Kind:** greenfield

## Acceptance criteria
- [ ] Runs git fetch/diff/merge and reports what changed.
- [ ] A child process exiting non-zero surfaces as a typed error.
- [ ] A command exceeding its timeout is killed and reaped.
- [ ] Captured stdout and stderr are separable.
- [ ] Secrets passed via environment are masked from logs.
- [ ] `--help` prints usage and exits 0.
