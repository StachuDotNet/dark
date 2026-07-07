# Darklang Surface Grammar

The authoritative description of the syntax accepted by the hand-written parser
(`LibParser.Parser`). When a "is this a bug or a decision?" question comes up,
the answer gets recorded here. The parser is the implementation; this is the
contract.

## Lexical structure

**Comments / trivia.** `// line`, `/// doc` (attaches to the next declaration as
its description), `////` and deeper are plain comments, `(* block *)` (nestable;
`(*)` is the multiply operator section, not a comment). All comments are
preserved on tokens as `leadingTrivia` — the token stream reconstructs the
source byte-exactly except trailing whitespace.

**Identifiers.** `[A-Za-z_][A-Za-z0-9_']*`. Backtick-quoted ``` ``name`` ```
permits anything up to the closing backticks. `___` is the blank identifier
(empty name). A leading `'` in type position lexes a type variable (`'a`,
`'TModel`); elsewhere `'…'` is a char literal.

**Integer literals.** Bare literals (`42`) are arbitrary-precision `Int`.
Suffixes select fixed-width types:

| suffix | type | | suffix | type |
|---|---|---|---|---|
| `y` | Int8 | | `uy` | UInt8 |
| `s` | Int16 | | `us` | UInt16 |
| `l` | Int32 | | `ul` | UInt32 |
| `L` | Int64 | | `UL` | UInt64 |
| `Q` | Int128 | | `Z` | UInt128 |
| `I` | Int (explicit; redundant) | | | |

A literal whose magnitude equals the type's `|MinValue|` (e.g. `128y`,
`9223372036854775808L`) is valid **only when negated** (`-128y`); bare use is an
out-of-range diagnostic, never a silent wrap.

**Float literals.** `digits[.digits][eE[±]exp]`. Lowered to exponent-free
whole/fraction decimal strings (`1e300` → `1` + 300 zeros).

**Strings.** `"…"` with escapes, `"""…"""` raw (no escapes), `$"…{expr}…"`
interpolated (`{{`/`}}` are literal braces; interpolation bodies are full
expressions, parsed with real source positions), `$"""…"""` raw-interpolated.
Escapes: `\n \t \r \a \b \v \f \\ \" \' \/ \0 \{ \}`, `\xHH`, `\XHHHH`,
`\uHHHH`, `\UHHHHHHHH` (must be a Unicode scalar — surrogates and > 0x10FFFF
are invalid). Invalid escapes are diagnostics. String content is NFC-normalized.

**Chars.** `'c'` — one extended grapheme cluster; escapes as in strings.

## Operators and precedence

Loosest to tightest; one table (`infixBindingPower`) drives the parser.

| level | operators | assoc | notes |
|---|---|---|---|
| 0 | `\|>` | left | pipe (below everything) |
| 1 | `\|\|` | left | |
| 2 | `&&` | left | |
| 3 | `== != < > <= >=` | left | `=` is **not** equality (binding only) |
| 4 | `@` | right | list append → `Stdlib.List.append` |
| 5 | `+ - ++` | left | `++` is string concat |
| 6 | `* / %` | left | |
| 7 | `^` | right | exponentiation (`2^3^2 = 2^(3^2)`) |
| 8 | application `f a b` | left | tightest |

Operator sections `(op)` are two-arg lambdas; `x |> (op) y` means `x op y`
(piped value is the *left* operand). Unary minus on a literal makes a negative
literal; on anything else it applies `Builtin.negate`. In argument position a
`-` glued to a number with a space before it is a negative-literal argument
(`f a -1` = `f a (-1)`; `f a - 1` = `(f a) - 1`) — F#'s rule.

## Offside (indentation) rules

One rule set for all inputs (no per-mode fork):

- Every statement/declaration/element anchors at its **start column**. A token
  on a new line *indented past the anchor* continues the current construct; at
  the anchor it starts a sibling; left of it, the enclosing construct ends.
- **Inside parentheses** the closing `)` is the real delimiter, so the anchor is
  *exact*: only a token at exactly the inner anchor column starts a new
  statement (paren bodies can be statement blocks); any other column — deeper
  *or shallower* — continues (`x |> (fn\n  args-dedented-below-callee)` works).
- Application arguments continue while past the *statement* anchor even when
  left of a far-right callee (`let r = xs |> List.fold\n  init\n  fn`).
- An operator on a new line continues the statement at-or-past the anchor —
  except `-`, which must be *past* it (`1L` then `-8L` at the anchor is two
  statements, not subtraction).
- `else`/`elif` bind to the `if` chain at-or-left of their column; `match` arms
  align on the first `|`'s column (a shallower `|` belongs to an outer match).

## Expressions

Literals, `()`, tuples `(a, b, …)`, lists `[a; b]` (`;`, `,`, or newline
separated), `Dict { k = v }`, records `Type { f = v }` / anonymous `{ f = v }`,
record update `{ r with f = v }`, field access `x.f` (lowercase step = field;
uppercase steps = module path), lambdas `fun p1 p2 -> body` (tuple patterns
allowed), `let pat = e` (with optional `in`; tuple/wildcard/unit patterns),
nested `let f (x: T) : R = …` (lowers to a let-bound lambda, types discarded),
`if/then/elif/else`, `match e with | pat [when g] -> body`, pipes, statement
sequences (newline or `;`).

