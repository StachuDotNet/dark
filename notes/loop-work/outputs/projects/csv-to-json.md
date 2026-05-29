# csv-to-json

**Goal:** Convert RFC-4180-style CSV from stdin into a single JSON array of objects on stdout, keyed by the header row.

**Kind:** greenfield

## Acceptance criteria
- [ ] Empty input produces stdout `[]` and exits 0; header-only input also produces `[]` and exits 0.
- [ ] `name,age\nAlice,30\nBob,25\n` produces `[{"name":"Alice","age":"30"},{"name":"Bob","age":"25"}]` (whitespace-insensitive; parseable by `jq`).
- [ ] A quoted field with an embedded comma is preserved (`"hello, world"` stays one field).
- [ ] A quoted field with an embedded newline is preserved as one field.
- [ ] An escaped double-quote inside a quoted field (`""`) decodes to a single `"`.
- [ ] Numeric-looking values stay JSON strings (`age` → `"30"`, never `30`); no auto-coercion to number/bool/null.
- [ ] CRLF (`\r\n`) line endings behave identically to LF.
- [ ] A row with fewer or more fields than the header exits non-zero with an error mentioning the row number.
- [ ] An unmatched quote (unterminated quoted field at end of input) exits non-zero with an error mentioning the line.
- [ ] Non-ASCII / UTF-8 content in headers and fields passes through cleanly.
- [ ] Output is a single JSON document (not NDJSON).
- [ ] `csv-to-json --help` prints usage and exits 0.
