# json-pointer

**Goal:** Provide an RFC 6901 JSON Pointer library (parse, evaluate, immutable set) plus a CLI driver.

**Kind:** greenfield

## Acceptance criteria
- [ ] Exposes `parsePointer`, `toString`, `evaluate`, and `set` with the documented signatures; a pointer is a list of already-unescaped tokens.
- [ ] All 8 standard RFC 6901 cases work against the RFC's example document (`""`, `/foo`, `/foo/0`, `/`, `/a~1b`, `/c%d`, `/e^f`, `/g|h`).
- [ ] `jp-cli eval "/foo/0"` on `{"foo":["a","b"]}` prints `"a"`.
- [ ] `jp-cli eval "/foo/9"` on `{"foo":["a"]}` exits non-zero (out of range).
- [ ] `jp-cli eval "/foo"` on `{"bar":1}` exits non-zero (missing key).
- [ ] `jp-cli set "/foo/0" "\"newvalue\""` on `{"foo":["a","b"]}` prints `{"foo":["newvalue","b"]}`; `set` returns a new root and does not mutate the input.
- [ ] Escapes decode correctly: `/a~1b` → token `a/b`, and `~01` → `~1` (not `/`).
- [ ] The empty pointer `""` selects the whole document.
- [ ] A trailing slash (`/foo/`) is invalid and exits non-zero on parse.
- [ ] `jp-cli --help` exits 0.