**Enum constructors.** `Type.Case`, `Module.Type.Case`, or bare `Case`.
`Case(a, b)` / `Case (a, b)` — parenthesized commas are **fields** (`Pair(1,2)`
has two fields; `Pair((1,2))` has one tuple field). `Case()` is one unit field.
Space-application binds fields only in head position (`Ok 5`), never in argument
position (`f None x` keeps `None` nullary). A *bare* nullary uppercase name is
resolved contextually (variable / enum case / DB) at lowering.

**Match patterns.** Literals (incl. negative), `_`, variables, tuples, lists,
cons `h :: t` (right-assoc), or-patterns `p1 | p2`, enum patterns with
**unqualified** case names (`| Ok x`, never `| Result.Ok x` — rejected), unit.

**Generics.** `Name<T, …>` requires `<` *adjacent* to the name (spaced `<` is
comparison). Works on calls (`parse<Int64> x`), enum ctors
(`Type<T>.Case`), records (`Type<T> { … }`), and type declarations
(`type F<'a> = …`). `>>` closes two levels.

## Types

Primitives (`Unit Bool Int Int8…UInt128 Float Char String DateTime Uuid Blob` —
the one list is `WrittenTypes.primTypes`), `List<T>`, `Dict<T>`, `Stream<T>`,
`DB<T>`, type variables (`'a` or bare lowercase), tuples `A * B`, functions
`A -> B -> C` (flat multi-arg: args `[A; B]`, return `C` — *not* curried),
qualified custom types with args. Enum case fields separate with `*` at atom
level (`Case of A * B` = two fields; a tuple field needs parens `(A * B)`).

## Declarations

`let f (p: T) … : R = body` (function — parenthesized, annotated params;
`()` unit param), `let x = e` (value **inside a module**; at a file's top level
it is a let-*expression* sequencing with what follows), `val x = e` (always a
value), `type Name<'a> = …` (record `{ f: T; … }` / enum `| A | B of T`
— after the first case each case *requires* a leading `|` / alias), `module
A.B` header (wraps the file) or `module X =` (indented body). Modules nest;
the path builds the package location (owner.modules.name). `///` doc comments
become descriptions.

**Testfile dialect** (`parseTestFile` only): top level acts as a module body,
`actual = expected` is a test assertion (with `error "msg"` / `sqlerror "msg"`
expected forms), `[<DB>] type X = T` declares a user DB.

## Diagnostics

Diagnostics are structured: `{ code; severity; range; message; related; hint }`.
Codes are stable identifiers (key on these, never on message text):

| code | meaning |
|---|---|
| `PARSE-EXPECTED` | expected X, found Y |
| `PARSE-UNCLOSED` | missing closing delimiter — the opener is in `related` |
| `PARSE-ESCAPE` | invalid escape/codepoint in a literal |
| `PARSE-INT-RANGE` | integer literal out of range (hint: the negated form) |
| `PARSE-TOO-DEEP` | nesting beyond the recursion cap |
| `PARSE-UNEXPECTED` | stray token inside a construct |
| `PARSE-PIPE-SEGMENT` | pipe RHS isn't a valid segment |
| `PARSE-PATTERN` | invalid match pattern shape |
| `PARSE-INTERNAL-LOOP` | parser step budget exhausted — a parser bug, please report |
| `LEX` | tokenizer-level recovery (unterminated literal, …) |

`renderDiagnostic` renders code + position + message + a caret snippet +
related locations + hint; the CLI uses it. The LSP wire (`parserParseDiagnostics`,
range+message tuples) predates the structure — upgrading it to carry
code/related is a pending Dark-side type change.

## Error recovery

- Diagnostics carry ranges; "expected X, found Y"; unclosed delimiters point at
  their opener.
- Holes are explicit `EError`/`MPError` nodes (zero-width range at the failure
  point). Execution rejects trees with diagnostics; tooling (highlighting,
  hover) consumes recovered trees and paints around holes.
- Recovery never consumes closing/sync tokens or declaration starters
  (`let`/`type`/`val`), and a declaration keyword at-or-left of the current
  declaration's column terminates any construct inside it — a broken
  declaration cannot swallow the ones after it.
- Nesting beyond 200 levels abandons the parse with one diagnostic
  (stack safety); string-interpolation nesting beyond 64 levels likewise
  (each `{expr}` body parses recursively with fresh state); statement
  sequences are unbounded (parsed iteratively).
- A step budget (`4000 + 300·n` parse-entries for n tokens) backstops the
  per-loop progress guards: any future no-progress loop becomes a
  `PARSE-INTERNAL-LOOP` diagnostic instead of a hang.

## Architecture and the two lowerings

```
source ──InterpTokenizer──▶ tokens(+trivia) ──Parser──▶ WrittenTypes (ranges)
   ├─ F# WT2PT ──▶ ProgramTypes        (execution: packages, testfiles, CLI)
   └─ WTSerializer ──▶ Dark WrittenTypes ──Dark WT2PT──▶ PT   (tooling: LSP,
      highlighting, round-trip, formatter)
```

The two lowerings **deliberately diverge** (drift beyond this list is a bug):

| construct | F# WT2PT (execution) | Dark WT2PT (tooling) |
|---|---|---|
| `a ⏎ b` statement seq | `let _ = a in b` | kept as `EStatement` (round-trips) |
| `EError` hole | raises (never lowered) | `EVariable "__parseError__"` |
| ids | minted `gid()` | minted Dark-side |
