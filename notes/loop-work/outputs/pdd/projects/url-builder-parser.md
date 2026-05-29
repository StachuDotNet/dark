# url-builder-parser

**Goal:** Provide an Elm-`Url`-style typed library that parses URL strings into structured values and builds well-formed URLs from parts, plus a CLI driver.

**Kind:** greenfield

## Acceptance criteria
- [ ] Exposes a typed `Url` record (protocol, host, optional port, path segments, query dict, optional fragment) and a `ParseError` type.
- [ ] Exposes `parse`, `toString`, `Url.Builder.crossOrigin`, `Url.Builder.relative`, and `Url.Parser.matchPath` with the documented signatures.
- [ ] `url-cli parse https://example.com/foo?a=1&b=2#section` extracts protocol, host, path, query, and fragment correctly.
- [ ] IPv6 hosts parse correctly: `https://[::1]:8080/path` yields host `[::1]` and port 8080.
- [ ] Percent-encoded paths round-trip: `parse("/foo%20bar") |> toString` ends with `/foo%20bar` (not `/foo bar`), with no double-encoding of already-encoded values.
- [ ] A query param with no value (`?key`) is `{key: ""}`, not omitted; an empty fragment (`#`) is `Some("")` while a missing fragment is `None`.
- [ ] `url-cli build crossOrigin example.com api/users name=alice` produces `https://example.com/api/users?name=alice`.
- [ ] `url-cli match "/users/:int" /users/42` extracts `42` as an int; `match "/users/:int" /users/foo` exits non-zero.
- [ ] A malformed URL (`http:/`) returns a typed `ParseError`, not a panic.
- [ ] Round-trip of canonical inputs through `parse | toString` is byte-equal (modulo query-key ordering).
- [ ] `url-cli --help` exits 0.
