# webhook-echo

**Goal:** Accept POSTed webhooks and return the most recent received requests as JSON.

**Kind:** greenfield

## Acceptance criteria
- [ ] `POST /` stores the request body, headers, and a timestamp.
- [ ] `GET /` returns the last 10 received requests as JSON.
- [ ] A round-trip of several POSTs followed by a GET reflects the stored bodies byte-equal.
- [ ] Header lookup is case-insensitive.
- [ ] The returned tail is ordered most-recent-first.
- [ ] State persists across requests within the server's lifetime.
- [ ] `webhook-echo --help` prints usage and exits 0.
