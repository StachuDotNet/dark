Build a small HTTP JSON API that listens on a port given on the command line (default 8080). `GET /health` answers `{"status": "ok"}`. `GET /add?a=2&b=3` answers `{"sum": 5}`. If `a` or `b` is missing or not a whole number, it answers status 400 with `{"error": "..."}` explaining what is wrong. Any other path answers 404.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** tiny
- **Kind:** HTTP server, JSON API
- **Areas:** HTTP server, JSON, strings
- **Network sites:** none (localhost only)
- **Seed:** none

## Acceptance checks

Start the server on port 18080 the way the agent's summary says.

1. `curl -s localhost:18080/health` returns JSON whose `status` is `"ok"`, with status 200 and a JSON content type.
2. `curl -s 'localhost:18080/add?a=2&b=3'` returns JSON whose `sum` is 5.
3. `curl -s 'localhost:18080/add?a=-7&b=3'` gives `sum` -4.
4. `curl -si 'localhost:18080/add?a=x&b=3'` and `'localhost:18080/add?a=1'` both give status 400 and a JSON body with an `error` field.
5. `curl -si localhost:18080/nope` gives 404.
