---
title: json-pointer
tier: S
class: library-port
modules: [Stdlib.Json, Stdlib.AltJson, Stdlib.String, Stdlib.List]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: false
---

# Description

A JSON Pointer library — RFC 6901. Library implements the pointer grammar (`/foo/0/bar`, with `~0` and `~1` escapes for `~` and `/` literally) plus three operations: parse, evaluate (lookup), set (immutable update).

Target: `Darklang.JsonPointer` (Dark) / `json-pointer` (TS) / `jsonpointer` (Py) / `jsonpointer` (Go/Rust).

# Library API surface

- **Type**: `Pointer = List<Token>` where `Token = String` (already RFC-unescaped).
- **`parsePointer : String -> Result<ParseError, Pointer>`** — parses `/foo/~0bar/0` into `["foo", "~bar", "0"]` (note: index `0` stays as a string token; the evaluator decides if it's array or key).
- **`toString : Pointer -> String`** — round-trip back, re-escaping `~` and `/` correctly.
- **`evaluate : Pointer -> Json -> Option<Json>`** — `None` if any segment is missing.
- **`set : Pointer -> Json -> Json -> Result<Error, Json>`** — immutably set the value at the pointer; returns the new root JSON.

# Driver CLI

- `jp-cli parse <pointer>` — prints token list, exits 0 / 1.
- `jp-cli eval <pointer> < input.json` — reads JSON from stdin, prints the value at the pointer.
- `jp-cli set <pointer> <new-value-as-json> < input.json` — prints the modified JSON to stdout.

# Behaviours

- The 8 RFC-6901 standard cases: `""` (root), `/foo`, `/foo/0`, `/`, `/a~1b`, `/c%d`, `/e^f`, `/g|h` (the literal RFC examples).
- `jp-cli eval "/foo/0" < {"foo":["a","b"]}` prints `"a"`.
- `jp-cli eval "/foo/9" < {"foo":["a"]}` exits non-zero (out of range).
- `jp-cli eval "/foo" < {"bar":1}` exits non-zero (missing key).
- `jp-cli set "/foo/0" "\"newvalue\"" < {"foo":["a","b"]}` prints `{"foo":["newvalue","b"]}`.
- Escape: `jp-cli parse "/a~1b"` token list is `["a/b"]` (the `~1` decodes to `/`).
- Escape: `~01` decodes to `~1` (NOT `/`). RFC 6901 §3.
- Empty pointer `""` selects the whole document.
- Trailing slash `/foo/` is invalid (empty token after the slash) — exits non-zero on parse.
- `jp-cli --help` exits 0.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. RFC §5 examples: feed the standard JSON document `{"foo":["bar","baz"],"":0,"a/b":1,"c%d":2,"e^f":3,"g|h":4," ":5,"m~n":8}`. Run all 8 standard pointers. Each should match the RFC's expected results.
2. **Mutation test**: mentally substitute `evaluate` to ignore the `~0`/`~1` escapes; verify the rubric catches it via the `~01` test. If not, flag in `SUMMARY.md`.
3. `set` immutability: capture `< orig.json`, run `jp-cli set ...`, confirm `orig.json` is unchanged on disk (set returns the new root, doesn't mutate).
4. Source review for the **"feels right" criterion**: is `evaluate` a clean fold over the pointer? Should be. Note in `SUMMARY.md`.
5. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- jp-cli parse "/foo"
- echo '{"foo":[1,2,3]}' | jp-cli eval "/foo/0"
- jp-cli --help
