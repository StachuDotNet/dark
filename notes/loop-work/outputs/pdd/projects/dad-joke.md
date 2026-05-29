# dad-joke

**Goal:** Fetch and print a random joke or trivia item from a public API.

**Kind:** greenfield

## Acceptance criteria
- [ ] Prints a fetched joke/trivia item.
- [ ] Works against a mock/fixture endpoint so tests run offline.
- [ ] Retries and timeouts behave correctly on simulated failures.
- [ ] Required auth is read from environment variables, never hardcoded.
- [ ] A JSON shape change produces a typed error, not a crash.
- [ ] `--help` / `-h` prints usage and exits 0.
