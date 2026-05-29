# markdown-reader

**Goal:** Render and scroll a Markdown document with headers, emphasis, and code styling.

**Kind:** greenfield

## Acceptance criteria
- [ ] Renders headers, bold/italic, and code, and supports scroll/outline navigation.
- [ ] A keystroke sequence fed via a pty produces the expected screen at each step (snapshot/diff).
- [ ] Resizing the terminal mid-run redraws without corruption.
- [ ] `q` and `ESC` restore the cursor and reset ANSI state on exit.
- [ ] A hard kill mid-edit leaves persisted state recoverable (atomic writes).
- [ ] `--help` / `-h` prints usage and exits 0.
