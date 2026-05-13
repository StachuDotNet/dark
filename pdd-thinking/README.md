# PDD — Pseudocode-Driven Development

> An experimental fork of Darklang where the interpreter materializes its own source code on demand via LLM, in parallel, speculatively, with traces as the durable artifact.

**Branch:** `pdd` (local-only, off `main`, **never pushed**)

## The five claims (memorize these)

1. **The source is lazy.** Names + signatures; bodies materialize on demand.
2. **The trace is the program.** Source files are sketches; the trace is the authoritative record.
3. **Types are the coordination protocol.** Pending references carry sig hints; parallel materializations agree via type unification.
4. **The runtime is tolerant.** Missing things substitute defaults; eval keeps moving; recoveries are auditable.
5. **The human is a materializer.** When find and generate fail, the human is the third path.

**Pitch:** *"The runtime materializes its own source code on demand, in parallel, speculatively, with the LLM as both author and search index — and traces are the artifact."*

**Anti-pitch:** don't say "Copilot for runtime" — that misses every interesting claim.

## The one demo

```bash
$ dark prompt "compute doubleIt of 4"
[pdd] prompt: compute doubleIt of 4
[pdd] decomposing via gpt-4o-mini...
[pdd] decomposed → doubleIt 4L
[pdd] kick-off 1 pendings in parallel
[pdd] ▸ start doubleIt gpt-4o-mini
[pdd] ▸ llm   doubleIt 1492ms
[pdd] ▸ parsed (x: Int64): Int64  x * 2L
[pdd] ✓ real  doubleIt 1714ms
[pdd] result: DInt64 8L
DInt64 8L
```

Free-text → LLM decomposes to a Dark expression → unresolved fn names auto-materialize via LLM → interpreter runs the result → answer. Second run of the same prompt hits both caches and skips LLM entirely (sub-100ms).

HTML view at `rundir/pdd-view/<sessionId>.html` shows annotated function cards (✓ real / ⋯ in-progress / ▼ fake / ↻ cached / ✗ failed) + chronological event log, self-refreshing every 1s.

## Heavy-hitters status

| # | Goal | Status |
|---|---|---|
| **H1** | `dark prompt "<freeform>"` CLI command | ✅ Live |
| **H2** | Implicit `Pending` from unresolved parser names | ✅ Live (qualified + unqualified) |
| **H3** | Interactive annotated HTML view | ✅ Live (zero deps; meta-refresh) + rich index.html |
| **H4** | Promotion of materialized fns to durable cache | ✅ Live (`rundir/pdd-cache/promoted.jsonl`) |
| **H5** | LibParser as primary body-parse path | ✅ Live (mini-parser is fallback only) |
| **H6** | Tests-as-gate: LLM-claimed + independent verification | ✅ Live (recursion-aware skip) |
| **H7** | Recursion via canonicalized handles + self-aware test runner | ✅ Live |
| **H8** | Safety rails: wall-clock budget, per-handle LLM cap | ✅ Live (`PDD_BUDGET_MS`, cap=3) |
| **H9** | Model override | ✅ Live (`PDD_MODEL`, defaults gpt-4o-mini) |
| **bonus** | Parallel materialization scheduler | ✅ Live (all pendings kick off pre-eval) |
| **bonus** | Decompose-step cache | ✅ Live (`rundir/pdd-cache/decomposed.jsonl`) |

### Demos verified live (latest branch state)

| Prompt | Result | Notes |
|---|---|---|
| `dark prompt "compute doubleIt of 4"` | `DInt64 8L` | original 1-fn demo |
| `dark pdd run "myAbs (-7L)"` | `DInt64 7L` | if-else + unary minus |
| `dark pdd run "myMaxOf 4L 9L"` | `DInt64 9L` | 2-arg comparison |
| `dark pdd run "factorial 6L"` | `DInt64 720L` | recursion, tests skipped on self-ref |
| `dark prompt "compute fibonacci of 8 plus factorial of 5"` | `DInt64 141L` | parallel materialization of two recursive fns |
| `dark prompt "compute the square of 7L plus the cube of 3L"` | `DInt64 76L` | multi-fn decompose |
| `PDD_MODEL=gpt-4o dark pdd run "sumList [1L;...;5L]"` | `DInt64 15L` | List<Int64> + lambda + Stdlib.List.fold; 3 indep tests pass |

## CLI surface

```
dark prompt "<free-text request>"        # decompose + run + visualize
dark pdd run <dark-expression>           # skip decompose, parse user-written Dark
dark pdd demo <fnName> <Int64-arg>       # hand-built Apply-of-Pending (test surface)
```

OpenAI key at `~/.config/darklang/llm-keys.env` (mode 600). On run, sourced via `set -a; source <key file>; set +a` then `dark prompt ...`.

