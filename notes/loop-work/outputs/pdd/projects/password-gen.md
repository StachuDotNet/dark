# password-gen

**Goal:** Generate a random password of a requested length and character-class mix, deterministically when seeded.

**Kind:** greenfield

## Acceptance criteria
- [ ] `password-gen --length 16` prints a single line of 16 characters and exits 0.
- [ ] `password-gen --length 16 --seed 42` is deterministic — running it twice produces the same output.
- [ ] `password-gen --length 0` exits non-zero with a clear usage line on stderr.
- [ ] `password-gen --length -5` exits non-zero with a clear error.
- [ ] `password-gen --classes lower --length 50 --seed 1` produces a 50-character output containing only `[a-z]`.
- [ ] `password-gen --classes upper,digit --length 40 --seed 2` produces output containing only `[A-Z0-9]`.
- [ ] `password-gen --classes lower,upper,digit,symbol --length 30 --seed 3` produces output drawing from all four classes.
- [ ] A large length (e.g. `--length 1024`) runs cleanly under ~1 second without absurd allocation.
- [ ] An unknown flag (e.g. `--bogus`) exits non-zero with a usage line.
- [ ] `password-gen --help` (or `-h`) prints usage and exits 0.
