# Type-providery parser generation in Dark

A speculative design for: "describe a grammar in Dark via DUs and records, and have something type-provider-shaped derive a working parser from it."

## Does the idea make sense?

Yes — and Dark's content-addressed package system actually makes it cleaner here than it would be in F#. The pitch in three lines:

- The grammar's **AST is just Dark types** — a DU describing the cases, records describing payloads.
- A **sidecar value** (or annotations adjacent to the type) describes concrete syntax: keywords, infix operators, precedence.
- A **generator** turns the pair into a runnable parser, ideally as a *derived package* whose hash is a function of the grammar's hash.

That last point is the Dark-specific twist. In F# a type provider runs at design time inside the compiler. In Dark, a "package transformer" can run at package-publish time inside the CLI, materializing a real, content-addressed package. Same idea, different host — and arguably a better fit.

## Three points on the design spectrum

These aren't exclusive; a real implementation picks one as the primary entry point and offers escapes to the others.

### 1. Pure datatype-directed (no annotations)

Take a DU, derive a parser mechanically. Works for canonical syntaxes only — JSON, s-expressions, a "constructor-call" form like `Add(IntLit(1), Mul(IntLit(2), IntLit(3)))`. You won't get `1 + 2 * 3` out of this without telling it more. Useful as a free fallback (cheap to implement, often good for debug serializers), not the main story.

### 2. DU + sidecar syntax record  *(the sweet spot)*

The AST is a normal Dark DU. A *separate* value of type `Syntax<Expr>` pairs each case with concrete syntax. The generator combines AST and sidecar into a parser.

This is the closest analogue to "type provider" in spirit: a generic mechanism that, given a type `T`, gives you a related type `Syntax<T>` and a function over both. The novel piece is having the type system understand `Syntax<T>` as "a record with one field per case of `T`, each typed by that case's payload."

### 3. Direct grammar DSL (FParsec-in-Dark)

Skip the type-derivation angle. Expose parser combinators in Dark and let users compose parsers by hand. Well understood, ergonomic, but doesn't deliver the type-provider flavor the question is reaching for. Almost certainly the *implementation layer* underneath flavor 2.

The interesting design (and the rest of this doc) is **flavor 2 on top of flavor 3**.

## What the F# / runtime side needs

I don't think this needs new F# language features. What it needs:

