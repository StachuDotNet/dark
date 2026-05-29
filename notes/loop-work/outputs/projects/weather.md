# weather

**Goal:** Look up current weather and a short forecast for a location.

**Kind:** greenfield

## Acceptance criteria
- [ ] Shows current conditions plus a multi-day forecast for a queried location.
- [ ] Works against a mock/fixture endpoint so tests run offline.
- [ ] Retries and timeouts behave correctly on simulated failures.
- [ ] Required auth is read from environment variables, never hardcoded.
- [ ] A JSON shape change produces a typed error, not a crash.
- [ ] `--help` / `-h` prints usage and exits 0.
