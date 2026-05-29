# parser-combinators

**Goal:** Provide an Elm-style parser-combinator library where parsers are first-class `Parser<a>` values, plus a CLI driver running several hand-written grammars.

**Kind:** greenfield

## Acceptance criteria
- [ ] Exposes `succeed`, `fail`, `map`, `andThen`, `oneOf`, `chompWhile`, `chompIf`, `getChompedString`, and `run` with the documented signatures.
- [ ] Exposes a `ParseError = { message: String, position: Int64 }` type at the API boundary.
- [ ] `parser-cli int 42` prints `42`, `int -7` prints `-7`, `int 0` prints `0`, and `int abc` exits 1 with an expected-digit error.
- [ ] `parser-cli ident foo_bar1` prints `foo_bar1`; `ident 1abc` and `ident ""` exit 1.
- [ ] `parser-cli bool true` prints `true`, `bool false` prints `false`, and `bool yes` exits 1.
- [ ] `parser-cli csv a,b,c` prints the three-element list; `csv ""` prints the empty list and exits 0.
- [ ] `parser-cli pairs a=1;b=2;c=3` prints all three pairs; `pairs a=1;b=` exits 1 (incomplete pair).
- [ ] `parser-cli parens "(())"` prints `2`, `parens "()"` prints `1`, `parens "((()))"` prints `3`.
- [ ] `parser-cli parens "(()"` exits 1 with an unmatched-paren error; `parens ")("` exits 1.
- [ ] Combinators compose without leaking the internal parser representation at call sites.
- [ ] `parser-cli --help` exits 0.
