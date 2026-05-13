# 04 — Signature Consensus

> When two parallel materializations race on the same pending handle, they might produce different signatures. What do we do?

## The problem in one example

The interpreter sees `foo(x)` in a generated body. We don't know `foo`'s sig.

- The **find** task searches the corpus, finds `Stdlib.List.foo: List<'a> -> 'a` — assumes `x: List<'a>`.
- The **generate** task asks the LLM, which writes `let foo (x: Int): String = ...` — assumes `x: Int`.

Both finish. Both produce a `PackageFn` with the right *name* but different *types*. **Whose signature wins?**

Downstream code already typechecked against *some* signature. Whichever wins must be backward-compatible with the assumed signature at the *call site*, not just self-consistent.

## Strategies, ranked

### Strategy A — First to write wins (simplest, what we should start with)

The race-finishing materializer claims the handle. Its signature becomes canonical. If the other materializer later writes back, it's discarded.

**Pros:** trivial to implement (just `if (vm.materialized.TryAdd handle result)`). Fast. Works for >90% of cases where both paths converge.

**Cons:** if the loser had a *better* signature (one the caller's body actually depends on), we'll get a type error at the call site that's confusing ("but the LLM said int!"). Need good error reporting.

### Strategy B — Type-driven: the call-site's expected type wins

At the call site `let r: Int = foo(x)`, we know `foo` must return `Int`. The pending handle carries a *constraint* from the call site. Materialization candidates that don't satisfy the constraint are rejected.

**Pros:** matches what the caller actually needs.

**Cons:** requires propagating type expectations into the materialization request. The "sigHint" in `Pending` is supposed to be just a hint; this makes it a hard constraint. Type inference at PT2RT becomes more complex.

### Strategy C — Generate the call site too

If signatures diverge, the call site is also pending — re-materialize it against the chosen signature. Cascades, but is principled.

**Pros:** never wrong.

**Cons:** unbounded re-materialization on conflict. Could thrash.

### Strategy D — Vote / consensus across N parallel attempts

Fire 3-5 LLM generations, take the majority signature.

**Pros:** robust to LLM hallucination.

**Cons:** expensive. Wasted speculation.

## Recommendation

**Strategy A for the PoC. Strategy B for v2.**

For Strategy A, log every "rejected because already claimed" with the rejected signature into the trace. The user will see, when reviewing, which fns had divergent attempts and can pick the other one if they want.

For Strategy B, here's the concrete change:

```fsharp
type FQFnName.Pending =
  { handle : System.Guid
    name : string
    sigHint : SignatureHint
    sigConstraints : SignatureConstraint list }   // NEW

and SignatureConstraint =
  | ParamMustBe of paramIdx:int * TypeReference
  | ReturnMustBe of TypeReference
  | ArityIs of int
```

Constraints are extracted at PT2RT time by looking at *how* the pending fn is used:
- `let r: Int = foo(x)` ⇒ `ReturnMustBe Int`
- `foo(123, "a")` ⇒ `ArityIs 2; ParamMustBe(0, Int); ParamMustBe(1, String)`

The materializer's prompt includes these. The post-materialization check rejects candidates that violate them.

## Identity — when are two `Pending` references the *same*?

When the parser sees `foo(x)` in two different places, are those one pending or two?

**The rule:** same name + same scope = same pending = same handle.

Implementation: a per-execution `pendingRegistry: Dict<scope * name, Pending>`. The parser/PT2RT looks up before creating a new handle.

This matters because:
- We don't want to materialize `foo` twice.
- Both call sites should resolve to the same body.
- But: if they have *incompatible* call-site constraints, that's a type error — and that's the right time to surface it.

## What about types-of-types?

A pending *type* (e.g., a record type that's referenced by name but doesn't exist yet) follows the same pattern but is simpler — types don't have race semantics around "behavior," just "structure." Once a type's fields are declared, it's done.

For the PoC: skip pending types entirely. Assume all types exist or get explicit `type` declarations. Only fns are lazy.

## Edge case — recursive self-reference during generation

Generated body of `foo` includes a call to `foo` itself. Without intervention, this creates a new `Pending` handle (because the existing `foo` handle isn't yet materialized — we're inside its materialization).

**Fix:** the materializer's context includes `currentlyMaterializing: Set<PendingHandle>`. When the parser sees a name in that set, it reuses the existing handle. Recursion works.

## Trace events

```
materialize_start  handle=h7a8 name=foo sigHint=...
candidate_arrived  handle=h7a8 source=find  sig="List<'a> -> 'a"  elapsedMs=85
candidate_rejected handle=h7a8 source=generate sig="Int -> String" reason="sig conflict, find already won"
materialize_done   handle=h7a8 winningSource=find
```

Stachu reviewing the trace sees both attempts. He can decide the LLM was actually right and force a re-materialization with constraint-relaxation.

## Key takeaway

**Don't over-engineer this.** First-to-write-wins is fine. The interesting cases (constraint-based consensus, full consensus voting) come up only when the PoC reveals which kinds of conflicts actually happen in practice. Build A, measure, then build B if needed.
