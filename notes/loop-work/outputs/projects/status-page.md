# status-page

**Goal:** Ping a list of URLs and render an HTML status page.

**Kind:** greenfield

## Acceptance criteria
- [ ] Renders an HTML page reflecting the up/down status of each monitored URL.
- [ ] A second process hitting the server on a random port gets the expected responses.
- [ ] Malformed JSON returns 400 with a readable body.
- [ ] State-mutating requests are idempotent where applicable.
- [ ] SIGTERM drains in-flight requests within ~1s.
- [ ] `--help` prints usage and exits 0.
