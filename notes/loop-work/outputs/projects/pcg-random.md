# pcg-random

**Goal:** Provide a seeded, immutable-state-threading random-number-generator library (PCG, or a named LCG) plus a CLI driver with deterministic, cross-language-reproducible output.

**Kind:** greenfield

## Acceptance criteria
- [ ] Exposes a `Generator` type and `seed`, `nextInt`, `nextFloat`, `nextBool`, `shuffle`, and `sample`; each value-producing call returns a new generator alongside the value.
- [ ] `pcg-cli seq 42 100` produces the same 100 lines every time (determinism).
- [ ] Seeded output is byte-identical across language implementations of the same named algorithm (cross-language fairness test).
- [ ] `pcg-cli shuffle 1 a,b,c,d,e` is stable for a given seed and changes with a different seed.
- [ ] `pcg-cli bool 42 1000` produces roughly 50% true / 50% false (within 5%).
- [ ] `nextInt 0 99` over 1000 samples bins roughly evenly across the 100 buckets.
- [ ] Seed 0 is valid (no special-case crash).
- [ ] `pcg-cli seq 42 0` produces no output and exits 0.
- [ ] Generating a large sequence (e.g. 1,000,000 values) completes in a couple of seconds (no O(n²) behavior).
- [ ] `pcg-cli --help` exits 0.
