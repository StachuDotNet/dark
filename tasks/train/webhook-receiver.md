Build an HTTP service that receives signed webhooks. `POST /webhook` carries a JSON body and a header `X-Signature: sha256=<hex>`, where `<hex>` is the HMAC-SHA256 of the exact body bytes using a shared secret read from the `WEBHOOK_SECRET` environment variable. A request with a missing or wrong signature answers 401 and is not stored. A valid one is appended to a log file as one JSON line and answers 202. `GET /events?type=<t>` returns the stored events whose `type` field equals `<t>` (all of them without the parameter), as a JSON array, oldest first.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** HTTP server, JSON API
- **Areas:** HTTP server, JSON, strings, files, permissions; also crypto and environment variables
- **Network sites:** none (localhost only)
- **Seed:** none

## Acceptance checks

Start it on port 18082 with `WEBHOOK_SECRET=s3cret`. Sign with `printf '%s' "$BODY" | openssl dgst -sha256 -hmac s3cret -hex | sed 's/.* //'`.

1. `BODY='{"type":"push","n":1}'` sent with the right signature answers 202.
2. The same body with signature `sha256=00` answers 401; with no header, 401.
3. A second valid event `{"type":"issue","n":2}` answers 202.
4. `GET /events` returns both in order; `GET /events?type=issue` returns only the second.
5. The log file has exactly two lines, each valid JSON.
6. A body with an extra space (`{"type":"push", "n":1}`) signed over its own exact bytes is accepted: the signature is checked on the raw body, not a re-serialized one.
