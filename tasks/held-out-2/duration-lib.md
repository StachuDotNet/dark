Build a small library for durations written like `1h30m`, `45s` or `2d4h` (units `d`, `h`, `m`, `s`, largest first, each at most once). One part parses such a text into a number of seconds and rejects bad input with a clear error. One part formats a number of seconds back into the shortest such text (`5400` is `1h30m`, `0` is `0s`). A command-line tool uses both: `dur add 1h30m 45m` prints `2h15m`, and `dur fmt 5400` prints `1h30m`; bad input prints the error and exits 1.

Document each public function so another developer can see what it does from the language's own tooling, and add tests for each part that run with one command.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** new library in several modules, with docs and tests
- **Areas:** modules, parsing, formatting, docs on functions, tests per module, CLI
- **Network sites:** none
- **Seed:** none

## Acceptance checks

1. `dur add 1h30m 45m` prints `2h15m`; `dur add 1d 1s` prints `1d1s`; `dur fmt 0` prints `0s`; `dur fmt 90061` prints `1d1h1m1s`.
2. `dur add 1h1h 5m`, `dur add 5x 1m` and `dur add "" 1m` exit 1 with an error.
3. Parsing, formatting and the command line are separate modules (Dark: separate package modules; Python: separate files).
4. Every public function has a description the tooling shows (Dark: `view` shows it; Python: a docstring).
5. Tests for each part run with one command and pass.
