# PDD — State Report

*Written 2026-05-13 evening. Branch `pdd`, ~80 commits past `main`, never pushed.*

## The one-line pitch (current form)

The runtime materializes its own source code on demand via LLM, in parallel, speculatively. Materialized fns can be **refined over time** — they iterate from rough to good. Traces are the artifact.

## What's actually running

### `dark prompt` / `dark pdd run` — arithmetic & recursion demos

All these work end-to-end, fresh cache, gpt-4o-mini:
- `dark pdd run "factorial 6L"` → `720L` (self-recursive, ~3s)
- `dark pdd run "myAbs (-7L)"` → `7L` (if-else, unary minus)
- `dark prompt "compute fibonacci of 8 plus factorial of 5"` → `141L` (parallel materialization)
- `dark pdd run "myMaxOf 4L 9L"` → `9L`

With `PDD_MODEL=gpt-4o`:
- `dark pdd run "sumList [1L;...;5L]"` → `15L` (`Stdlib.List.fold` + lambda)
- `dark pdd run "doubleAll [3L;5L;7L]"` → `[6L;10L;14L]` (`Stdlib.List.map`)
- `dark pdd run "longestRow \"alice,30\\nbob-smith,25\\n...\""` → `"daniel-johnson,35"` (real CSV-shape processing, 6s, 3 independent QA tests pass)

### `darklang.com` port — 10 routes serving real HTML

`Builtin.httpServerServe` from inside a `dark pdd run` expression. Each route is a `render*Page` Pending that materializes on server start (parallel scheduler, cap 3 in-flight). Per-route TSX content extracted from `~/code/darklang.com/src/pages/<X>/index.tsx`, passed as a literal String arg, included in the LLM materialize prompt.

After **`dark pdd refine --all`**, all 10 fns improved. Examples:
- `renderHome` 377 → 1481 chars. Adds `<nav>` linking to anchored sections, `<main>` with 6 `<section>`s, `<footer>` with copyright.
- `renderBackends` 713 → 1284 chars.

Server start after refine: 10 cache-hits via LibParser path in ~20ms total (vs ~5s materializing).

### Architecture pieces shipped

| Piece | What |
|---|---|
| `FQFnName.Pending` | New variant alongside Builtin/Package at PT + RT level. Match-exhaustive across ~13 sites. |
| `OnMissing.AllowPending` | New name-resolver policy. Unresolved fn names in source (incl pipe-stage) become Pendings instead of NotFound. |
| LibParser as primary parse path | LLM body → LibParser → PT.Expr → PT2RT.Expr.toRT → RT.Instructions. Mini-parser is fallback for trivial bodies. |
| Canonicalize Pending handles | After PT2RT, all `Pending` references with the same name get the same RT handle. Fixes recursion + dedupes shared sub-fns. |
| Runtime arg-type propagation | Interpreter writes `pendingArgTypeHints[handle]` from actual Dval types just before calling materialize. Pipeline-stage Pendings get the right sig. |
| Self-aware test runner | Per-test `ExecutionState` whose `materializeFn` returns the just-built fn for self-recursive refs (no LLM loop). |
| Verifiable vs Creative classifier | By name prefix (`render*`/`generate*`/...) + return type. Creative fns skip QA tests, get HTML-specific body prompt. |
| Provisional state | "Works but rough." After creative materialization, score the body; mark Provisional if tag-count or length thin. |
| `dark pdd refine` | Loads cached body, calls LLM with "IMPROVE this", scores new vs old, keeps richer. `--all` iterates all creative fns. |
| Safety rails | Wall-clock budget (`PDD_BUDGET_MS`, default 5min), per-handle LLM cap (3), recursion-skip on test fail, parallel cap (`PDD_PARALLEL`, default 3). |
| Caches | `rundir/pdd-cache/promoted.jsonl` (name → body) + `decomposed.jsonl` (free-text → expr). Survive across sessions. |
| HTML view | `rundir/pdd-view/<sessionId>.html` per session. Two-pane: function cards (state badges) + event log. Top-level expr with inline state-colored annotations on Pending names. Index page at `index.html` lists sessions newest-first with pills + llm-call counts. |
| Tests-as-gate | Independent QA test gen (second LLM call framed as a reviewer who hasn't seen the body) verifies arithmetic-shaped fns. 57/57 PDD unit tests green. |
| HTTP server in Cli | Added `Builtins.Http.Server` to Cli's deps. Dark code itself runs the server. |
| `PDD_MODEL` env override | gpt-4o-mini default (cheap); gpt-4o (or other) when needed for picky syntax. |

## CLI surface

```
dark prompt "<free-text request>"          # decompose + run + visualize
dark pdd run <dark-expression>             # parse + run directly
dark pdd demo <fnName> <Int64-arg>         # hand-built Apply-of-Pending
dark pdd cache (list | clear | paths)      # promoted/decomposed cache admin
dark pdd trace (list | last)               # session HTML lookup
dark pdd refine <fnName> | --all           # iterate a creative fn's body
```

Env: `PDD_MODEL`, `PDD_BUDGET_MS`, `PDD_PARALLEL`, `PDD_SKIP_QA`.

## Demos that still defeat the system

| Prompt | Where it trips |
|---|---|
| `biggestVarianceDate "date,open,close\n..."` (CSV variance) | LLM body uses tuples `(date, var)` + pattern destructuring + non-existent stdlib (`Stdlib.List.foldi`, `Stdlib.Tuple.first`). LibParser declines. Fallback identity. |
| `filter the even numbers from [...] then sum them` | Decompose produces nested-pipe-inside-lambda LibParser can't parse. Clean parse-error message; no crash. |
| `compute the mean of [10L, 20L, 30L]` | LLM proposes `divideBy` returning `Option<Int64>`. Result-handling chain breaks. |

## Cost so far

Cumulative OpenAI spend ≈ $0.10 of the $10 budget. Most expensive demo: full `refine --all` on 10 darklang.com routes with `PDD_MODEL=gpt-4o` and `max_tokens=2000` (~$0.02 per round). Live-runs of cached fns are sub-$0.0001.

## How to enter (for future-you or a colleague)

1. Read this file.
2. `git log pdd ^main` — the diff IS the source of truth.
3. `pdd-thinking/REPORT-thoughts.md` — architectural notes + roadmap.
4. `rundir/pdd-cache/promoted.jsonl` — see what got materialized.
5. `rundir/pdd-view/index.html` — browse all sessions.

To play: source the OpenAI key, then run the darklang.com server:
```
docker exec -d -e OPENAI_API_KEY="$OPENAI_API_KEY" -e PDD_MODEL=gpt-4o \
  -e PDD_BUDGET_MS=3600000 zen_easley bash -c '
    cd /home/dark/app/backend
    dotnet run --project src/Cli --no-build -- pdd run "$(cat /tmp/serve-expr.txt)"
  '
curl http://172.17.0.2:9876/
```
