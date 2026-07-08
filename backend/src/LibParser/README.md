# LibParser

The hand-written recursive-descent parser for Darklang source. One lexer,
one parser, one syntax tree — this is the only parser in the system
(tree-sitter and the FCS-based parser are gone).

**[GRAMMAR.md](GRAMMAR.md) is the spec for the grammar it accepts** — operator
precedence, offside rules, dialect decisions, and diagnostic codes.

## Pipeline

```
source
  │  Lexer.tokenize                  tokens + trivia (comments preserved;
  │                                  byte-exact source reconstruction)
  ▼
  │  Parser.parse / parseTestFile    range-complete WrittenTypes tree +
  │                                  structured diagnostics (never throws,
  │                                  never dies; recovers with EError holes)
  ▼
  ├─ WrittenTypesToProgramTypes      execution lowering → ProgramTypes
  │                                  (package loading, testfiles, CLI)
  └─ WrittenTypesToDarkTypes         tooling path → Dark WrittenTypes (Dvals) →
     (Builtins.Language)             Dark WT2PT (LSP, highlighting, round-trip)
```

The two lowerings are kept in agreement by a differential test
(`Tests/WrittenTypesLoweringParity.Tests.fs`) that compares their ProgramTypes
output over the real package corpus; they must be identical (node ids aside).

## Files

- `Tokenizer.fs` — shared token/position types (`Token`, `Pos`, `TokenRange`)
- `Lexer.fs` — the lexer: tokens with ranges, doc comments, trivia
- `Parser.fs` — the parser: `ParserState` + module-level parse functions;
  offside scope stack; recovery; structured `Diagnostic` + `renderDiagnostic`
- `WrittenTypes.fs` — the syntax tree (every node carries source ranges) plus
  the normalized package IR
- `WrittenTypesToProgramTypes.fs` — execution lowering + name resolution glue
- `NameResolver.fs` — name → FQName resolution against the package manager
- `Package.fs` — package-file entrypoint used by the loader
- `TestModule.fs` — testfile (`.dark` test dialect) harness

## Testing

- `Tests/LibParser.Tests.fs` — structure, offside, types, literals,
  recovery, golden diagnostics, fuzzer, range invariants, and the corpus gate
  (every real package file must parse cleanly)
- `Tests/WrittenTypesLoweringParity.Tests.fs` — F# vs Dark lowering agreement
- `Tests/LibParser.RoundTrip.Tests.fs` — source → PT → pretty-print round-trips
