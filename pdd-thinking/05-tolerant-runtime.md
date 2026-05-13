# 05 — Tolerant Runtime

> Stachu's directive: "we'll need a really tolerant runtime, especially at first. and tighten over time."

## What "tolerant" means

A normal runtime treats every error as a stop. PDD's runtime treats most errors as **opportunities to substitute a default and keep going**, recording the substitution in the trace.

Tolerance dimensions:

| Dimension | Strict | Tolerant |
|---|---|---|
| Unresolved name | error | materialize lazily; if budget expires, empty body |
| Materialization fails | error | substitute `default(returnType)` |
| Type mismatch at call | error | coerce if cheap (Int→Float, String→...); else default |
| Empty/null in op | error | empty propagates (List.head [] = None, not crash) |
| Division by zero | error | 0 (or NaN for floats) |
| Stack overflow | error | bail out, return default |
| Builtin missing | error | return `default(returnType)` |
| Pattern non-exhaustive | error | catch-all returns default |
| Async timeout | error | return default |

The tolerant runtime's hard rule: **anything that would have crashed instead substitutes a default and records it in the trace.** Eval keeps moving forward. The trace tells you what was substituted, and the user can decide whether to fix it.

## Why tolerance unlocks PDD

If the LLM generates a body that calls a fn with the wrong types, or returns the wrong type, or pattern-matches incompletely — a strict runtime stops dead. The user has to re-prompt, re-eval, see the next error, repeat.

A tolerant runtime keeps going to the *end* of the program. Now the user sees the *whole* program's trace and can iterate on whatever's wrong all at once. Way better feedback shape.

## Implementation — the RecoveryPolicy

From `02-libexecution-changes.md` we introduced:

```fsharp
type RecoveryPolicy =
  | KillFrame
  | EmptyBody
  | EmptyFrame
  | AskUser

recoveryPolicy : RTE.Error -> RecoveryPolicy
```

The "tolerant default" version of this policy:

```fsharp
let tolerantPolicy (rte : RTE.Error) : RecoveryPolicy =
  match rte with
  | RTE.MaterializationFailed _ -> EmptyBody
  | RTE.FnNotFound _ -> EmptyBody
  | RTE.TypeError _ -> EmptyBody
  | RTE.UncaughtException _ -> EmptyFrame   // bail one level up
  | RTE.DeprecatedItemHalted _ -> AskUser    // these mean something
  | RTE.MatchUnmatched _ -> EmptyBody
  | RTE.DivideByZero -> EmptyBody  // returns 0
  | _ -> KillFrame
```

The "strict default" is `_ -> KillFrame`.

A CLI flag `--tolerance strict|loose|debug` picks. Default `loose`. Tests use `strict`. `debug` is `AskUser` for everything, useful for interactive sessions.

## What does "EmptyBody" actually return?

Need a `Dval.defaultFor : TypeReference -> Dval`. Skeleton:

```fsharp
let rec defaultFor (t : TypeReference) : Dval =
  match t with
  | TUnit -> DUnit
  | TBool -> DBool false
  | TInt64 -> DInt64 0L
  | TFloat -> DFloat 0.0
  | TString -> DString ""
  | TChar -> DChar ""
  | TList _ -> DList(VT.unknown, [])
  | TDict _ -> DDict(VT.unknown, Map.empty)
  | TTuple(a, b, rest) ->
      DTuple(defaultFor a, defaultFor b, List.map defaultFor rest)
  | TOption _ -> DEnum(... "None" ...)   // None
  | TResult(_, _) -> DEnum(... "Ok" defaultFor ok ...)  // Ok default
  | TCustomType(name, _) ->
      // For records: empty record (all fields defaulted). For enums: first case.
      defaultForCustomType exeState name
  | TVariable _ -> DUnit  // fallback for unresolved generics
  | _ -> DUnit
```

This is straightforward but has to handle every type. **Most-common 80% first; "exotic" types fall through to DUnit and that's OK.**

## The trace tells you what substituted

Every recovery emits a trace event:

```
recovery  loc=Function(Package h7a8) policy=EmptyBody reason="MaterializationFailed" substituted=DUnit
```

When the user reviews the trace, the recoveries are highlighted. They're not invisible — they're like compiler warnings, surfaced after the fact.

## Tightening over time

The user starts with `--tolerance loose`. As confidence grows in specific fns (e.g., they've been materialized, reviewed, and committed to the package store), those fns get marked `[<Strict>]` and revert to crash-on-error behavior for *just those fns*.

This is the path to a normal program. PDD is *training wheels for development*: you start everything tolerant, and over time, more code gets locked down as "this is final, fail loudly if anything's wrong."

## What about silently producing wrong answers?

The biggest risk of a tolerant runtime: the program "succeeds" but produces nonsense. A division by zero returning 0 might propagate to an answer of "your bank balance is $0" when it should be "error."

Three mitigations:

### Mitigation 1 — Tolerance is opt-out, not just opt-in
Every recovery is visible in the trace. If the user's not looking at traces, they shouldn't trust the result. Train them to.

### Mitigation 2 — Quarantine recovered values
A `Dval` that came from a recovery is tagged. Operations on tagged values stay tagged. The final result tells the user "this answer involved N substitutions." Like NaN propagation, but for "made-up values."

```fsharp
and Dval =
  | DInt64 of int64
  | ...
  | DRecovered of Dval * RecoveryReason   // wrap
```

Or use a per-Dval tag bit. The user-facing output shows "you got 42, with 3 recovered values en route."

### Mitigation 3 — Eventually, the type system catches this
A strict-typed value can't be assigned from a recovered value without explicit acknowledgment. `val final: Strict<Int>; let final = foo(x)` errors if `foo(x)` recovered anywhere.

For PoC: do Mitigation 1 (it's free). Skip 2 and 3.

## When NOT to be tolerant

Tolerance is wrong for:
- **Side effects** — sending an email to the wrong person because a name lookup recovered to "" is bad. Builtins that do effects must opt out and use strict mode. (Tie in: `06-builtin-permissions.md`.)
- **Test runs** — tests should fail loudly. CI defaults to `--tolerance strict`.
- **Production**, eventually. Tolerant mode is a development affordance. We need a story for graduation.

## Tolerance in error reporting

A tolerated error is *still an error*. The trace shows it. The dev surface needs:
- A "show me the recoveries" command — `pdd recoveries <traceId>` lists every substitution.
- A "narrow the trace to just before the first recovery" — useful for iterating.
- A "re-run with strict tolerance, see what crashes" — diagnostic mode.

These are easy to build once the trace format is firm. See `08-tracing-as-artifact.md`.
