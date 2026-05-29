# paste-bin

**Goal:** Serve a paste service: POST text returns a slug, GET retrieves it with content-type negotiation, and DELETE removes it with the right token.

**Kind:** greenfield

## Acceptance criteria
- [ ] `POST /` with a text body returns an `[a-z0-9]{8}` slug and a `Location` header.
- [ ] `GET /<slug>` returns the stored text as `text/plain` by default.
- [ ] `GET /<slug>?html=1` returns syntax-highlighted HTML.
- [ ] `DELETE /<slug>` with the wrong token is rejected; with the right token it deletes the paste.
- [ ] After deletion, `GET /<slug>` returns 404.
- [ ] State persists across requests within the server's lifetime.
- [ ] `paste-bin --help` prints usage and exits 0.