## What's built (live in code)

- **PT + RT:** `FQFnName.Pending` variant in both layers, with PT2RT lowering. Match-exhaustiveness fixes across ~13 sites (LibExecution + LibDB + LibSerialization).
- **Interpreter:** `Function(Pending p)` arm in both executionPoint match and the big Apply match. Two cache layers: `packageFnInstrCache` (by hash) + `pendingFnInstrCache` (by handle) to skip re-materialization.
- **Parser:** `OnMissing.AllowPending` policy in `NameResolver`. Fn-name fallback chains in `WT2PT` check `AllowPending` after exhausting normal lookups; convert unresolved name → `PT.FQFnName.Pending`.
- **Materializer (`PDDMaterializer.fs`):** real OpenAI HTTP call via `System.Net.Http`. Body is parsed via LibParser (dependency-injected `BodyParser` hook installed by CLI; the body is wrapped in `fun <params> -> body` so identifiers bind correctly). Lowered via `PT2RT.Expr.toRT`, then `canonicalizePendingHandles` rewrites all Pending refs so same-name → same-handle (so self-recursion and shared sub-fns dedupe). Mini-parser remains as fallback for trivial bodies. Tests-as-gate: independent test generation via a second LLM call framed as a QA reviewer that hasn't seen the body. Tests run via a CLI-installed `TestRunner` callback that builds a state with a self-aware materializer (self-recursive refs resolve to the just-built fn, no LLM loop). Self-recursive bodies trust-without-test. Failed tests trigger fix-up retries up to maxAttempts. Three safety rails: wall-clock budget, per-handle LLM cap, recursion-skip. Promotes to `rundir/pdd-cache/promoted.jsonl` only when tests pass.
- **HTML view (`PDDHTMLView.fs`):** session-keyed, two-pane, 5 state badges. Updates per event; ~1s meta-refresh.
- **EventSink:** `currentSink : PDDEvent -> unit` with 6 lifecycle events. CLI installs combined stderr+HTML sink.
- **CLI (`Cli/PddCommand.fs`):** `dark prompt`, `dark pdd run`, `dark pdd demo`, `dark pdd cache`, `dark pdd trace`. Decompose-cache + materialize-cache transparent. Parallel scheduler walks instructions pre-eval, fires `Task.Run` per Pending, stashes arg-type hints from literal call-sites. Installs the LibParser body-parser + the test runner. Wall-clock budget enforced (default 5min, override via `PDD_BUDGET_MS`).
- **Tests:** 57/57 PDD tests green (`./scripts/run-backend-tests --filter-test-list PDD`).

## What's *not* yet built

- Parked-frame scheduler for eval that proceeds in parallel with materialization (today: pendings materialize before eval starts; eval is serial).
- Find path (corpus search). Generate-only.
- Capability gates. CapAny implicit.
- Recovery policy beyond raise-FnNotFound. EmptyBody/tolerant-runtime is design only.
- Sig consensus (Strategy B). Strategy A (first-wins) in spirit.
- `trace show` / `replay` / `diff` / `promote` CLI commands. JSONL log exists but no viewer.
- Real package-store promotion (today it's a JSONL sidecar, not the durable `package_fns` table).
- Option<T>, Tuple, Record types in `parseSimpleType` (today: only primitive types + List<T>). LLM-produced bodies that touch these fall through to mini-parser → fallback-identity.
- Mid-program fn iteration (per user feedback: materialized fns should be re-derivable if their results don't satisfy downstream constraints). Today: one-shot per materialization, with retry only on test-fail.

## Hard rules

- **Never push `pdd`.** Local-only by design. Cherry-pick later if anything ships.
- **Commit after every successful compile.** Free, atomic, easy to revert.
- **30-minute rule on stuck:** revert and try a different angle.
- **OpenAI key** lives at `~/.config/darklang/llm-keys.env` (mode 600). Never written to any repo file. Cumulative spend ≈ $0.005 of the $10 budget (mostly prompt-iteration during the design loop; live runs are sub-$0.0001 each thanks to caching).
- Build is two-pass after Dark type changes: `touch backend/src/LibExecution/package-ref-hashes.txt && build`.

## How to enter

1. Read this file (you're here).
2. `DESIGN.md` — sectioned design depth (LibExecution, scheduler, sig, tolerance, capabilities, human, tracing, HTML view).
3. `EMPIRICAL.md` — what we verified empirically about LLM behavior + open questions + red-team.
4. `DEMOS-AND-BUDGETS.md` — concrete programs to build toward + spike envelopes.
5. `archive/` — earlier iteration-by-iteration docs. Don't read unless tracing a specific decision.

When in doubt: `git log pdd ^main` for the total diff. The total diff is the source of truth.
