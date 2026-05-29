# batch-ffmpeg

**Goal:** Glob media files and run ffmpeg per file with a progress bar.

**Kind:** greenfield

## Acceptance criteria
- [ ] Processes each matched file through ffmpeg and shows aggregate progress.
- [ ] A child process exiting non-zero surfaces as a typed error.
- [ ] A command exceeding its timeout is killed and reaped.
- [ ] Captured stdout and stderr are separable.
- [ ] Secrets passed via environment are masked from logs.
- [ ] `--help` prints usage and exits 0.
