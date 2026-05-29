# age

**Goal:** Compute an age in years/months/days from a birth date (e.g. `age 1988-07-14`).

**Kind:** greenfield

## Acceptance criteria
- [ ] Computes years, months, and days correctly across month/year boundaries.
- [ ] An invalid or future date errors cleanly.
- [ ] Produces correct output for at least 5 canonical inputs.
- [ ] Bad input exits non-zero with a readable error on stderr.
- [ ] Handles empty, very long, and non-ASCII input without crashing.
- [ ] `--help` / `-h` prints usage and exits 0.
