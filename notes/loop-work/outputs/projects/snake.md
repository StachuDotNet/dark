# snake

**Goal:** Provide a grid-based Snake game in the terminal.

**Kind:** greenfield

## Acceptance criteria
- [ ] Moves the snake with the keyboard, grows on food, and ends on self/wall collision.
- [ ] A keystroke sequence fed via a pty produces the expected screen at each step (snapshot/diff).
- [ ] Resizing the terminal mid-run redraws without corruption.
- [ ] `q` and `ESC` restore the cursor and reset ANSI state on exit.
- [ ] A hard kill mid-edit leaves persisted state recoverable (atomic writes).
- [ ] `--help` / `-h` prints usage and exits 0.
