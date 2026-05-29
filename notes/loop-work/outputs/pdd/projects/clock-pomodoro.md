# clock-pomodoro

**Goal:** Show a big-font ASCII clock with a Pomodoro timer state machine.

**Kind:** greenfield

## Acceptance criteria
- [ ] Renders a large clock and runs a configurable Pomodoro work/break cycle.
- [ ] A keystroke sequence fed via a pty produces the expected screen at each step (snapshot/diff).
- [ ] Resizing the terminal mid-run redraws without corruption.
- [ ] `q` and `ESC` restore the cursor and reset ANSI state on exit.
- [ ] A hard kill mid-edit leaves persisted state recoverable (atomic writes).
- [ ] `--help` / `-h` prints usage and exits 0.