1. **A combinator parser library inside `LibExecution`** — the runtime workhorse. Either a thin wrapper over FParsec, or a small monadic parser hand-rolled to produce `Dval` values directly. Producing Dvals (not F# values) matters: the parser's output type isn't known statically — it's whatever Dark type the caller threaded through.

2. **TypeReference introspection** — already largely there. The runtime can resolve a `TypeReference` to its `PackageType` and walk cases/fields. The generator uses this to ask: "what cases does `Expr` have? what payload does `Add` carry? are any payload fields recursive into `Expr`?"

3. **A builtin (or stdlib fn) that bridges the two:**
   ```
   Parser.deriveFromType : TypeReference -> SyntaxSpec -> Parser
   ```
   At call time it walks the type, threads the syntax spec, and produces a closed-over parser function that yields Dvals of the right shape. Validation up-front: every case in the type must have a syntax entry; the spec can't refer to nonexistent cases.

4. **Optionally: a package transformer.** A function `Grammar -> ParserPackage` run at package-build time, emitting a new derived package. This is where Dark's content-addressed model earns its keep — the parser package's hash is a deterministic function of the grammar package's hash, so consumers always get a parser consistent with the grammar that produced it. F# type providers achieve consistency by living in the compiler; Dark achieves it through hashing.

5. **A small precedence-climbing helper.** Pure top-down combinators handle most things but wreck stack on left-recursion and operator chains. A Pratt-style helper that takes a list of precedence levels is worth building once and reusing — it's the piece that turns flavor-2 syntax specs into something that handles `1 + 2 * 3` correctly.

6. **The one piece that *might* be a real new feature: `Syntax<T>` as a type-level construct.** The cleanest version is a structural record type generated from the cases of `T`. That's a small but real addition to the type checker — essentially a generic conditional/derived type. The cheap version: skip the type-level magic and pass the spec as `List<(String, CaseSyntax)>` with names validated at parser-construction time. Pragmatic, less safe, no language change required. I'd ship the cheap version first.

## Defining a grammar — what the Dark source looks like

Two flavors, to taste. First, the **typed-sidecar** style:

```darklang
// the AST lives in your package as ordinary Dark types
type Expr =
  | IntLit of Int64
  | Var of String
  | Binop of Op * Expr * Expr
  | Paren of Expr

type Op = | Add | Sub | Mul | Div

// the syntax sidecar — one entry per case of Expr
let exprSyntax : Parser.SyntaxFor<Expr> =
  Parser.SyntaxFor {
    IntLit = Parser.Atom Parser.int64
    Var    = Parser.Atom Parser.ident
    Paren  = Parser.Between ("(", Parser.recurse, ")")
    Binop  = Parser.Precedence [
      [ Parser.InfixL "*" (fun l r -> Binop(Mul, l, r));
        Parser.InfixL "/" (fun l r -> Binop(Div, l, r)) ];
      [ Parser.InfixL "+" (fun l r -> Binop(Add, l, r));
        Parser.InfixL "-" (fun l r -> Binop(Sub, l, r)) ] ]
  }

let exprParser : Parser<Expr> =
  Parser.deriveFromType exprSyntax
```

Things to note:

- `Parser.SyntaxFor<Expr>` is the type-level magic — a structural record whose field names line up with `Expr`'s cases. If we don't grow that feature, this becomes a list-of-pairs and we lose the field-name-as-case-name guarantee.
- `Parser.recurse` is a placeholder the generator wires up to the rule being defined — same trick FParsec uses for forward references.
- The `Binop` entry is interesting: it consumes *all four* operator cases at once via the precedence layering, rather than splitting them across separate sidecar entries. The generator needs to recognize precedence layers as a fan-out across DU cases.

Second flavor, **declarative grammar value** — closer to BNF, no function bodies:

```darklang
let grammar : Grammar.G = Grammar.build [
  Grammar.rule "expr" (Grammar.choice [
    Grammar.case "IntLit" [Grammar.token Grammar.int64];
    Grammar.case "Var"    [Grammar.token Grammar.ident];
    Grammar.case "Paren"  [Grammar.lit "("; Grammar.ref "expr"; Grammar.lit ")"];
    Grammar.precedenceLayer [
      Grammar.infix Left "*" "Binop" "Mul";
      Grammar.infix Left "/" "Binop" "Div" ];
    Grammar.precedenceLayer [
      Grammar.infix Left "+" "Binop" "Add";
      Grammar.infix Left "-" "Binop" "Sub" ]])
]

let exprParser = Parser.fromGrammar<Expr> grammar
```

This sacrifices some type safety (case names are strings) but is *inspectable* — you can pretty-print it, fingerprint it, or generate it from an external `.bnf` file. Useful for tooling.

## Consuming a generated parser

The easy half:

```darklang
let result = Parser.run exprParser "1 + 2 * 3"

match result with
| Ok ast ->            // ast : Expr
    Eval.evaluate ast
| Error e ->
    Parser.formatError e
```

If you take the package-transformer route, the consumer surface gets even tighter, because the *derived* package can expose typed conveniences directly:

```darklang
match MyLang.Parser.parse "1 + 2 * 3" with
| Ok ast -> Eval.evaluate ast
| Error e -> Parser.formatError e
```

`MyLang.Parser` here is *not hand-written*. It's emitted at build time from `MyLang.Grammar`, lives in the package tree like any other module, and its hash is a deterministic function of the grammar's hash. To Dark's tooling it's indistinguishable from human-written code; only its provenance metadata says "derived."

## The package-transformer angle — the Dark-shaped answer

This is where Dark differs interestingly from F#:

- F# type providers run inside `fsc`. They synthesize types and methods that live mostly at compile time; at runtime they're erased or backed by the provider's runtime DLL.
- Dark has no compile/runtime split in the same shape, but it *does* have a publish boundary: the moment a package gets a content hash and becomes consumable from elsewhere.

A *package transformer* is a function the CLI knows how to run as part of `commit` / `publish`:

```
transform : grammar.MyLang  -->  parser.MyLang
```

The output is just another package. Two consequences:

1. **Cache coherence is automatic.** Grammar hash unchanged → derived hash unchanged → no rebuild. Grammar hash changed → derived hash changes → consumers see the mismatch and re-resolve. The same mechanism the package system already uses everywhere.

2. **The transformer is itself a Dark function.** Introspectable, testable, version-controlled, replaceable. Want a different parser-generation strategy? Swap the transformer. F# type providers don't have this — they're DLLs supplied by the library author and that's the end of it.

The practical upshot: the "type provider" piece doesn't have to be magic burned into LibExecution. It can be a regular Dark function that uses runtime type introspection. You only need *one* magic builtin underneath: a Dval-producing parser interpreter that can be driven by a grammar-shaped value.

## Limits and open problems

Things that won't fall out for free:

- **Error messages.** Generated parsers produce structurally correct errors but rarely *good* ones. Hand-written parsers have custom recovery and contextual messages. The derivation needs hooks for "when this rule fails at this position, say X."
- **Whitespace and comments.** Cross-cutting; don't fit per-case syntax. Probably needs a top-level `lexerConfig` adjacent to the grammar.
- **Disambiguation.** If two cases of a DU have overlapping leading tokens, the derived parser needs ordering or longest-match rules. Easy to specify, easy to get wrong silently. Probably want an analyzer that flags overlap at derivation time.
- **Performance.** Interpreting a grammar Dval at every parse step is a lot of dictionary lookup. The package-transformer route lets you compile once and reuse; the runtime-derive route doesn't, and may need a memoization layer to be acceptable.
- **Left recursion.** Standard combinator hazard. Pratt-style precedence layers handle the common operator case; arbitrary left recursion needs more (GLL, packrat-with-seed, or a grammar rewrite step).
- **`Syntax<T>` as a real type-level feature.** Skippable but the alternative (string keys) is a real ergonomic and safety hit. Worth doing eventually, not for v1.

## TL;DR

Yes, implementable, and Dark is actually a *better* host for it than F# is. Minimum viable version: one runtime builtin (Dval-producing parser interpreter) plus a Dark stdlib module that builds parsers from types and a sidecar spec. Maximum version: a package-transformer step that emits derived parser packages, plus a structural `Syntax<T>` in the type system. At the maximum it stops feeling like a parser library and starts feeling like a language feature — which is the whole point of the type-provider analogy.
