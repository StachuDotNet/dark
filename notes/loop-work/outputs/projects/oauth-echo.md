# oauth-echo

**Goal:** Complete an OAuth dance and print the resulting token, as a dev tool.

**Kind:** greenfield

## Acceptance criteria
- [ ] Runs the redirect/callback flow and prints the obtained token.
- [ ] A second process hitting the server on a random port gets the expected responses.
- [ ] Malformed JSON returns 400 with a readable body.
- [ ] State-mutating requests are idempotent where applicable.
- [ ] SIGTERM drains in-flight requests within ~1s.
- [ ] `--help` prints usage and exits 0.
