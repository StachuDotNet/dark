---
title: redis-driver
tier: M
class: app
modules: [Stdlib.Cli]
languages: [dark, ts, py, go, rust]
expected_outcome: fail-likely
known_blockers: [no-raw-sockets]
framework_hint: null
core: false
---

# Description

A small Redis client library that speaks RESP-2 (Redis serialization protocol) over a TCP socket. The CLI exposes `GET`, `SET`, `DEL`, `EXISTS` against a running Redis at `localhost:6379`.

The point is **socket-level networking**: opening a TCP connection, sending a length-prefixed command frame, parsing a length-prefixed response. RESP is the simplest meaningful binary protocol — text-friendly, well-documented, the canonical "first protocol you implement when learning sockets."

For TS, the natural implementation is `node:net` to open a socket + RESP serialization by hand, or `redis` / `ioredis` packages. For Py, `socket.socket` + RESP, or the `redis` package. For Go, `net.Dial` + RESP, or `github.com/redis/go-redis`. For Rust, `std::net::TcpStream` + RESP, or the `redis` crate. **For Dark today, no raw socket primitive exists** — `Stdlib.HttpClient` does HTTP only; `Stdlib.HttpServer` listens but isn't a general-purpose socket interface. There's no equivalent of `net.connect("localhost", 6379)`.

This is the *deepest* gap of the 5 expected-to-fail specs. The agent has fewer workaround paths than for archives or JWTs — `Process.spawn redis-cli` is essentially the only option. **`expected_outcome: fail-likely` is *generous*** — `fail-known` would be defensible.

# Behaviours

- `redis-driver SET foo bar` connects to `localhost:6379`, runs `SET foo bar`, prints `OK`, exits 0.
- `redis-driver GET foo` returns `bar`, exits 0.
- `redis-driver GET nonexistent` returns `(nil)`, exits 0 (Redis convention; not an error).
- `redis-driver EXISTS foo` returns `1`, `redis-driver EXISTS nonexistent` returns `0`.
- `redis-driver DEL foo` returns `1` (number of keys deleted).
- Multiple commands in sequence (e.g. SET then GET) reflect the connection's state.
- Command with binary-unsafe value: `redis-driver SET foo "with\nnewline"` — the newline is preserved on read-back.
- `--host` and `--port` flags override the default.
- Connection failure (no Redis running) exits non-zero with a clear "connection refused" message.
- `redis-driver --help` exits 0.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Start a local Redis: `docker run -p 6379:6379 redis:7-alpine` (or `brew services start redis`).
2. `redis-driver SET hello world` → expect `OK`.
3. `redis-cli GET hello` (the official Redis CLI, *not* the artifact) → should return `world`. **External-verifier interop check.**
4. `redis-driver GET hello` (artifact) → also returns `world`. Round-trip clean.
5. `redis-driver SET binary "$(printf 'a\\x00b')"` — set a value containing a null byte. `redis-driver GET binary | xxd` should show the null byte preserved. (RESP is binary-safe; agent must not lose bytes via String operations.)
6. Stop Redis. `redis-driver GET anything` should produce a clean "connection refused" error, not a panic / stack trace.
7. **For Dark specifically**: examine the source. The agent will almost certainly:
   - (a) Try and fail at the `connect to socket` step — Dark exposes no such primitive. Compile or import will error.
   - (b) Shell out to `redis-cli` via `Stdlib.Cli.Process.exec` — workaround. Functions but cedes the language-level claim entirely; `redis-driver` is now a thin wrapper over `redis-cli`.
   - (c) Honestly report: "Dark has no raw sockets; this requires `Stdlib.Tcp` or similar. Returning a stub."
8. Bench the round-trip latency: SET 1000 small keys then GET them all. Real Redis libraries do this in tens of milliseconds; a `redis-cli` fork-per-command approach takes seconds. *Wall-clock illuminates the workaround penalty.*
9. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- redis-driver --help
- redis-driver SET foo bar
- redis-driver GET foo
- redis-driver GET nonexistent-key

---

**Why this is `fail-likely` (and arguably `fail-known`)**: Dark has zero socket primitives. `HttpClient` is HTTP-shaped, not generic. `HttpServer` listens but isn't a generic TCP interface. There's no `Stdlib.Tcp`, no `Stdlib.Net`, no `Stdlib.Socket`. The agent's only path is shelling out — and shelling out to `redis-cli` is no longer "implementing a Redis driver"; it's "writing a thin process-spawning shim."

**The longitudinal value**: when Dark adds raw socket support, this spec flips. **Bigger picture**: socket support unlocks a massive category of work — not just Redis but Postgres, MQTT, raw HTTP/2, custom protocols. This spec's gap-closure is one of the highest-leverage Dark improvements measurable on the bench.

**Cross-spec note for the catalog Class M**: Redis-direct, Postgres-direct, WebSocket, MQTT, port-scanner, netcat-lite — *all* blocked by the same `no-raw-sockets` blocker. Once Dark adds sockets, ~6 catalog projects move from `fail-known` to `pass`. The bench captures this as a multi-spec gap-closure event in the time series.
