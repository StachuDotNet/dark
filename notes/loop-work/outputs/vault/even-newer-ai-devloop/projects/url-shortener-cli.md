---
title: url-shortener-cli
tier: S
class: app
modules: [Stdlib.DB, Stdlib.Crypto, Stdlib.Cli, Stdlib.String]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: false
---

# Description

A command-line URL shortener. Two subcommands: `add` registers a URL and prints a 6-character slug; `get` looks up a slug and prints the original URL.

State persists between invocations. Each language solves persistence with its native idioms — Dark uses `Stdlib.DB` (the package-tree-backed store), TS uses something like `better-sqlite3` or a JSON file, Py uses `sqlite3` or shelve, Go uses `database/sql` + sqlite or a JSON file, Rust uses `rusqlite` or a JSON file.

The agent's choice of slug-derivation algorithm is open. Common choices: random, hash-of-URL, sequential-base62, or a hybrid. The rubric tests *behaviours*, not the algorithm.

This is the project that most directly differentiates Dark's "no DB setup" story from competing stacks. **The single most predictive Dark-wins-on-cost project** — TS/Py/Go/Rust agents typically spend tokens deciding which storage layer to use; Dark agents reach for `Stdlib.DB` immediately.

# Behaviours

- `url-shortener-cli add https://example.com` prints a 6-character slug (matching `[a-zA-Z0-9_-]{6}`) and exits 0.
- After `add`, the slug must persist across process boundaries: a *second* invocation of `url-shortener-cli get <slug>` (separate process) must print the original URL exactly.
- `url-shortener-cli get <unknown-slug>` exits non-zero with a clear "not found" error.
- `url-shortener-cli add not-a-url` (no scheme) exits non-zero with a "invalid URL" error.
- `url-shortener-cli add ""` (empty) exits non-zero.
- `url-shortener-cli add https://example.com` twice on the same URL — behaviour is implementation-defined, but **the rubric requires that both invocations succeed** (no error). Either same-slug-returned or new-slug-each-time is acceptable.
- Slug collisions are handled cleanly: if the slug-generation algorithm produces a collision against an existing different URL, the program must NOT silently overwrite. Either retry-with-new-slug or error are both acceptable.
- Slugs are URL-safe: produced slugs match `[a-zA-Z0-9_-]{6}` exactly.
- `url-shortener-cli list` (optional but encouraged) lists all stored mappings, one per line as `<slug>\t<url>`. If unimplemented, the rubric skips this test.
- `url-shortener-cli --help` exits 0 with usage.
- An unknown subcommand exits non-zero with usage.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. **The persistence test** (the load-bearing self-check):
   ```
   $ url-shortener-cli add https://darklang.com
   abc123
   $ url-shortener-cli get abc123
   https://darklang.com
   ```
   Now in a fresh process (separate shell invocation):
   ```
   $ url-shortener-cli get abc123
   https://darklang.com
   ```
   The second `get` (in a fresh process) is the actual persistence test. If it fails — e.g. state was kept in process memory only — the spec failed.
2. `url-shortener-cli add not-a-url` — must error and exit non-zero.
3. Add 100 distinct URLs in a tight loop, then list (or get each) — confirm all 100 are retrievable.
4. **Read the implementation.** For Dark: did the agent reach for `Stdlib.DB`, or did they roll their own file-backed store? Either is fine, but `Stdlib.DB` is the idiomatic choice — if not used, that's a §3.1 Discovery friction (the agent didn't find the right module). Note this in `SUMMARY.md`.
5. Force a slug collision (manual edit: pin the slug-generation to always return the same string). Re-run the test in step 3; confirm the program errors or retries cleanly, not silently overwrites.
6. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- url-shortener-cli add https://example.com
- url-shortener-cli get bogus
- url-shortener-cli add not-a-url
- url-shortener-cli --help

---

**Why this spec is the Dark differentiator**: per iter-43 core rationale, this is the project that most directly differentiates Dark vs TS/Py/Go/Rust. Other-language agents spend tokens choosing between sqlite, JSON-file, level-db, redis, etc. Dark agents reach for `Stdlib.DB` because it's the only obvious answer. **Watch §6 #5 (median tokens) and §6 #4 (trace adoption rate)**: this spec should show the largest token-gap-favoring-Dark of any project in the bench. **It's the one project where Dark winning is the *expected* outcome, not the stretch.**
