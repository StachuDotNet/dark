# redis-driver

**Goal:** Provide a Redis client speaking RESP-2 over a TCP socket, exposing `GET`/`SET`/`DEL`/`EXISTS` via a CLI.

**Kind:** greenfield

## Acceptance criteria
- [ ] `redis-driver SET foo bar` connects to `localhost:6379`, runs the command, prints `OK`, exits 0.
- [ ] `redis-driver GET foo` returns `bar` and exits 0.
- [ ] `redis-driver GET nonexistent` returns `(nil)` and exits 0 (Redis convention, not an error).
- [ ] `redis-driver EXISTS foo` returns `1`; `EXISTS nonexistent` returns `0`.
- [ ] `redis-driver DEL foo` returns the number of keys deleted (`1`).
- [ ] Commands in sequence reflect the connection's state (e.g. SET then GET).
- [ ] Binary-unsafe values round-trip: a value containing a newline or null byte is preserved on read-back.
- [ ] `--host` and `--port` flags override the defaults.
- [ ] Connection failure (no Redis running) exits non-zero with a clear "connection refused" message.
- [ ] Interop check: a value written by `redis-driver` is readable by the official `redis-cli`, and vice versa.
- [ ] `redis-driver --help` exits 0.
