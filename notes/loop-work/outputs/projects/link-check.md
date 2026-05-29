# link-check

**Goal:** Walk a markdown tree, HEAD every URL, and report dead links.

**Kind:** greenfield

## Acceptance criteria
- [ ] Reports each URL's status and flags dead links.
- [ ] Works against a mock/fixture endpoint so tests run offline.
- [ ] Retries and timeouts behave correctly on simulated failures.
- [ ] Required auth is read from environment variables, never hardcoded.
- [ ] A JSON shape change produces a typed error, not a crash.
- [ ] `--help` / `-h` prints usage and exits 0.
