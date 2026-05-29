# static-file-server

**Goal:** Serve a directory of files over HTTP with MIME detection.

**Kind:** greenfield

## Acceptance criteria
- [ ] Serves files from a directory with correct content types and 404s for missing paths.
- [ ] A second process hitting the server on a random port gets the expected responses.
- [ ] Malformed JSON returns 400 with a readable body.
- [ ] State-mutating requests are idempotent where applicable.
- [ ] SIGTERM drains in-flight requests within ~1s.
- [ ] `--help` prints usage and exits 0.
