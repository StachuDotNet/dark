Build a small HTTP key-value service. `PUT /kv/<key>` stores the request body as the value and answers 201 when the key is new, 200 when it replaced a value. `GET /kv/<key>` answers the value, or 404. `DELETE /kv/<key>` answers 204, or 404 when there was nothing to delete. `GET /kv` answers a JSON object mapping every key to the length of its value in bytes. A key is 1 to 64 characters of letters, digits, `-` and `_`; any other key answers 400. Values live in memory only.

Write automated tests for the request handling that run with one command, and also try the running server.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** HTTP server, JSON API, with tests
- **Areas:** HTTP server, routing, JSON, dicts, state, tests of handlers
- **Network sites:** none (localhost only)
- **Seed:** none

## Acceptance checks

Start it on port 18085.

1. `PUT /kv/a` with body `hello` answers 201; again with `world` answers 200; `GET /kv/a` answers `world`.
2. `GET /kv/missing` answers 404; `DELETE /kv/a` answers 204, then 404.
3. `PUT /kv/bad%20key` and a 65-character key answer 400.
4. After `PUT /kv/x` (`abc`) and `PUT /kv/y` (`12345`), `GET /kv` is `{"x":3,"y":5}` (key order free).
5. The tests run with one command, pass, and call the handlers without a network port (Dark: package tests that `test run` finds).
