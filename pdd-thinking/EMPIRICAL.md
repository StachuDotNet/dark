# PDD Empirical Findings

> **STATUS:** Written mid-spike. Findings on prompt iteration and
> LLM behavior are durable; specifics like "cumulative ~$0.0012" and
> "match-site count = 9" are stale (current: ~$0.30, 15+ sites with
> PackageID). Several "open questions" in §4 are now answered:
> Q10 (recursion termination) — solved via canonicalized handles;
> Q1 (Pending in PT) — yes, lives in both PT and RT. For current
> state see `WRAP-UP.md`.

What we actually verified by running real LLM calls + what we're still unsure about + where the spike could fail.

---


## §2 Cost numbers (verified)

- **Per gpt-4o-mini call:** 100-450 input + 20-150 output tokens. ~$0.00005-$0.00015 per call. Average ~$0.00008.
- **Total spent during the spike:** ~$0.0012 across ~20 sanity calls during prompt iteration.
- **Spike budget cap:** $10. Effectively un-exhaustible at cheap-model rates (~125K calls before tripwire).

`@deep_materialize` annotation (Sonnet) would cost ~$0.001-$0.005/call, ~30x cheap-rate. Reserve for fns that hit AST-retry repeatedly.

---

## §3 The mini-body parser (what the LLM body becomes)

The materializer hands the LLM's `body` string to `parseMinimalBody` in `PDDMaterializer.fs`. Three recognized shapes today:

| Shape | Example body | Generated RT instructions |
|---|---|---|
| Int64 literal | `42L` | 1 instruction: `LoadVal(1, DInt64 42L)`; resultIn=1 |
| Identity | `x` (or matches param name) | 0 instructions; resultIn=0 (the arg register) |
| Binary arith | `x + 1L`, `x - 3L`, `x * 7L` | 3 instructions: load builtin Applicable, load constant, Apply |

Anything else falls back to an identity-shaped `PackageFn` (returns arg unchanged). LLM body is still logged to `rundir/logs/pdd-materialize.jsonl` for inspection.

**Verified end-to-end:** `addOne 5L` with body `"x + 1L"` runs through the real interpreter using `Stdlib.Int64.add`, returns `DInt64 6L`. The mini-parser's instructions actually compute.

**Reachable next** (with LibParser or LLM-emits-PT-JSON): full bodies become executable, not just three shapes.

---

## §4 Open questions (what we're still unsure about)

1. **Should `Pending` also live in PT, not just RT?** Currently RT-only. If the LLM emits PT-shaped JSON, having Pending in PT would simplify the parse-back path. Counter: pollutes the source-level type with a runtime concept. **Tentative answer**: keep RT-only until the LLM-emits-PT path is built.

2. **Eager or lazy materialization at program load?** Currently lazy (materialize when call site fires). Eager would walk the program at load and start all materializers in parallel. Faster perceived response but wastes calls if branches aren't taken. Validate empirically when we have real programs.

3. **Pending types vs Pending fns?** Fns only for spike. Types are out-of-scope until a real program needs an unresolved type ref.

4. **Capability inference at PT2RT?** No, just runtime-check at the leaf builtin call. Belt+suspenders for v2 when we start composing capabilities.

5. **Recovered-value quarantine?** `DRecovered` wrapper that propagates through eval so users see "this answer involved N substitutions." Defer; rely on trace inspection instead for now.

6. **What does "review" mean for a trace?** Real UX question. Defer until Demo 6 (HN headline sentiment) trace exists.

7. **What's the right corpus for find?** Just Darklang stdlib? Or scrape Python/Haskell stdlibs for ideas (translated)? Defer.

8. **Should the LLM emit structured PT JSON instead of a body string?** Yes, eventually — sidesteps the mini-parser. But the string path works for the spike.

9. **How to score sig-match confidence?** For finding by name+arity, an exact match is unambiguous. Embeddings come later if name-match is too coarse.

10. **Recursion through Pending — termination story?** Generated body of `foo` references `foo`. Need cycle detection or a depth limit. Today it'd infinite-loop on materialize. Add a per-handle "currently materializing" set + depth cap before the demo.

---

## §5 Red-team (what could go wrong)

### Design-level

**R1 — Traces aren't actually replayable in practice.** If too much context isn't captured (LLM nondeterminism, time, network), replays diverge and "trace is the program" is rhetoric. **Test:** take a trace, replay 10×, count matches. If <9/10 match, the framing is broken.

**R2 — Tolerance hides logic bugs, not just hallucinations.** Loose mode lets your own division-by-zero return 0; you think the program works; it's silently wrong. **Mitigation:** run tests in strict periodically. Don't let `EmptyBody` cover non-materialization errors.

**R3 — Signature consensus thrashes.** If LLM-generated sigs disagree with find-result sigs 30%+ of the time, Strategy A logs noise constantly. **Mitigation:** Strategy B (constraint-driven) becomes urgent. Have the design ready.

**R4 — The spike succeeds but feels wrong.** Demo 6 runs end-to-end. Stachu uses PDD for a real task. It works. But it feels uncomfortable, unpredictable, hard to debug. **This is the most valuable failure mode.** Notice the feeling, write up *why*, pivot.

### Implementation-level

**R5 — F# match-exhaustiveness explosion.** ✅ Already navigated: only 9 sites needed updates (predicted 74).

**R6 — Two-pass build forgotten.** Touching RT types invalidates `package-ref-hashes.txt`. Mitigation: always `touch backend/src/LibExecution/package-ref-hashes.txt` after type changes; rebuild. (Standing memory.)

**R7 — Ply cancellation is subtle.** Cancelling a losing race in `Ply` doesn't model cleanly. For the spike: don't actually cancel — just ignore the loser. 1 second of wasted background CPU per pending is fine.

**R8 — Find is slow because package store has no name index.** Existing PackageManager is by-hash only. Mitigation: skip find for v0 (generate-only); add a `pdd_pinned_fns` name→hash table when find becomes worth wiring.

**R9 — `defaultFor` is wrong for custom types.** For records: need to look up field types and default each. Today returns DUnit for `TCustomType`. Mitigation: most demos use only Stdlib types; sidestep the issue. Fix when it hurts.

**R10 — LLM returns syntactically invalid Dark.** Already seen for string ops (~50-65% first-try). Mitigation: retry-with-AST-feedback. 2× cost; ~halves failure rate.

