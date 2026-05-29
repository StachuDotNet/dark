---
title: csv-to-json
tier: S
class: app
modules: [Stdlib.String, Stdlib.Json, Stdlib.List, Stdlib.Cli]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: [no-csv-stdlib]
framework_hint: null
core: false
---

# Description

A command-line tool that converts RFC-4180-style CSV from stdin into a JSON array of objects on stdout. The first row of input is treated as the header; subsequent rows become objects keyed by the header columns.

The program must handle the standard RFC-4180 quirks: fields can be quoted with `"…"`, quoted fields can contain commas and embedded newlines, double-quotes inside a quoted field are escaped as `""`. Both `\r\n` and `\n` line endings are accepted on input.

Output is a JSON array — one object per non-header row — emitted as a *single* JSON document on stdout (not NDJSON). The output should be parseable by `jq`. Field values are always rendered as JSON strings (never auto-coerced to number / bool / null).

Reads no other files. Stdout receives the JSON; stderr receives errors.

# Behaviours

- Empty input → stdout `[]`, exit 0. (Empty CSV is not an error.)
- Header-only input (`name,age\n`) → `[]`, exit 0.
- Simple input `name,age\nAlice,30\nBob,25\n` → JSON `[{"name":"Alice","age":"30"},{"name":"Bob","age":"25"}]` (whitespace-insensitive comparison; `jq` should parse it).
- Quoted field with embedded comma: `name,note\nAlice,"hello, world"\n` → `[{"name":"Alice","note":"hello, world"}]`.
- Quoted field with embedded newline: `name,bio\nAlice,"line1\nline2"\n` → `[{"name":"Alice","bio":"line1\nline2"}]`.
- Quoted field with embedded escaped quote: `name,quote\nAlice,"she said ""hi"""\n` → `[{"name":"Alice","quote":"she said \"hi\""}]`.
- Numeric-looking values stay as strings: `name,age\nAlice,30` → `"age":"30"` (NOT `"age":30`). The rubric tests this explicitly because it's a common over-helpfulness trap.
- CRLF line endings (`\r\n`) work identically to LF.
- Inconsistent column counts (a row with fewer fields than the header) → exit non-zero with an error mentioning row number.
- More fields than the header → exit non-zero with the same kind of error.
- An unmatched quote (e.g. `name\n"unterminated\n` end-of-input mid-quoted-field) → exit non-zero, error mentions the line.
- Input with non-ASCII / UTF-8 in fields and headers passes through cleanly.
- `csv-to-json --help` prints usage and exits 0.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. `printf 'name,age\\nAlice,30\\n' | csv-to-json | jq .` — output should pretty-print as a 1-element array. `jq` not erroring is the smoke test.
2. `printf 'a,b\\n"x,y","p\\nq"\\n' | csv-to-json | jq .` — exercises the comma-in-quoted-field and newline-in-quoted-field cases.
3. `printf 'name,age\\nAlice\\n' | csv-to-json` (one missing column) — must error with a non-zero exit, error mentions row 2.
4. Read the implementation. The CSV parser is hand-rolled (Dark has no CSV stdlib per `known_blockers`); confirm it didn't use `String.split(',')` as the parser, which would silently break on quoted commas. **Mutation test**: mentally substitute a naive `split(',')` parser; confirm the rubric's quoted-comma case fails — proves the rubric catches the lazy implementation. Flag in `SUMMARY.md` if the rubric *wouldn't* catch it.
5. Pipe a large CSV (10K rows) through; confirm linear-ish performance, not O(n²).
6. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- printf 'name,age\nAlice,30\n' | csv-to-json
- printf 'a\n' | csv-to-json
- echo '' | csv-to-json
- csv-to-json --help

---

**Why this spec stresses Dark specifically**: Dark has no CSV stdlib (project-survey §2 + iter 28 caveat). The agent must hand-roll RFC-4180 escaping. TS and Py have well-known CSV libraries (`csv-parse` / `csv` module); Go has `encoding/csv`; Rust has the `csv` crate. The cross-language comparison here is *implementation-effort* — Dark agents write a parser, others call a library. Watch §6 #5 (median tokens) and §6 #11 (followup-edit cost): Dark should run hot on this spec specifically because of the no-stdlib gap. **Once the `csv` library port (#23 in the catalog) ships, agents can `dark import` it instead — then this spec becomes language-fair.**
