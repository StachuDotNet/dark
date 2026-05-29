---
title: csv
tier: S
class: library-port
modules: [Stdlib.String, Stdlib.List, Stdlib.Result, Stdlib.Dict]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: false
---

# Description

A CSV (RFC 4180) parser + writer library. Implements the core grammar: quoted fields, embedded commas, embedded newlines, escaped double-quotes (`""`).

Two pairs of API functions: `parse` / `write` work with `List<List<String>>` (raw rows); `parseWithHeaders` / `writeWithHeaders` work with `List<Dict<String, String>>` (header-keyed records).

Target: `Darklang.Csv` (Dark) / `csv` (TS/Py/Go/Rust). Py/Go can wrap stdlib `csv` modules; Rust can wrap the `csv` crate; the spec wants a *typed API*, not raw stdlib usage.

# Library API surface

- **Type**: `ParseError = { line: Int, column: Int, message: String }`.
- **`parse : String -> Result<ParseError, List<List<String>>>`** — raw rows, no header semantics.
- **`parseWithHeaders : String -> Result<ParseError, List<Dict<String, String>>>`** — first row = headers; subsequent rows become records.
- **`write : List<List<String>> -> String`** — emits RFC-4180 with proper quoting.
- **`writeWithHeaders : List<String> -> List<Dict<String, String>> -> String`** — explicit header order + records.

# Driver CLI

- `csv-cli parse < input.csv` — prints rows as JSON `[[...], [...]]`.
- `csv-cli parse-headers < input.csv` — prints records as JSON `[{...}, {...}]`.
- `csv-cli write < input.json` — input is `[[...]]`-shaped, output is CSV.
- `csv-cli write-headers <header-comma-list> < input.json` — records to CSV with explicit header order.

# Behaviours

- The 8 RFC-4180 corner cases (from `csv-to-json` Phase-1 spec): quoted commas, embedded newlines, escaped quotes (`""`), CRLF vs LF, unmatched quote, unterminated row, etc.
- Round-trip: `parse(s) |> Result.map(write)` should byte-equal `s` for canonical (well-quoted) inputs.
- `parseWithHeaders` rejects rows with mismatched column counts (typed error, not silent NaN).
- `write` quotes fields containing `,`, `\n`, or `"` automatically.
- `write` does *not* quote fields that don't need it (no over-quoting that bloats output).
- Header-order in `writeWithHeaders` is honored even when records have keys in different orders.
- Empty input → empty output for `parse`; `parseWithHeaders` errors on empty input (no headers).
- `csv-cli --help` exits 0.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. **Cross-spec interop**: feed `csv-cli parse-headers < /tmp/sample.csv` into `csv-cli write-headers <headers> < <previous-output>` — confirm round-trip CSV is canonical.
2. **Mutation test**: mentally substitute `parse` with `String.split(',')` for fields. Re-run with quoted-comma input; rubric should catch the corruption (same pattern as `csv-to-json`'s self-verification step 4). If not, flag in `SUMMARY.md`.
3. Source review: idiomatic record types, clean separation between row-shape and dict-shape APIs. Note "feels right" criterion in `SUMMARY.md`.
4. **The big payoff**: with this library shipped, agents tackling the `csv-to-json` Phase-1 spec can `import Darklang.Csv` instead of hand-rolling. **Re-run csv-to-json with this library available** — token cost should drop materially (the iter-61 caveat about hand-rolling RFC-4180 evaporates).
5. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- echo "a,b\n1,2" | csv-cli parse
- echo "a,b\n1,2" | csv-cli parse-headers
- csv-cli --help

---

**Cross-spec note**: this library *retires* the `no-csv-stdlib` blocker on `csv-to-json` (Phase-1 spec). When `csv` ships, csv-to-json's `known_blockers: [no-csv-stdlib]` should clear, and Dark's token cost on csv-to-json should drop into parity with TS/Py/Go/Rust. **The bench captures this as a longitudinal signal**: csv-to-json's token-cost-over-time chart becomes a metric of "did the csv library port help?"
