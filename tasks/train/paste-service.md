Build a small pastebin web service.

- `POST /pastes` with JSON `{"text": ..., "expires_in_seconds": ...}` stores a paste and answers 201 with its id and URL. `expires_in_seconds` is optional (default one day, maximum 30 days). Text over 64 KB is refused with 413.
- `GET /pastes/<id>` answers the paste as JSON; `GET /p/<id>` shows it as an HTML page with the text safely escaped; `GET /p/<id>/raw` answers plain text. An expired or unknown paste answers 404.
- `GET /pastes?recent=N` lists the N most recent unexpired pastes (id, first line, created time).
- Each client IP may create at most 5 pastes per minute; the sixth answers 429 with a `Retry-After` header.
- Pastes survive a restart. Expired pastes are removed from storage within a minute of expiring, without needing a request.

Organise the code in separate parts (storage, HTTP handling, rate limiting, rendering) and include tests for the rate limiter, expiry and HTML escaping. The port is a command-line argument.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** larger (a few modules, about 200+ lines)
- **Kind:** HTTP server, JSON API, persistence, background job; multi-module project
- **Areas:** HTTP server, JSON, dicts, lists, strings, files, permissions, tests, concurrency (background expiry)
- **Network sites:** none (localhost only)
- **Seed:** none

## Acceptance checks

Start it on port 18083.

1. `POST /pastes` with `{"text": "hello\n<b>x</b>"}` answers 201 with an id; `GET /pastes/<id>` returns the text; `/p/<id>/raw` returns it as plain text.
2. `/p/<id>` is HTML and shows `&lt;b&gt;`, not a bold tag.
3. A 70 KB text answers 413.
4. Six quick POSTs: the sixth answers 429 with `Retry-After`.
5. A paste with `expires_in_seconds: 2`: after 3 seconds `GET` answers 404, and within 70 seconds it is gone from the storage file or folder, with no request made in between.
6. `GET /pastes?recent=2` returns the two newest unexpired pastes with their first lines.
7. Restart the server: the unexpired pastes are still there.
8. The code has separate parts for storage, HTTP, rate limiting and rendering; the tests run with one command and pass.
