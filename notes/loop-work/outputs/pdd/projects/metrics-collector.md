# metrics-collector

**Goal:** Accept posted metrics and expose them in a Prometheus-like text format.

**Kind:** greenfield

## Acceptance criteria
- [ ] Accepts metric submissions and serves an aggregated text dump.
- [ ] A second process hitting the server on a random port gets the expected responses.
- [ ] Malformed JSON returns 400 with a readable body.
- [ ] State-mutating requests are idempotent where applicable.
- [ ] SIGTERM drains in-flight requests within ~1s.
- [ ] `--help` prints usage and exits 0.
