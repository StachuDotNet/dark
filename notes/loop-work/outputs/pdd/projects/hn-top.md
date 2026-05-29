# hn-top

**Goal:** Fetch and pretty-print Hacker News top stories.

**Kind:** greenfield

## Acceptance criteria
- [ ] Lists the top stories with titles and links from the HN API.
- [ ] Works against a mock/fixture endpoint so tests run offline.
- [ ] Retries and timeouts behave correctly on simulated failures.
- [ ] Required auth is read from environment variables, never hardcoded.
- [ ] A JSON shape change produces a typed error, not a crash.
- [ ] `--help` / `-h` prints usage and exits 0.
