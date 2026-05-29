---
title: parser-combinators
tier: M
class: library-port
modules: [Stdlib.String, Stdlib.List, Stdlib.Result, Stdlib.Char]
languages: [dark, ts, py]
expected_outcome: stretch
known_blockers: []
framework_hint: null
core: true
---

# Description

A small library implementing parser combinators in the Elm tradition. Parsers are values of type `Parser<a>` — descriptions of how to consume some prefix of input and produce a value of type `a` (or fail with an error).

The library exposes a handful of primitive parsers and a handful of combinators for building bigger parsers from smaller ones. Crucially, parsers are *first-class values*: you can pass them around, store them, recurse through them. The whole point of the library is that grammars become *data*, not control flow.

The target module is `Darklang.Parser` (Dark) / `parser` (TS) / `parser` (Py). All three expose the same API surface and the same combinator semantics.

This is a port of [Elm's `parser`](https://package.elm-lang.org/packages/elm/parser/latest/). The agent should match Elm's API names where natural; use idiomatic equivalents where Elm's choices don't fit (e.g. Dark's pipe direction may differ).

The library is **`expected_outcome: stretch`** — not because the API is large, but because the type-system bookkeeping (higher-order functions over `Parser<a>`) is where Dark's type inference and enum syntax meet maximum stress. Library ports of this shape are the cleanest test we have for §3.2's type-system improvements.

# Library API surface

The agent must implement these public types and functions:

- **Type**: `Parser<a>` — abstractly, "a function that takes the remaining input and returns either Success(value, new remaining input) or Failure(reason, position)." The concrete representation is the agent's choice — function-typed, record-with-fn-field, ADT, etc. The library's *external API* is what the spec tests; the internal rep is the agent's call.
- **Type**: `ParseError = { message: String, position: Int64 }` — for failure reporting. Exposed at the API boundary.
- **`succeed : a -> Parser<a>`** — a parser that always succeeds, consuming nothing, yielding the given value.
- **`fail : String -> Parser<a>`** — a parser that always fails with the given message.
- **`map : (a -> b) -> Parser<a> -> Parser<b>`** — transform the success value.
- **`andThen : (a -> Parser<b>) -> Parser<a> -> Parser<b>`** — sequence; the second parser depends on the first's value.
- **`oneOf : List<Parser<a>> -> Parser<a>`** — try each parser in turn; succeed with the first one that succeeds.
- **`chompWhile : (Char -> Bool) -> Parser<Unit>`** — consume characters while the predicate holds; always succeeds, even if zero characters consumed.
- **`chompIf : (Char -> Bool) -> Parser<Unit>`** — consume exactly one character matching the predicate; fail if the next char doesn't match.
- **`getChompedString : Parser<a> -> Parser<String>`** — run a parser, then return the chunk of input it consumed (rather than the parser's value).
- **`run : Parser<a> -> String -> Result<ParseError, a>`** — execute a parser against an input string. Top-level entry point.

(Optional but encouraged: `lazy : (() -> Parser<a>) -> Parser<a>` for recursive grammars. Test it via the balanced-parens grammar below.)

# Driver CLI

`parser-cli` exposes 6 hand-written grammars built from the library. Each subcommand parses its argument and prints the result.

- `parser-cli int <s>` — parses an optional sign + digit-run. `parser-cli int 42` → `42`. `parser-cli int -7` → `-7`. `parser-cli int abc` → exit 1, print error.
- `parser-cli ident <s>` — parses an identifier (letter, then letters/digits/underscores). `parser-cli ident foo_bar1` → `foo_bar1`. `parser-cli ident 1abc` → exit 1.
- `parser-cli bool <s>` — parses literal `true` or `false`. `parser-cli bool true` → `true`. Anything else → exit 1.
- `parser-cli csv <s>` — parses comma-separated list of identifiers. `parser-cli csv a,b,c` → `[a, b, c]` (or any clear list rendering).
- `parser-cli pairs <s>` — parses `key=value` pairs separated by `;`. `parser-cli pairs a=1;b=2` → `[(a,1), (b,2)]`.
- `parser-cli parens <s>` — parses nested balanced parens, returns the depth. `parser-cli parens "(())"` → `2`. `parser-cli parens "(()"` → exit 1, error mentions unmatched.

# Behaviours (rubric tests these via parser-cli)

- `parser-cli int 42` prints `42`, exits 0.
- `parser-cli int -7` prints `-7`.
- `parser-cli int 0` prints `0`.
- `parser-cli int abc` exits 1 with an error mentioning expected-digit (or similar).
- `parser-cli ident foo` prints `foo`.
- `parser-cli ident foo_bar1` prints `foo_bar1`.
- `parser-cli ident 1abc` exits 1.
- `parser-cli ident ""` exits 1.
- `parser-cli bool true` prints `true`.
- `parser-cli bool false` prints `false`.
- `parser-cli bool yes` exits 1.
- `parser-cli csv a,b,c` prints `[a, b, c]` or equivalent list rendering.
- `parser-cli csv ""` prints `[]` (empty list — debatable; spec says explicitly: empty input → empty list, exit 0).
- `parser-cli pairs a=1;b=2;c=3` prints all three pairs.
- `parser-cli pairs a=1;b=` exits 1 (incomplete pair).
- `parser-cli parens "(())"` prints `2`.
- `parser-cli parens "()"` prints `1`.
- `parser-cli parens "((()))"` prints `3`.
- `parser-cli parens "(()"` exits 1 with an unmatched-paren error.
- `parser-cli parens ")("` exits 1 (starts with closing paren).

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Read the library source. Are combinators *composable* without contortions? Specifically, can you write `oneOf [int, ident, bool]` and have it typecheck without a type annotation? If the user has to write a per-call type, the library is dragging.
2. Read the `Parser<a>` representation. Idiomatic for the language? In Dark, expect either a function-type alias or a record with a function field. In TS, expect a class or a function with `.run()`. In Py, expect a callable class.
3. Read the `oneOf` and `andThen` implementations. Both must compose without leaking the underlying parser representation. (No `parser.unwrap()` calls visible at the call site.)
4. Run `parser-cli parens "(())"` — should work. Then deliberately introduce a left-recursion in the grammar (manual edit). Confirm it stack-overflows or runs forever, *not* silently succeeds. **Mutation test of the library's recursion handling.**
5. The library's own tests file (`tests/`, idiomatic location). Should cover the `andThen` chain at least 3 levels deep (e.g. `int |> andThen (\n -> chompIf ',' |> andThen (\_ -> int))` — pair-of-ints).
6. Record agent's own confidence + uncertainty in `SUMMARY.md`. The "feels right" criterion: does the library look like something you'd want to use in another project? If the agent needed 200 lines of internal helpers to expose 10 API functions, that's a code-smell — flag it.

# Smoke commands (pre-rubric sanity)

- parser-cli int 42
- parser-cli ident foo_bar
- parser-cli csv a,b,c
- parser-cli parens "(())"
- parser-cli int abc
- parser-cli --help

---

**Why this spec stresses Dark specifically**: parser combinators are higher-order functions all the way down. `andThen` takes a function returning a parser; `oneOf` takes a list of parsers; `map` lifts a function across a parser. **If Dark's type inference doesn't handle `Parser<a>` cleanly, this is where it breaks.**

**Role**: §6 #9 (rework ratio) channel — agents that fight Dark's types on this project rework heavily. **The "type-system polish" canary** for the §3 backlog.
