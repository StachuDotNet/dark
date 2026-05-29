# mvu-runtime

**Goal:** Provide a generic Model-View-Update runtime (the pure-functional core of the Elm Architecture) plus counter and todo example programs driven via a CLI.

**Kind:** greenfield

## Acceptance criteria
- [ ] Exposes `program`, `runProgram`, `step`, and `renderHistory` with the documented signatures; `Program<model, msg, view>` is constructed from `init`, `update`, and `view`.
- [ ] `runProgram` returns the final model, final view, and the history of intermediate models.
- [ ] `mvu-cli counter` (no msgs) prints the initial view `count: 0` and exits 0.
- [ ] `mvu-cli counter Inc Inc Inc` prints `count: 3`; `counter Inc Inc Dec` prints `count: 1`; `counter Inc Inc Reset` prints `count: 0`.
- [ ] `mvu-cli counter --history Inc Inc Dec` prints 4 lines including the initial state (`count: 0`, `count: 1`, `count: 2`, `count: 1`).
- [ ] `mvu-cli counter Bogus` exits non-zero with an "unknown msg" error.
- [ ] `mvu-cli todo "Add a" "Add b" "Toggle 0"` prints a 2-line view with "a" checked and "b" unchecked.
- [ ] `mvu-cli todo "Add a" "Remove 0"` prints an empty / "(no todos)" view.
- [ ] `mvu-cli todo "Toggle 99"` (out-of-range) exits non-zero with an error mentioning the index.
- [ ] `mvu-cli todo "Add a" "Add b" --history` prints 3 history entries (initial empty plus the two adds).
- [ ] `mvu-cli` with no subcommand exits non-zero with usage.
- [ ] `mvu-cli --help` exits 0.
