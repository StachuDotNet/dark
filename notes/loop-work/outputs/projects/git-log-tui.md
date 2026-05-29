# git-log-tui

**Goal:** Browse a scrollable commit list with a diff preview pane.

**Kind:** greenfield

## Acceptance criteria
- [ ] Scrolls through commits and shows the selected commit's diff.
- [ ] A keystroke sequence fed via a pty produces the expected screen at each step (snapshot/diff).
- [ ] Resizing the terminal mid-run redraws without corruption.
- [ ] `q` and `ESC` restore the cursor and reset ANSI state on exit.
- [ ] A hard kill mid-edit leaves persisted state recoverable (atomic writes).
- [ ] `--help` / `-h` prints usage and exits 0.
