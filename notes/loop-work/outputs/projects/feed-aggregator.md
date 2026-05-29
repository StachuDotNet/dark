# feed-aggregator

**Goal:** Poll several RSS-like feeds and expose the merged items as JSON.

**Kind:** greenfield

## Acceptance criteria
- [ ] Fetches multiple feeds on a schedule and serves a merged JSON item list.
- [ ] A second process hitting the server on a random port gets the expected responses.
- [ ] Malformed JSON returns 400 with a readable body.
- [ ] State-mutating requests are idempotent where applicable.
- [ ] SIGTERM drains in-flight requests within ~1s.
- [ ] `--help` prints usage and exits 0.
