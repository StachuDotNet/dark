---
title: pretty-printer
tier: M
class: library-port
modules: [Stdlib.String, Stdlib.List, Stdlib.Int64]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: false
---

# Description

A pretty-printer library in the Wadler / Leijen tradition. Users build a `Doc` (a structural description of how text *might* be laid out) by composing primitive `Doc`s with combinators. A `render : Int -> Doc -> String` function then chooses the best layout that fits within a given line width, producing the actual rendered string.

The point of this style of pretty-printer is **width-aware composition**: the same `Doc` value renders differently at width 20 vs 80. Soft line breaks (`group`) collapse to spaces when the content fits and break when it doesn't. Indented blocks (`nest`) preserve their structure across line breaks.

The target module is `Darklang.Pretty` (Dark) / `pretty` (TS/Py/Go/Rust). Reference implementations: F# Fantomas's `PrettyPrint`, OCaml's `Format` module, Haskell's `prettyprinter` package.

This is genuinely useful infrastructure — once it lands, `dark show <hash>`, `dark review`, and other Dark CLI commands could lean on it for width-aware output. **One of the highest-leverage library ports in the catalog**: real internal users.

# Library API surface

The agent must implement these public types and functions:

- **Type**: `Doc` — abstract; the agent's choice of internal rep (ADT, recursive struct, etc.).
- **`text : String -> Doc`** — a literal string; never breaks.
- **`line : Doc`** — a soft line break that becomes either a single space (if grouped and fits) or a newline.
- **`hardLine : Doc`** — always becomes a newline; never collapses.
- **`empty : Doc`** — the empty doc; renders to "".
- **`concat : Doc -> Doc -> Doc`** — adjacent composition (no separator). Should be infix-friendly in idiomatic languages.
- **`concatSpace : Doc -> Doc -> Doc`** — concat with a space.
- **`concatLine : Doc -> Doc -> Doc`** — concat with a soft line.
- **`nest : Int64 -> Doc -> Doc`** — indent the doc by N spaces. Indentation is added after every line break inside the doc.
- **`group : Doc -> Doc`** — try to fit the doc on one line; if it fits within remaining width, soft lines become spaces; if not, soft lines become newlines.
- **`render : Int64 -> Doc -> String`** — render a doc to a string at the given max line width.
- **`vsep : List<Doc> -> Doc`** — concatenate with newlines between (sugar over folding `concatLine`).
- **`hsep : List<Doc> -> Doc`** — concatenate with spaces between (sugar over `concatSpace`).

# Driver CLI

`pretty-cli` exposes 6 hand-built fixture documents. Each subcommand renders one fixture at the requested width.

- **`pretty-cli simple <width>`** — fixture: `text "hello world"` rendered. Should always be exactly `hello world` regardless of width (no soft breaks possible).
- **`pretty-cli inline <width>`** — fixture: `group (text "a" <> line <> text "b" <> line <> text "c")`. At width ≥ 5: `a b c`. At width < 5: `a\nb\nc`.
- **`pretty-cli nested <width>`** — fixture: `text "outer" <> nest 2 (line <> text "inner1" <> line <> text "inner2")`. The "inner" lines indent by 2 spaces when broken.
- **`pretty-cli list <width>`** — fixture: a 5-element list rendered like `[a, b, c, d, e]` at wide widths and one-per-line at narrow widths. Tests `vsep` / `hsep`.
- **`pretty-cli record <width>`** — fixture: a record with 3 fields. Wide: `{name: alice, age: 30, role: admin}`. Narrow: each field on its own line, indented.
- **`pretty-cli deep <width>`** — fixture: 3 levels of nesting; tests that nested groups make independent fit-or-break decisions.

Reads: `pretty-cli <fixture> <width>` always reads the named fixture, renders at the given width, prints to stdout. No newline trailing the output unless the rendered doc ends in one.

# Behaviours (rubric tests these via pretty-cli)

- `pretty-cli simple 80` → `hello world` (no trailing newline).
- `pretty-cli simple 5` → `hello world` (still — `text` never wraps; only `line`/`group` produce wrapping).
- `pretty-cli inline 80` → `a b c`.
- `pretty-cli inline 4` → `a\nb\nc` (3 lines, joined by newlines).
- `pretty-cli nested 80` → `outer inner1 inner2` (or `outer\n  inner1\n  inner2` — depends on whether the agent chose `line` or `hardLine`; **both forms are accepted**, the rubric tests both shapes).
- `pretty-cli nested 10` → `outer\n  inner1\n  inner2` (indented by 2 when broken).
- `pretty-cli list 80` → `[a, b, c, d, e]` (single line).
- `pretty-cli list 10` → 5 lines, each list element on its own line, structurally clear (the exact rendering — brackets/commas/indents — is implementation-defined; the rubric tests for "one element per line" via line count).
- `pretty-cli record 80` → single line with all 3 fields.
- `pretty-cli record 20` → multiple lines, each field on its own line.
- `pretty-cli deep 80` → entire structure on one line (everything fits).
- `pretty-cli deep 30` → some groups break, others don't; the rubric checks that nested groups make independent decisions (i.e. a single line that's long doesn't force every nested group to break).
- `pretty-cli simple 0` → exits non-zero (zero-width is invalid).
- `pretty-cli simple -5` → exits non-zero.
- `pretty-cli unknown 80` → exits non-zero with "unknown fixture" error.
- `pretty-cli --help` → usage, exit 0.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Run `pretty-cli list 80` and `pretty-cli list 10` side-by-side — confirm wide is one-line, narrow is multi-line, both look readable.
2. Run `pretty-cli deep 30` repeatedly with widths 20, 25, 30, 35, 40 — observe a graceful degradation: at narrow widths everything breaks, at wide widths nothing does, with smooth transitions in between.
3. **Read the `render` implementation.** Is it tracking remaining-width correctly? The classic Wadler/Leijen algorithm is recursive with a "lookahead" decision — does the doc-rest fit on the current line? If the implementation greedy-renders without lookahead, certain narrow-width cases will look wrong. **Eyeball: at width 30, are individual lines staying ≤ 30 chars?** (Quick check: `awk '{ print length }'` — flag in `SUMMARY.md` if any line exceeds the requested width.)
4. **Mutation test**: replace `group` with the identity function (`group x = x`). Re-run `pretty-cli inline 4`. The rubric should fail — without `group`, soft lines never collapse. If it passes, the rubric isn't actually testing group's behaviour.
5. The "feels right" criterion: 12 API entries is a lot — does the library expose them cleanly? Or is it 12 functions all returning the same opaque object with no clear naming? Library-port pattern: API names should match Wadler/Leijen / Hughes-PJ conventions.
6. Performance: render a 1000-element `vsep` list at width 80; should be ~linear. If quadratic, `concat` is doing something subtle (eager string-flattening rather than lazy doc-tree building).
7. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- pretty-cli simple 80
- pretty-cli inline 4
- pretty-cli list 10
- pretty-cli simple 0
- pretty-cli --help

---

**Why this spec is the "real internal user" library port**: per iter 29, the highest-leverage library ports are ones that obsolete app-level reinvention. Pretty-printer would benefit `dark show <hash>`, `dark review`, `dark log` — every CLI command that displays structured data with width awareness. The bench measures this spec for token-cost / type-correctness; the longer-term value is the §3.6 review-tooling work that pretty-printer enables.
