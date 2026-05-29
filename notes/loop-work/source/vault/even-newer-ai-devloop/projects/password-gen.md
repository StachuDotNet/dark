---
title: password-gen
tier: T
class: app
modules: [Stdlib.Cli, Stdlib.Crypto, Stdlib.Int64, Stdlib.String, Stdlib.List]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: true
---

# Description

A command-line password generator. The user requests a password with a given length and character-class mix; the program prints one to stdout.

When seeded with `--seed N`, output is deterministic — the same seed always produces the same password. (This makes the program testable without coordinating across runtimes' RNG behavior.)

The program reads no stdin and writes no files. Its only side effect is one line on stdout, plus a non-zero exit on bad input.

# Behaviours

- `password-gen --length 16` prints a single line of 16 characters and exits 0.
- `password-gen --length 16 --seed 42` is deterministic — running it twice produces the same output.
- `password-gen --length 0` exits non-zero with a clear usage line on stderr.
- `password-gen --length -5` exits non-zero with a clear error.
- `password-gen --classes lower --length 50 --seed 1` produces a 50-character output containing only `[a-z]`.
- `password-gen --classes upper,digit --length 40 --seed 2` produces output containing only `[A-Z0-9]`.
- `password-gen --classes lower,upper,digit,symbol --length 30 --seed 3` produces output drawing from all four classes.
- `password-gen --help` (or `-h`) prints usage and exits 0.
- An unknown flag like `--bogus` exits non-zero with a usage line.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. `password-gen --length 16` — eyeball that the result is a plausible 16-character password (mix of cases / digits depending on default classes).
2. `password-gen --length 16 --seed 42` twice — confirm byte-identical outputs (deterministic-RNG smoke test).
3. `password-gen --length 1024` — runs cleanly, doesn't allocate absurdly. Wall time under ~1 second.
4. `password-gen` (no args) — prints usage hint and exits non-zero, doesn't crash or hang.
5. `password-gen --classes lower --length 100 --seed 9 | tr -d 'a-z' | wc -c` — should print `0` (or `1` for trailing newline) since no non-lowercase characters should appear.
6. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- password-gen --length 16
- password-gen --length 16 --seed 42
- password-gen --classes lower --length 20 --seed 1
- password-gen --length 0

---

**Cross-language note**: TS / Py / Go / Rust implementations should match the same deterministic-seed contract — the rubric runner verifies byte-equality of seeded outputs across languages, which catches RNG-implementation drift.

**Role**: §6 #5 (median tokens per pass) channel. Trivial-tier representative *with* a real algorithm. Smallest possible "the harness works at all" signal beyond `hello-cli`.
