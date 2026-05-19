# PDD — Pseudocode-Driven Development

> An experimental fork of Darklang where the interpreter materializes its own source code on demand via LLM, in parallel, speculatively, with traces as the durable artifact.

**Branch:** `pdd` (local-only, off `main`, **never pushed**)

## Claims

See `CLAIMS.md` for the five core claims (reframed since the spike).

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

## Demos verified live

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

## CLI

```
dark prompt "<free-text request>"   # decompose + materialize + run + visualize
```

The spike had a thicket of `dark pdd ...` subcommands. Per feedback,
most should be automatic (cache, demo) or part of normal SCM
(promote, history, diff, revert) rather than a separate PDD surface.
What survives, and in what shape, is open. See `FRONTIER.md`.

OpenAI key lives at `~/.config/darklang/llm-keys.env` (mode 600).

## What's live in code

`FQFnName.Pending` + `PackageID` variants in PT/RT, `OnMissing.AllowPending`
parser policy, `PDDMaterializer.fs` (OpenAI + LibParser + tests-as-gate
+ canonicalized handles + hot-reload), `PDDHTMLView.fs`, `Cli/PddCommand.fs`,
57/57 PDD tests green. See `WRAP-UP.md` for the retrospective, `FRONTIER.md`
for what the F# substrate should grow into.

## How to enter

1. Read this file — what's live in code.
2. `WRAP-UP.md` — spike retrospective + 3-wave integration plan.
3. `CLAIMS.md` — the claims, reframed.
4. `ALGORITHM.md` — high-level sketch (incomplete).
5. `FRONTIER.md` — speculative + source-code-changes thoughts.

When in doubt: `git log pdd ^main` for the total diff. The total diff is the source of truth.
