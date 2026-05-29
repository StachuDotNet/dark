# csv

**Goal:** Provide an RFC 4180 CSV parser/writer library (row-shaped and header-keyed APIs) plus a CLI driver.

**Kind:** greenfield

## Acceptance criteria
- [ ] Exposes `parse`, `parseWithHeaders`, `write`, and `writeWithHeaders` with the documented signatures, plus a `ParseError = { line, column, message }` type.
- [ ] Handles the RFC-4180 corner cases: quoted commas, embedded newlines, escaped quotes (`""`), CRLF vs LF, unmatched quote, unterminated row.
- [ ] Round-trip: `parse(s) |> Result.map(write)` is byte-equal to `s` for canonical well-quoted inputs.
- [ ] `parseWithHeaders` rejects rows with mismatched column counts via a typed error (no silent corruption).
- [ ] `write` quotes fields containing `,`, `\n`, or `"` automatically, and does not over-quote fields that need no quoting.
- [ ] `writeWithHeaders` honors the explicit header order even when records have keys in different orders.
- [ ] Empty input yields empty output for `parse`; `parseWithHeaders` errors on empty input (no headers).
- [ ] `csv-cli parse` / `parse-headers` print rows/records as JSON; `csv-cli write` / `write-headers` emit CSV.
- [ ] `csv-cli --help` exits 0.
