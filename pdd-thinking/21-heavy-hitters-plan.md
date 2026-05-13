# 21 — Heavy-Hitters Plan (post coding-loop pivot)

> Written 2026-05-13 ~16:00 EDT after Stachu asked: "what's the most heavy-hitting stuff you could do so I can build full dark programs that take advantage of this system? And how can I give users visualizations as to what fns are being built, what top-level expression is being run, etc?"

The low-level primitives are done (35/35 PDD tests green). What's left to make this **a thing you can use end-to-end on real Dark programs**:

## The four heavy-hitters

### H1 — `dark pdd run <expr>` CLI command  *(highest immediate impact)*

End-user surface. Stachu (or anyone) types:

```bash
dark pdd run "addOne 5L"
```

…and watches the runtime materialize `addOne` via OpenAI, run it, return `6L`. Without this, the whole system is a buried library.

Implementation outline:
- New subcommand handler in `backend/src/Cli/Cli.fs` (or a sibling module) that dispatches on `pdd run`.
- Parses the expression string via `LibParser.Parser.parsePTExpr`.
- Builds an ExecutionState with `PDDMaterializer.materialize` plugged in as `fns.materialize`.
- Streams events to stderr (see H3).
- Executes; prints the result.

Estimated effort: 1-2 iterations.

### H2 — Implicit Pending from unresolved names  *(makes ANY .dark file a PDD program)*

When the parser hits a fn name it can't resolve, instead of erroring (`OnMissing.Error`), emit a `Pending` ref. PT then carries Pendings; PT2RT translates them; the interpreter materializes at call time.

This is the wire that turns "PDD" from an opt-in concept into the *default behavior for any .dark file with missing names*.

Implementation outline:
- New `OnMissing.AllowPending` variant in `backend/src/LibParser/NameResolver.fs`.
- When fn-name resolution fails under that policy, emit `PT.FQFnName.Pending { name = …; … }`.
- Need to add `Pending` to PT-level `FQFnName` (currently RT-only). Mirrors the existing PT→RT lowering.
- Wire the CLI's parser invocation with `AllowPending`.

Estimated effort: 2-3 iterations. The PT-side `Pending` requires re-doing the match-exhaustiveness sweep we did for RT (probably another ~10 match sites).

### H3 — Interactive annotated code view  *(visualization, the "show, don't tell")*

> Stachu 2026-05-13: "I'd love some actual interactive view — not some logs, but like I wanna see the code, and have annotations around it noting what's real and what's in-progress and what's fake, etc. with logs to the side."

**Architecture: generate a live-updating HTML document** during the run. Two-pane layout:

```
┌────────────────────────────────────┬──────────────────────────┐
│ pseudo code                        │ events                   │
│                                    │                          │
│   let result =                     │ 16:03:12 parsing         │
│     addOne ✓ (real, 312ms)         │ 16:03:12 pending: addOne │
│       5L                           │ 16:03:13 llm→ gpt-4o-mini│
│                                    │ 16:03:13 llm← "x + 1L"   │
│   (* pending: nothing *)           │ 16:03:13 compile: 3 ins  │
│   = DInt64 6L                      │ 16:03:13 result: 6L      │
│                                    │                          │
└────────────────────────────────────┴──────────────────────────┘
```

Annotation states per name:
- **real (green)**: materialized + executed successfully (this run or cached from prior)
- **in-progress (yellow, animated)**: currently materializing (LLM call in flight)
- **fake (gray)**: returned EmptyBody / default / identity placeholder
- **failed (red)**: materialization errored (cap denied, parse failure, etc.)
- **cached (blue)**: hit `pendingFnInstrCache` or pinned hash — no LLM call

Implementation outline:
- `PDDMaterializer` (or a new `LibPDD.View` module) maintains an event log + per-handle state.
- A live HTML file is written to `rundir/pdd-view/<sessionId>.html`. Re-rendered after each event.
- Self-refreshing via `<meta http-equiv="refresh" content="1">` for v1; later WebSocket / SSE.
- Static CSS+inline JS — no dependencies, opens directly in a browser.
- The CLI prints the file path once at start; user opens it once and watches.

Logs to stderr (the original plan) also stay — they're useful for piping/grep.

Estimated effort: 2-3 iterations. The HTML renderer is the bulk of it.

### H4 — Promotion to package tree  *(durability across sessions)*

Currently every session re-materializes. Real PDD wants: materialize once, persist to `package_fns`, future sessions look it up by hash and skip the LLM call.

Captured as Task #10. Implementation:
- After `materialize` returns a `PackageFn`, call `PMRT.Fn.put` (or equivalent) to persist.
- Use the deterministic hash from `fnFromBody` so identical bodies dedupe.
- Optionally add a name → hash index (`pdd_pinned_fns` table) so finding by name later doesn't need a full scan.
- CLI flag `--auto-promote` (default true) and `dark pdd promote <fnHash>` for manual control.

Estimated effort: 2-3 iterations. Depends on understanding LibDB's package_fns insert path.

## What to NOT do yet (still worth deferring)

- **Parked-frame scheduler** — Materialize-on-call works fine synchronously. Parallelism is a Day-5+ optimization.
- **Find path** — Generate-only is enough until quality starts hurting.
- **Capability gates** — In dev mode CapAny is fine. Wire later before production.
- **Recovery policy beyond "raise FnNotFound"** — Once H1-H3 land, we can see what failure modes actually hurt and tighten then.
- **Real trace viewer (`dark pdd trace show`)** — JSONL is greppable today. Pretty-printer can come after H1.
- **Sig consensus, type-hint propagation, deep_materialize** — All design-doc work, no immediate user-visible impact.

## Order of operations

| Iter | Task | Acceptance |
|---|---|---|
| 10 | Add an `EventSink` to ExecutionState; PDDMaterializer emits structured events | Unit test: events captured to in-memory list match expected sequence |
| 11 | H1 CLI command — `dark pdd run <expr>` (prints events to stderr) | `dark pdd run "addOne 5L"` works end-to-end with real LLM |
| 12 | H3 HTML view — render annotated code + event panel | After each `dark pdd run`, an HTML file exists; opening it shows the code with state badges |
| 13-14 | H2 implicit Pending in parser | `dark pdd run "let foo (x: Int64): Int64 = something x in foo 5L"` works without `something` defined |
| 15 | H3 live refresh (auto-reload during run) | Long-running materialization is visible as it happens |
| 16-17 | H4 promotion | Same prompt run twice; second run skips the LLM call |

After H1 (iter 11), you can demo. After H2 (iter 13), you can write real Dark files. After H4 (iter 15), the demo is reproducible cheap.

## Doc tidying done in this pass

- This file (`21-heavy-hitters-plan.md`) is the **current plan of record**. Older plan docs (10-day-1-hacking-plan.md, 18-minimum-viable-spike.md, etc.) describe how we got here; they're history.
- `00-LOOP-SUMMARY.md` will be updated to point here.
- `FINAL-REPORT-2026-05-13.md` is now a snapshot of the design-loop state, not the working plan.

## The single sentence to keep in mind

**A user should be able to type `dark pdd run "<some pseudocode>"`, open the HTML view in their browser, and watch their code light up — green for real, yellow for materializing, gray for fake — with logs streaming to the side.** Everything in H1-H4 serves that sentence.
