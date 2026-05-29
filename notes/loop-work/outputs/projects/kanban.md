# kanban

**Goal:** Provide a keyboard-driven Kanban board with JSON-on-disk persistence.

**Kind:** greenfield

## Acceptance criteria
- [ ] Moves cards between columns with the keyboard and persists the board as JSON.
- [ ] A keystroke sequence fed via a pty produces the expected screen at each step (snapshot/diff).
- [ ] Resizing the terminal mid-run redraws without corruption.
- [ ] `q` and `ESC` restore the cursor and reset ANSI state on exit.
- [ ] A hard kill mid-edit leaves persisted state recoverable (atomic writes).
- [ ] `--help` / `-h` prints usage and exits 0.
