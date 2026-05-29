# webhook-relay

**Goal:** Accept incoming webhooks and fan them out to registered subscribers.

**Kind:** greenfield

## Acceptance criteria
- [ ] Forwards each received webhook to all registered subscriber endpoints.
- [ ] A second process hitting the server on a random port gets the expected responses.
- [ ] Malformed JSON returns 400 with a readable body.
- [ ] State-mutating requests are idempotent where applicable.
- [ ] SIGTERM drains in-flight requests within ~1s.
- [ ] `--help` prints usage and exits 0.
