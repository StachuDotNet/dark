# short-url-resolver

**Goal:** Follow HTTP redirects to show a short URL's final destination and the redirect chain.

**Kind:** greenfield

## Acceptance criteria
- [ ] Prints the final resolved URL and the full redirect chain.
- [ ] Works against a mock/fixture endpoint so tests run offline.
- [ ] Retries and timeouts behave correctly on simulated failures.
- [ ] Required auth is read from environment variables, never hardcoded.
- [ ] A JSON shape change produces a typed error, not a crash.
- [ ] `--help` / `-h` prints usage and exits 0.
