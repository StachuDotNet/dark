---
title: parallel-downloader
tier: M
class: app
modules: [Stdlib.HttpClient, Stdlib.List, Stdlib.Cli]
languages: [dark, ts, py, go, rust]
expected_outcome: fail-likely
known_blockers: [no-async-primitives]
framework_hint: null
core: false
---

# Description

A command-line tool that downloads multiple URLs concurrently and writes each to a file. The user passes a list of URLs (one per line via stdin or a file). The program fetches them in parallel — at most N concurrent requests, default 10 — and writes each response body to a file named after the URL's last path segment (or a hash if the path is degenerate).

The point of the project is **parallelism**: 10 URLs that each take 200 ms should complete in ~200 ms total, not 2000 ms. The rubric verifies this with a wall-clock check.

For TS, the natural implementation is `Promise.all(urls.map(fetch))` with a concurrency limiter. For Py, `asyncio.gather` with `aiohttp` or threads with `concurrent.futures.ThreadPoolExecutor`. For Go, goroutines + a buffered channel. For Rust, `tokio::join!` or `futures::stream::buffer_unordered`. **For Dark, no idiomatic implementation exists today** — `Stdlib.HttpClient` is synchronous, and Dark has no async/await/threads/Process pool. An agent could approximate via `Stdlib.Cli.Process.spawn` (per project-survey workaround) shelling out to `curl` in N background processes, but that's a hack, not the idiomatic answer the spec asks for.

# Behaviours

- `parallel-downloader urls.txt` reads URLs from `urls.txt` (one per line), downloads each, writes to `./<basename>` files. Exits 0.
- `cat urls.txt | parallel-downloader` reads URLs from stdin (no positional arg).
- `parallel-downloader --concurrency 5 urls.txt` caps concurrency at 5 simultaneous requests.
- **Wall-clock parallelism check**: 10 URLs each backed by a 200 ms-delay test server should complete in 250–500 ms total (with overhead, but materially less than 2000 ms). The rubric times the run and asserts.
- HTTP errors (4xx, 5xx) are reported per-URL on stderr but do not abort the rest.
- `parallel-downloader --help` exits 0.
- Empty input (no URLs) exits 0 with no output.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Stand up a tiny test server that sleeps 1s before responding (`python -m http.server` plus a sleep middleware, or `nc -l 8080` per request).
2. Generate 10 URLs pointing at the test server.
3. Run `time parallel-downloader urls.txt`. **Expected wall: ~1.1 s** (one second of work, ~100 ms overhead). If wall is closer to 10 s, the implementation is sequential, not parallel — *that's the failure mode this spec documents*.
4. **For Dark specifically**: examine the source. Did the agent reach for `Process.spawn`-based fake-parallelism (the workaround), or honestly report that Dark can't do this idiomatically and produce a sequential implementation? **Either is acceptable for the rubric** — sequential will fail the wall-clock test (correctly), Process.spawn might pass it (also correctly, capturing a workaround). The agent's commentary in `SUMMARY.md` should explain which approach was taken and why.
5. Run with concurrency=1 — should take 10× longer than concurrency=10. Confirms the cap is honored.
6. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- echo "https://example.com" | parallel-downloader
- parallel-downloader --help
- parallel-downloader /no/such/file

---

**Why this is `fail-likely`**: Dark has no async/await, no threads, no `Promise.all`, no `Parallel.map`. The agent has three options, all imperfect:
1. **Sequential implementation** — passes the rubric's correctness checks (URLs downloaded, files written), fails the wall-clock parallelism check.
2. **`Process.spawn`-based fake-parallelism** — shells out to `curl` in N background processes, polls for completion. May pass the wall-clock check; arguably "Dark can do this" but in a way that cedes the "better than bash" claim (per project-survey §M workaround note).
3. **Honest "this can't be done idiomatically in Dark"** — the agent reports the gap in `SUMMARY.md` and produces a sequential fallback. This is the most informative outcome: the bench captures both the failure *and* the agent's diagnosis.

**The longitudinal value**: the day Dark adds async primitives (per [improvements.md known-runtime-gaps](../improvements.md#known-runtime-gaps-out-of-3-scope-but-worth-tracking)), this spec flips to passing on the wall-clock check. The bench detects the gap-closure automatically. **Without this spec, "Dark added async" is a feature; with this spec, it's a measured improvement on a real workload.**
