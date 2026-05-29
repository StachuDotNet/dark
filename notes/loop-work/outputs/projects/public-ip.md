# public-ip

**Goal:** Report the machine's public IP, optionally with geolocation.

**Kind:** greenfield

## Acceptance criteria
- [ ] Prints the public IP, optionally with location info.
- [ ] Works against a mock/fixture endpoint so tests run offline.
- [ ] Retries and timeouts behave correctly on simulated failures.
- [ ] Required auth is read from environment variables, never hardcoded.
- [ ] A JSON shape change produces a typed error, not a crash.
- [ ] `--help` / `-h` prints usage and exits 0.
