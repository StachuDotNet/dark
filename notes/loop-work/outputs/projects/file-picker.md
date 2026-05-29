# file-picker

**Goal:** Fuzzy-find files over a rooted tree with a preview pane, like `fzf`-lite.

**Kind:** greenfield

## Acceptance criteria
- [ ] Fuzzy-matches file paths as the user types and previews the selection.
- [ ] A keystroke sequence fed via a pty produces the expected screen at each step (snapshot/diff).
- [ ] Resizing the terminal mid-run redraws without corruption.
- [ ] `q` and `ESC` restore the cursor and reset ANSI state on exit.
- [ ] A hard kill mid-edit leaves persisted state recoverable (atomic writes).
- [ ] `--help` / `-h` prints usage and exits 0.
