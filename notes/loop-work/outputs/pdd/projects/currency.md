# currency

**Goal:** Convert an amount between currencies using a live exchange-rate API.

**Kind:** greenfield

## Acceptance criteria
- [ ] Converts a given amount from one currency to another at the current rate.
- [ ] Works against a mock/fixture endpoint so tests run offline.
- [ ] Retries and timeouts behave correctly on simulated failures.
- [ ] Required auth is read from environment variables, never hardcoded.
- [ ] A JSON shape change produces a typed error, not a crash.
- [ ] `--help` / `-h` prints usage and exits 0.
