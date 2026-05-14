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
| **H10** | `FQFnName.PackageID` variant — equal treatment to Package(hash) | ✅ Live (PT+RT, threaded through 15+ sites) |
| **H11** | Hot-reload: refine in one process → running server picks up | ✅ Live (pddRefreshHook + file mtime polling) |
| **H12** | `dark pdd refine --watch` background daemon | ✅ Live (round-robin, settle at 5 refines / 2 stuck) |
| **H13** | `dark pdd promote <name>` — SCM commit step (PackageID → Package(hash)) | ✅ Live (28 fns promoted in demo) |
| **bonus** | Parallel materialization scheduler | ✅ Live (cap via PDD_PARALLEL, retries on failure) |
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
| `PDD_MODEL=gpt-4o dark pdd run "doubleAll [3L;5L;7L]"` | `[6L;10L;14L]` | List<Int64>→List<Int64> via Stdlib.List.map |
| `PDD_MODEL=gpt-4o dark pdd run "longestRow \"alice,30\\nbob-smith,25\\n…\""` | `"daniel-johnson,35"` | First **end-to-end String CSV demo**: split + fold + max-by-length. 6s. 3 QA tests pass. |
| `PDD_MODEL=gpt-4o dark pdd run "parseRows \"date,open,close\\n…\""` | `[[date,open,close],[2024-01-01,100,108],…]` | CSV → List<List<String>>. ✓ real with 3 QA tests. |

### darklang.com port — WORKING (live)

The "user hits a route → materialize on demand" vision is real. 10
routes from darklang.com serve real semantic HTML via gpt-4o-materialized
Dark functions. Run:

```bash
PDD_BUDGET_MS=3600000 PDD_MODEL=gpt-4o PDD_PARALLEL=3 \
  dark pdd run "$(cat /tmp/serve-expr.txt)"
# starts an HTTP server on :9876 inside the container; curl http://172.17.0.2:9876/{ , /no, /cli, /packages, /ai, /our-cloud, /backends, /editing, /getting-started, /language }
```

Each route handler is a `render*Page` Pending. The CLI walks the React
source under `~/code/darklang.com/src/pages/<X>/index.tsx`, extracts
visible text content, and embeds it as a literal arg to the route's
`render*Page` call. PDD then materializes each fn with that arg-value
in the LLM prompt — output is semantic HTML5 (`<html><head><title>...
<body><h1>...<section><h2>...<ul><li>...`).

Architecture:
- **Verifiable vs Creative fn classification.** Heuristic by name prefix
  (`render*`, `generate*`, `synthesize*`, etc) + return type. Creative
  fns SKIP independent QA tests (whose hallucinated expectations
  destroy creative output) and instead get a **thin-body detector**:
  if the body looks like an echo (no `<`, or starts with the param +
  has no HTML), re-materialize with explicit "return rich semantic
  HTML" guidance. Up to 3 attempts (vs 2 for verifiable).
- **Per-route literal context.** Page text extracted from TSX, passed
  as a String arg to each Pending. The LLM sees the actual content,
  not just the fn name.

### Demos that still trip the system (gaps surfaced)

| Prompt | Failure | Reason |
|---|---|---|
| `filter the even numbers from [...] then sum them` | parse error (LibParser declines) | Decompose produced nested-pipe-in-lambda LibParser doesn't handle |
| `compute the mean of [10L, 20L, 30L]` | Fake `divideBy` | LLM proposed `Option<Int64>` return; tuple destructuring + Option arithmetic |
| **CSV-variance demo** (`getDate(findMaxVarianceRow(parseRows csv))`) | `parseRows` works; downstream fns produce tuple-heavy bodies LibParser declines | Real progress: runtime-type-propagation gives downstream fns the right sig (List<List<String>> not String). But LLM-natural FP idioms use `(a,b)` tuples + pattern destructuring + non-existent Stdlib names (List.foldi, List.nth, Tuple.second), each of which trips LibParser or runtime. Would need: tuple support in PT/LibParser, or stricter prompt convincing the LLM to avoid the pattern. |

## CLI surface

```
dark prompt "<free-text request>"        # decompose + run + visualize
dark pdd run <dark-expression>           # skip decompose, parse user-written Dark
dark pdd demo <fnName> <Int64-arg>       # hand-built Apply-of-Pending (test surface)
dark pdd cache (list | clear | paths)    # promoted/decompose admin
dark pdd trace (list | last)             # session HTML index
dark pdd refine <name> | --all | --watch [sec]   # iterate creative fns
dark pdd promote <name> | --all | list   # SCM commit step (PackageID → hash)
dark pdd history <name>                  # working revs + committed snapshots
dark pdd diff <name>                     # what `refine` last changed
dark pdd revert <name> [rev]             # roll back to an earlier rev
dark pdd status                          # one-glance environment snapshot
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

1. Read this file (you're here) — *what's built and live*.
2. **`WRAP-UP.md`** — **the single consolidated doc.** What worked, what didn't, decisions to lock, 3-wave merge plan, big-picture roadmap, F# → Dark self-hosting plan, prioritized TODOs by time budget. Print this and mark it up.
3. `DESIGN.md` — sectioned design depth (LibExecution, scheduler, sig, tolerance, capabilities, human, tracing, HTML view).
4. `EMPIRICAL.md` — what we verified empirically about LLM behavior + open questions + red-team.
5. `DEMOS-AND-BUDGETS.md` — concrete programs to build toward + spike envelopes.
6. `PDD-CLI-REFERENCE.md` — every `dark pdd ...` command in detail.
7. `REAL-PACKAGE-FNS.md` — scope sketch for Wave 3 (real `package_functions` integration).
8. `archive/` — historical session reports, the original SCM-INTEGRATION pivot, earlier iter-by-iter docs, and `reflection-layer/` (the four per-topic reflection docs consolidated into `WRAP-UP.md`). Don't read unless tracing a specific decision.

When in doubt: `git log pdd ^main` for the total diff. The total diff is the source of truth.
