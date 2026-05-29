---
title: url-builder-parser
tier: S
class: library-port
modules: [Stdlib.String, Stdlib.Dict, Stdlib.Result, Stdlib.List]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: false
---

# Description

A URL builder + parser library, port of [Elm's `Url`](https://package.elm-lang.org/packages/elm/url/latest/). Two complementary halves: a **parser** that turns strings into structured `Url` values, and a **builder** that turns parts into well-formed URL strings.

The library handles standard URL components — protocol, host, port, path segments, query params, fragment — plus the gnarly bits: percent-encoding, IPv6 host literals, query-param ordering for round-trip stability, fragment escaping.

Target module: `Darklang.Url` (Dark) / `url` (TS/Py/Go/Rust). Py implementations may wrap stdlib `urllib.parse`, but the spec wants a typed API surface, not raw stdlib.

# Library API surface

- **Type**: `Url = { protocol, host, port?: Int, path: List<String>, query: Dict<String, String>, fragment?: String }`
- **Type**: `ParseError` — typed errors for invalid input.
- **`parse : String -> Result<ParseError, Url>`** — string in, structured Url out.
- **`toString : Url -> String`** — round-trip back to string. `parse(s).map(toString)` should byte-equal `s` for canonical inputs (modulo query-key ordering).
- **`Url.Builder.crossOrigin : (host: String, path: List<String>, query: Dict<String, String>) -> String`** — builds an absolute URL from parts.
- **`Url.Builder.relative : (path: List<String>, query: Dict<String, String>) -> String`** — builds a relative path-with-query.
- **`Url.Parser.matchPath : List<PathPattern> -> Url -> Option<Match>`** — pattern-matches the URL's path against a list of path patterns. Patterns: `s "users"` (literal), `int` (capture int), `string` (capture string).

# Driver CLI

- `url-cli parse <url-string>` — prints structured fields, exits 0 on parse success / 1 on failure.
- `url-cli build crossOrigin <host> <path-segs-pipe-separated> <key=val,...>` — `url-cli build crossOrigin example.com api/v1/users name=alice`.
- `url-cli build relative <path> <query>` — relative variant.
- `url-cli match <pattern-spec> <url>` — `url-cli match "/users/:int" /users/42` prints `{userId: 42}`.

# Behaviours

- `url-cli parse https://example.com/foo?a=1&b=2#section` extracts protocol/host/path/query/fragment correctly.
- IPv6 hosts: `https://[::1]:8080/path` parses with port=8080, host=`[::1]`.
- Percent-encoded paths round-trip: `parse("/foo%20bar") |> toString` ends with `/foo%20bar` (not `/foo bar`).
- Query params with no value: `?key` is `query={key: ""}`, not omitted.
- Empty fragment (`#`) is `fragment = Some("")`; missing fragment is `fragment = None`.
- `url-cli build crossOrigin example.com api/users name=alice` → `https://example.com/api/users?name=alice`.
- `url-cli match "/users/:int" /users/42` extracts `42` as Int. `url-cli match "/users/:int" /users/foo` exits non-zero (foo isn't an int).
- Malformed URL (`http:/`) returns a typed ParseError, not a panic.
- `url-cli --help` exits 0.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Round-trip 10 URLs through `parse | toString`; eyeball byte-equality (modulo query ordering).
2. Read source: is `Url` an idiomatic record/struct? **"Feels right" criterion** per iter-58. Note in `SUMMARY.md`.
3. Edge case: query with `=` inside the value: `?json=%7B%22a%22%3A1%7D` — should keep the encoded form, not double-encode.
4. **Mutation test**: mentally substitute the parser's `parse` to use String.split instead of proper percent-decoding; verify the round-trip rubric catches the regression. If the rubric *wouldn't* catch it, flag in `SUMMARY.md`.
5. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- url-cli parse https://example.com/foo
- url-cli build crossOrigin example.com api a=1
- url-cli match "/users/:int" /users/42
- url-cli --help
