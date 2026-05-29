# gh-stars

**Goal:** List a user's starred GitHub repos, filterable by language.

**Kind:** greenfield

## Acceptance criteria
- [ ] Lists starred repositories and filters them by language.
- [ ] A GitHub token is read from the environment when needed.
- [ ] Works against a mock/fixture endpoint so tests run offline.
- [ ] Retries and timeouts behave correctly on simulated failures.
- [ ] Required auth is read from environment variables, never hardcoded.
- [ ] A JSON shape change produces a typed error, not a crash.
- [ ] `--help` / `-h` prints usage and exits 0.
