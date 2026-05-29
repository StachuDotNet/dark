# process-viewer

**Goal:** Display a refreshing process list parsed from `/proc`, like `htop`-lite.

**Kind:** greenfield

## Acceptance criteria
- [ ] Renders a live-refreshing process table parsed from system process info.
- [ ] A keystroke sequence fed via a pty produces the expected screen at each step (snapshot/diff).
- [ ] Resizing the terminal mid-run redraws without corruption.
- [ ] `q` and `ESC` restore the cursor and reset ANSI state on exit.
- [ ] A hard kill mid-edit leaves persisted state recoverable (atomic writes).
- [ ] `--help` / `-h` prints usage and exits 0.
