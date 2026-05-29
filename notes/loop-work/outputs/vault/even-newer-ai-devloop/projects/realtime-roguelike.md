---
title: realtime-roguelike
tier: M
class: tui
modules: [Stdlib.Cli.Stdin, Stdlib.Cli.UI, Stdlib.Cli.Host, Stdlib.List]
languages: [dark, ts, py, go, rust]
expected_outcome: fail-likely
known_blockers: [no-non-blocking-stdin]
framework_hint: null
core: false
---

# Description

A minimal real-time roguelike. The player controls `@` on a 20×10 grid using arrow keys. Each tick (every ~200 ms), the world advances: a wandering `M` (monster) takes a step in a random direction. The player's input is processed *as it arrives* without blocking the tick clock — so if the player doesn't move, the world still advances around them.

Press `q` to quit. Press `r` to reset. The game ends if the monster reaches the player (game over) or if the player reaches `>` (victory, exit 0).

The point of this project is **non-blocking input + tick clock**: the program needs to advance time *while* listening for input, not stop the world when waiting for a keypress.

For TS, the natural implementation is `process.stdin.setRawMode(true)` + `setInterval` for the tick. For Py, `select.select()` on stdin with a timeout, or `curses.halfdelay` mode. For Go, a goroutine reading stdin posting to a channel + `time.Ticker`. For Rust, `crossterm::event::poll` with a timeout. **For Dark today, no non-blocking-stdin primitive exists** — `Stdlib.Cli.Stdin.readKey` blocks (verified iter 50, also surfaced in vault `where we're a bit short.md`). There's no `readKeyWithTimeout` API; once `readKey` is called, the world stops until the user presses something.

The `improvements.md` proposal `stdinReadKeyWithTimeout(timeoutMs: Int64) -> Option<KeyRead>` (vault `Agent Next Steps.md` item) would unblock this spec — a ~30-line F# addition. Until that lands, the spec is `fail-likely`.

# Behaviours

- The program initializes a 20×10 grid with `@` at top-left, `>` (exit) at bottom-right, `M` (monster) somewhere in the middle.
- The program clears the screen, draws the grid with ANSI, and re-renders on every tick.
- Pressing arrow keys (Up / Down / Left / Right) moves `@` by one cell. The keypress is consumed and processed within ~50 ms.
- **The world ticks every ~200 ms regardless of input.** A monster takes a step in a random direction every 5 ticks. *If the player doesn't press anything for 2 seconds, the monster still moves.* This is the load-bearing behaviour.
- `q` quits cleanly: cursor restored, ANSI state reset, exit 0.
- `r` resets the grid to initial state.
- If `M` and `@` end up on the same cell: print "GAME OVER", exit 1.
- If `@` reaches `>`: print "VICTORY", exit 0.
- Walls (grid edges) prevent moves.
- The grid is rendered using box-drawing characters; the renderer doesn't flicker (uses cursor positioning rather than full screen redraws if possible).

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Run `realtime-roguelike` in a real terminal. Don't press anything for 5 seconds. **The monster should be moving.** If the monster only moves when you press a key, the spec failed — input is blocking.
2. Press an arrow key. `@` should move by one cell within ~50 ms.
3. Press arrow keys rapidly. Each should register; no input should be silently dropped.
4. Press `q`. The cursor should return to its normal terminal position; ANSI state should be reset; the shell prompt should look normal afterward.
5. Run `realtime-roguelike` then `kill -9` the process from another terminal. The terminal should be left in a usable state (no leftover raw-mode, no stuck cursor). If the terminal is hosed, no SIGTERM handler — *another known Dark gap* (vault `where we're a bit short.md`: "no signal handling other than sending signals").
6. **For Dark specifically**: examine the source. Did the agent:
   - (a) Implement a tight `readKey` loop with no tick? Game won't tick when player idle. *Likely outcome.*
   - (b) Use `Stdlib.Cli.Process.spawn` to fork a tick-pusher? Workaround.
   - (c) Honestly report the gap and produce a turn-based variant (no tick)? Honest fail.
   - (d) Try to use `setTimeout`-style scheduling that Dark doesn't have? Will fail compile.
7. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- realtime-roguelike --help
- echo q | realtime-roguelike   # should exit cleanly given an immediate q

---

**Why this is `fail-likely`**: the load-bearing primitive is `readKey` with a timeout. Dark's `Stdlib.Cli.Stdin.readKey` is blocking. The vault explicitly flags this:

> **No streaming stdin line iterator.** `readLine` / `readKey` exist, but `readKey` errors out when stdin is a pipe (not a terminal) — observed: `Cannot read keys when ... console input has been redirected`.

Plus signal handling is a separate gap — even if the game runs, killing it cleanly is fragile. **This spec exercises two adjacent runtime gaps** (no-non-blocking-stdin + no-signal-handling) — one project, two longitudinal trackers.

**The longitudinal value**: when Dark adds `stdinReadKeyWithTimeout` (proposed in improvements.md known-runtime-gaps notes per iter 50 / vault `Agent Next Steps.md`), this spec flips to passing. The signal-handling gap is separate; if it lands first, self-check step 5 still flags the remaining issue. Both gaps tracked independently.
