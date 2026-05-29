---
title: pcg-random
tier: S
class: library-port
modules: [Stdlib.Int64, Stdlib.UInt64, Stdlib.List, Stdlib.Float]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: false
---

# Description

A PCG (Permuted Congruential Generator) random number library. PCG is a small, fast, statistically-strong family of RNGs with a clean stateful-but-pure API: a `Generator` value carries the state, each call produces a new generator alongside the random output. Threading the state through the program is explicit — there's no hidden global.

Reference: [PCG paper / pcg-random.org](https://www.pcg-random.org/). For Dark, an LCG (linear congruential, simpler) is acceptable as a first pass — the spec is about the *API shape* (immutable state-threading) more than which specific algorithm is implemented.

Target: `Darklang.Random` (Dark) / `pcg-random` (TS/Py/Go/Rust).

# Library API surface

- **Type**: `Generator` — opaque wrapper over the RNG state.
- **`seed : Int64 -> Generator`** — deterministic from a seed.
- **`nextInt : Int -> Int -> Generator -> (Int, Generator)`** — pulls a random integer in `[lo, hi]`. Returns the value AND a new Generator.
- **`nextFloat : Generator -> (Float, Generator)`** — value in `[0.0, 1.0)`.
- **`nextBool : Generator -> (Bool, Generator)`**.
- **`shuffle : List<a> -> Generator -> (List<a>, Generator)`** — Fisher-Yates shuffle.
- **`sample : List<a> -> Generator -> Option<(a, Generator)>`** — pick one element. None if list is empty.

# Driver CLI

- `pcg-cli seq <seed> <count>` — generate `count` random Int64s in `[0, 99]` from seed; print one per line.
- `pcg-cli float <seed> <count>` — same, but floats.
- `pcg-cli shuffle <seed> <comma-separated-list>` — shuffle the list deterministically.
- `pcg-cli bool <seed> <count>` — print `true`/`false` lines.

# Behaviours

- **Determinism**: `pcg-cli seq 42 100` produces the *same* 100 lines every time.
- **Cross-language determinism**: Dark's `pcg-cli seq 42 100` and TS/Py/Go/Rust's should match byte-for-byte. **This is the cross-language fairness test**.
- `pcg-cli shuffle 1 a,b,c,d,e` is stable for seed 1; different seed produces different permutation.
- `pcg-cli bool 42 1000` produces ~50% true / ~50% false (within 5%).
- `nextInt 0 99` distribution: 1000 samples should bin roughly evenly across 100 buckets (chi-squared test passes).
- Seed 0 is valid (no special-case crash).
- `pcg-cli seq 42 0` produces no output, exits 0.
- `pcg-cli --help` exits 0.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Determinism: `pcg-cli seq 42 100 > a.txt; pcg-cli seq 42 100 > b.txt; diff a.txt b.txt` — should be identical.
2. **Cross-language**: the rubric requires byte-equality of seeded output across Dark/TS/Py/Go/Rust — agents that picked different RNG variants will diverge here. Force-pick a *named* algorithm (PCG-XSH-RR-32 or LCG with stated constants) and document the algorithm + constants in `SUMMARY.md`.
3. **Mutation test**: mentally substitute `nextInt` with a hard-coded `(0, gen)`. Re-run `pcg-cli seq 42 10` — should print 10 zeros. The rubric's distribution test should catch this. If not, flag in `SUMMARY.md`.
4. Speed: `pcg-cli seq 42 1000000 | wc -l` — should print "1000000" within a couple of seconds. Catches O(n²) implementations.
5. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- pcg-cli seq 42 5
- pcg-cli shuffle 1 a,b,c,d,e
- pcg-cli --help
