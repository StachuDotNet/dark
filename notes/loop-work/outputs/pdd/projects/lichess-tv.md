# lichess-tv

**Goal:** Show the currently running top Lichess games.

**Kind:** greenfield

## Acceptance criteria
- [ ] Lists the current featured/top games from the Lichess API.
- [ ] Works against a mock/fixture endpoint so tests run offline.
- [ ] Retries and timeouts behave correctly on simulated failures.
- [ ] Required auth is read from environment variables, never hardcoded.
- [ ] A JSON shape change produces a typed error, not a crash.
- [ ] `--help` / `-h` prints usage and exits 0.
