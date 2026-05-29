---
title: validation-applicative
tier: S
class: library-port
modules: [Stdlib.Result, Stdlib.List, Stdlib.String]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: true
---

# Description

A small library implementing the `Validation` applicative — an error-accumulating result type, structurally similar to `Result<a, err>` but with crucially different semantics.

The key distinction: **`Result` short-circuits at the first error; `Validation` accumulates *all* errors**. This matters in domains like form validation, parser combinators, and config loading where the user wants *every* error reported in one pass, not one-at-a-time.

Target module: `Darklang.Validation` (Dark) / `validation` (TS, npm-style) / `validation` (Py module). All languages must expose the same public API surface and the same error-accumulating semantics.

This is a port of [Haskell's `validation` package](https://hackage.haskell.org/package/validation) and equivalent libraries in F#, OCaml. The agent should reach for the idioms native to the target language (Dark's enums for the ADT, TS's tagged unions, Py's dataclasses, Go's interface-via-tagged-struct, Rust's enum) — but the *behaviour contract* below is identical across languages.

# Library API surface

The agent must implement these public types and functions:

- **Type**: `Validation<err, a>` — an ADT with two constructors: `Valid(a)` carrying a success value, `Invalid(List<err>)` carrying a non-empty list of errors. Idiomatic-language equivalents are fine.
- **`valid : a -> Validation<err, a>`** — wrap a success.
- **`invalid : err -> Validation<err, a>`** — wrap a single error (lifts to a singleton list internally).
- **`map : (a -> b) -> Validation<err, a> -> Validation<err, b>`** — transform success; passes Invalid through unchanged.
- **`apply : Validation<err, (a -> b)> -> Validation<err, a> -> Validation<err, b>`** — the *applicative magic*: if both Valid, applies the function; if one Invalid, returns it; **if both Invalid, concatenates the error lists**.
- **`combine2 : (a -> b -> c) -> Validation<err, a> -> Validation<err, b> -> Validation<err, c>`** — convenience for 2-arg.
- **`combine3 : (a -> b -> c -> d) -> Validation<err, a> -> Validation<err, b> -> Validation<err, c> -> Validation<err, d>`** — 3-arg.
- **`toResult : Validation<err, a> -> Result<List<err>, a>`** — escape hatch back to `Result`.

# Driver CLI

The agent also writes a thin `validation-cli` wrapper:

- `validation-cli combine2-int <a> <b>` — `combine2 (+) (validInt a) (validInt b)`. `validInt` parses string→int, returns `invalid("not-int: <s>")` on failure. Prints sum / errors.
- `validation-cli combine3-int <a> <b> <c>` — same shape, 3-arg.
- `validation-cli form name=<s> email=<s> age=<s>` — three validators (non-empty / has @ / positive int), combined with `combine3`. Prints all errors found.
- `validation-cli to-result <validation-expr>` — tiny test-DSL (`valid:5`, `invalid:e1,e2`), prints the `toResult` output.

# Behaviours (rubric tests these via validation-cli)

- `validation-cli combine2-int 3 4` prints `7`, exits 0.
- `validation-cli combine2-int 3 abc` prints `not-int: abc`, exits 1.
- `validation-cli combine2-int xyz abc` prints **both** errors on separate lines, exits 1. *(Validates accumulation.)*
- `validation-cli combine3-int 1 2 3` prints `6`, exits 0.
- `validation-cli combine3-int x 2 y` prints two errors, exits 1.
- `validation-cli form name=Alice email=a@b.com age=30` prints `OK`, exits 0.
- `validation-cli form name= email=invalid age=-5` prints **all three** errors (empty-name, no-@-in-email, age-not-positive), exits 1.
- `validation-cli form name= email=invalid age=foo` prints all three errors including the parse-error for age.
- `validation-cli to-result valid:5` prints `Ok(5)`, exits 0.
- `validation-cli to-result invalid:e1,e2` prints `Error([e1, e2])`, exits 0.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Read the library's `apply` implementation. Confirm it handles all 4 cases: (Valid, Valid), (Valid, Invalid), (Invalid, Valid), (Invalid, Invalid). The fourth is load-bearing — must concatenate, not pick.
2. Run `validation-cli form name= email=invalid age=-5` — verify all three errors appear, not just the first.
3. **Self-mutation check**: mentally substitute `apply` with a Result-style early-return; verify the rubric's `combine2-int xyz abc` test would fail under that substitution. (If the rubric would still pass, it's broken — flag this in `SUMMARY.md`.)
4. Library has its own minimal tests file (`tests/` idiomatic for the language) covering the 4 apply cases directly.
5. Record the agent's own confidence + any uncertainty in `SUMMARY.md`.

# Smoke commands (pre-rubric sanity)

- validation-cli combine2-int 3 4
- validation-cli combine2-int xyz abc
- validation-cli form name=Alice email=a@b.com age=30
- validation-cli form name= email=bad age=-5
- validation-cli --help

---

**Role**: §6 #15 (edit-format compliance) channel. Tests Dark's ability to host an ADT *distinct from* `Result` with different semantics. Validates the language's enum/match story under load. Agents that don't grok Dark's enum syntax (`| Valid x ->`) produce broken code that the rubric immediately rejects.

**The "library port that teaches a real distinction"** per iter 29 — pedagogical value beyond the rubric.
