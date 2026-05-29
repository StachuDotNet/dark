# todo-tui

**Goal:** Provide a keyboard-navigated, file-backed to-do list TUI with checkboxes.

**Kind:** greenfield

## Acceptance criteria
- [ ] Navigates a list with the keyboard and toggles item completion.
- [ ] The list is persisted to disk and reloaded on start.
- [ ] A keystroke sequence fed via a pty produces the expected screen at each step (snapshot/diff).
- [ ] Resizing the terminal mid-run redraws without corruption.
- [ ] `q` and `ESC` restore the cursor and reset ANSI state on exit.
- [ ] A hard kill mid-edit leaves persisted state recoverable (atomic writes).
- [ ] `--help` / `-h` prints usage and exits 0.
