---
title: mvu-runtime
tier: M
class: library-port
modules: [Stdlib.List, Stdlib.Dict, Stdlib.String, Stdlib.Result]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: false
---

# Description

A small library implementing the Elm Architecture — Model-View-Update — as a generic runtime for state-machine programs. Users define a `Program` by providing an `init` value (the starting model), an `update` function (`Msg -> Model -> Model`), and a `view` function (`Model -> View`). The runtime steps the program through a sequence of messages and exposes the final model + the final view.

This is *not* a UI framework. There's no event loop, no DOM, no terminal rendering. It's the *pure-functional core* of MVU — a generic state machine with a clean API for stepping and observing. Real UI frameworks would build on top of this core (subscriptions, commands, side-effects) but those are out of scope.

The target module is `Darklang.MVU` (Dark) / `mvu` (TS/Py/Go/Rust). The agent should implement *both* the library *and* two example programs (counter, todo-list) on top of it, exposed via the driver CLI below.

This library port also serves as **ethos validation** for Dark — the user's vault flags MVU as core to Dark's design philosophy (`~/vaults/Darklang Dev/04.Ethos/Composable/MVU everywhere/`). If MVU lands cleanly in Dark, that's evidence Dark's idioms support the architecture; if it doesn't, that's signal about a gap in the language.

# Library API surface

The agent must implement these public types and functions:

- **Type**: `Program<model, msg, view>` — the configuration of an MVU program. Internal rep is the agent's choice (record with three fields, class with three methods, etc.).
- **`program : { init: model, update: msg -> model -> model, view: model -> view } -> Program<model, msg, view>`** — constructor.
- **`runProgram : Program<model, msg, view> -> List<msg> -> { finalModel: model, finalView: view, history: List<model> }`** — runs the program through a sequence of msgs, returns the final state plus the history of intermediate models (useful for debugging + test verification).
- **`step : msg -> { current: model, program: Program<m, msg, v> } -> { current: model, program: Program<m, msg, v> }`** — single-step variant. Useful for interactive debugging or test scenarios that interleave steps with assertions.
- **`renderHistory : List<model> -> view-function -> List<view>`** — convenience for "what did the user see at each step?"

# Driver CLI

`mvu-cli` exposes two hardcoded example programs built using the library. Each subcommand runs the program for a sequence of messages (passed as args) and prints the final state.

- **`mvu-cli counter <Msg> [<Msg>...]`** — the counter program. `Msg = Inc | Dec | Reset`. Model is `Int`. Initial state: 0. View: `"count: <n>"`.
  - `mvu-cli counter Inc Inc Dec Reset Inc` → prints the *final view* (`count: 1`) on stdout.
- **`mvu-cli todo <Msg> [<Msg>...]`** — the todo-list program. `Msg = Add <str> | Remove <int> | Toggle <int>`. Model is `List<{text: String, done: Bool}>`. Initial state: `[]`. View: a multi-line summary.
  - `mvu-cli todo "Add buy milk" "Add walk dog" "Toggle 0"` → prints the final view (something like `[x] buy milk\n[ ] walk dog`).
- **`mvu-cli counter --history <Msg>...`** — same as counter, but prints the model history one-per-line (for testing the `runProgram` history return value).
- **`mvu-cli todo --history <Msg>...`** — same idea.
- **`mvu-cli --help`** — usage.

Msg parsing for the todo subcommand: the rubric uses literal strings `Add <text>`, `Remove <idx>`, `Toggle <idx>` as msg arguments. The driver parses these into the `Msg` ADT. Quoting handles spaces in todo text.

# Behaviours (rubric tests these via mvu-cli)

- `mvu-cli counter` (no msgs) — prints the initial view (`count: 0`), exits 0.
- `mvu-cli counter Inc` — prints `count: 1`.
- `mvu-cli counter Inc Inc Inc` — prints `count: 3`.
- `mvu-cli counter Inc Inc Dec` — prints `count: 1`.
- `mvu-cli counter Inc Inc Reset` — prints `count: 0`.
- `mvu-cli counter Reset Reset Reset` — prints `count: 0`.
- `mvu-cli counter --history Inc Inc Dec` — prints the history: 4 lines (`count: 0`, `count: 1`, `count: 2`, `count: 1`), one per line. Includes the initial state.
- `mvu-cli counter Bogus` — exits non-zero with an "unknown msg" error.
- `mvu-cli todo "Add buy milk"` — prints a 1-line todo view with "buy milk" unchecked.
- `mvu-cli todo "Add a" "Add b" "Toggle 0"` — prints a 2-line view: "a" checked, "b" unchecked.
- `mvu-cli todo "Add a" "Remove 0"` — prints empty (or "(no todos)" — both acceptable).
- `mvu-cli todo "Toggle 99"` (out-of-range index) — exits non-zero, error mentions the index.
- `mvu-cli todo "Add a" "Add b" --history` — prints 3 entries in the history (initial empty + after Add a + after Add b).
- `mvu-cli` (no subcommand) — exits non-zero with usage.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. **Read `runProgram`'s implementation.** Is it a simple `List.fold` over the msgs? It should be. If the implementation is a 50-line stateful loop, that's a code-smell — MVU is *the* fold-shaped pattern. Note in `SUMMARY.md`.
2. Run a counter sequence and verify against the expected behaviour: `mvu-cli counter --history Inc Inc Dec Inc Reset` should print 6 lines (`count: 0` through `count: 1`, ending with `count: 0`).
3. **The "feels right" criterion** (per iter-58, library-port pattern): does `program(...)` look like an Elm-style configuration? Or did the implementation invent something stranger? The point of the port is to bring an idiom *over*, not to invent something new.
4. **Mutation test**: substitute `update` with `(_, m) -> m` (no-op). Re-run any non-trivial test from the rubric. The rubric should fail (counter would always be 0). If it passes, the rubric isn't actually exercising the library's behaviour — flag in `SUMMARY.md`.
5. The library should be idiomatic for the language. Dark uses an enum for `Msg`; TS uses a discriminated union or string-literal-union; Py uses dataclasses with a tag; Go uses a sealed interface or sum-style struct; Rust uses an enum. **Same as validation-applicative's pattern.**
6. Read the example programs (counter, todo). They should be ~10–30 lines each. If the example programs are larger than the library itself, the library API is wrong.
7. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- mvu-cli counter Inc Inc Dec
- mvu-cli todo "Add buy milk" "Toggle 0"
- mvu-cli counter --history Inc
- mvu-cli Bogus
- mvu-cli --help

---

**Why this spec is ethos-validation**: per iter-29, MVU is flagged in the user's vault as core to Dark's ethos. A clean MVU port is evidence Dark's idioms support the architecture. A *messy* MVU port (the agent fights the type system, or `runProgram` becomes 100 lines) is evidence of a gap. **The §6 metric to watch is #15 (edit-format compliance)** — agents that don't grok Dark's enum syntax will produce broken `Msg` types that the rubric immediately rejects. *Twin signal with validation-applicative's edit-format-compliance check, but on a more complex ADT.*
