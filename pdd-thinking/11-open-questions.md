# 11 — Open Questions

> Things I (Claude) am genuinely unsure about. Stachu's the right person to answer most of these.

**Status:** Stub. Add as they come up across loop iterations.

## Architectural

1. **Should `Pending` live in PT or only in RT?** I argued RT-only in `02-libexecution-changes.md`, but PT-side might make parsing easier. If the parser can emit `Pending` directly, no lowering wrinkles. Counter: it pollutes the source-level type with a runtime concept.

2. **Eager vs lazy materialization defaults.** I said "eager with lazy fallback." But eager has cost — every `Pending` triggers an LLM call at load even if it's never called. Maybe lazy is fine for most fns and eager only for marked-hot ones?

3. **What's the role of types-of-types (Pending types)?** I punted in `04-signature-consensus.md`. Maybe records can also be materialized — "I don't know the fields, generate them based on use." Field references would force the schema.

4. **Do we need Pending values (vs just fns)?** Values are eagerly evaluated and content-addressed. A pending value is `let x = computeSomething()` where `computeSomething` is itself pending. The mechanism subsumes; no new primitive needed.

## Algorithmic

5. **What's the right corpus for "find"?** Just Darklang stdlib? Or scrape e.g. Python stdlib / Haskell base for ideas (translated)? Stachu has mentioned cross-language baselines elsewhere — maybe there's something there.

6. **How do we score sig-match confidence?** Two candidates with the same name but different sigs — we need a ranking function. Embedding similarity? AST shape match? For PoC: probably "exact name match + arity match" only.

7. **What's the right granularity for "deep materialize"?** Per-fn flag? Per-call? Per-trace?

## Runtime

8. **How tolerant is too tolerant?** Returning `0` for division-by-zero might be wrong. We need a list of "always-strict" RTEs even in tolerant mode. Probably: anything involving secrets, anything involving network sends.

9. **Recursion through Pending — what's the termination story?** If the LLM generates a body that calls a pending fn that calls a pending fn... we need depth limits or cycle detection.

## Operational

10. **What does the CLI command surface look like?** `dark pdd run <expr>` vs `dark exec --pdd <file>` vs auto-enabled? Picking the right ergonomics matters.

11. **How do we benchmark?** "Time from prompt to working result" is the headline. But sub-metrics: materialization latency, find-vs-generate win rate, trace size, recovery count, ...

12. **What's the failure-recovery story for a session?** If the LLM provider rate-limits, do we resume with cached pendings? Save progress?

## Philosophical

13. **What's the relationship between a "branch" in SCM and a "session" in PDD?** Are they the same thing? Two sessions on the same source might diverge in their materializations — that's effectively two branches.

14. **Does PDD have a "build" step?** Today no — every load is a materialization. Tomorrow maybe — a "freeze" command snapshots the current cache into a stable artifact you can deploy.

15. **What does "review" mean when reviewing a trace?** It's an interactive process — step through, see substitutions, see materializations, approve / reject. UX challenge.

---

**Note to future-Claude (or Stachu):** add answers under the questions as they're settled. Don't delete the question — leave the original phrasing so the trail of thought is visible.
