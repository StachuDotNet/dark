# realtime-roguelike

**Goal:** Run a minimal real-time roguelike on a grid where the world advances on a tick clock while player input is processed without blocking that clock.

**Kind:** greenfield

## Acceptance criteria
- [ ] Initializes a 20×10 grid with `@` (player) at top-left, `>` (exit) at bottom-right, and `M` (monster) in the middle.
- [ ] Clears the screen, draws the grid with ANSI, and re-renders on every tick.
- [ ] Arrow keys move `@` by one cell, with the keypress consumed within ~50 ms.
- [ ] The world ticks every ~200 ms regardless of input; the monster steps in a random direction every 5 ticks even if the player does nothing (the load-bearing non-blocking-input behavior).
- [ ] `q` quits cleanly: cursor restored, ANSI state reset, exit 0.
- [ ] `r` resets the grid to its initial state.
- [ ] If `M` and `@` share a cell, prints "GAME OVER" and exits 1.
- [ ] If `@` reaches `>`, prints "VICTORY" and exits 0.
- [ ] Grid edges (walls) prevent moves off the board.
- [ ] The renderer avoids flicker (cursor positioning rather than full redraws where possible).
- [ ] `realtime-roguelike --help` exits 0.
