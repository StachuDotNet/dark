# pastebin-upload

**Goal:** POST content to a pastebin service and return the resulting URL.

**Kind:** greenfield

## Acceptance criteria
- [ ] Uploads stdin/file content and prints the returned paste URL.
- [ ] Works against a mock/fixture endpoint so tests run offline.
- [ ] Retries and timeouts behave correctly on simulated failures.
- [ ] Required auth is read from environment variables, never hardcoded.
- [ ] A JSON shape change produces a typed error, not a crash.
- [ ] `--help` / `-h` prints usage and exits 0.
