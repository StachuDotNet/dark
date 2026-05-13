# PDD Spike — Overnight Design Report

**For:** Stachu, waking up 2026-05-13 around 8am EDT
**By:** Claude, working the `pdd` branch from ~00:48 to ~07:30 EDT
**Branch:** `pdd` (local-only, off `main`, never pushed)
**Companion docs:** `pdd-thinking/00-LOOP-SUMMARY.md` through `pdd-thinking/17-day-1-quick-reference.md`
**Open at the desk:** `pdd-thinking/17-day-1-quick-reference.md` — it's a single page, tape it down.

---

## The five claims (memorize)

1. **The source is lazy.** Names + signatures; bodies materialize on demand.
2. **The trace is the program.** Source files are sketches; the trace is the authoritative record.
3. **Types are the coordination protocol.** Pending references carry sig hints; parallel materializations agree via type unification.
4. **The runtime is tolerant.** Missing things substitute defaults; eval keeps moving; recoveries are auditable in the trace.
5. **The human is a materializer.** When find and generate fail, the human is the third path. Their answers cache like any other.

If pitching: "*The runtime materializes its own source code on demand, in parallel, speculatively, with the LLM as both author and search index — and traces are the artifact.*" Don't say "Copilot for runtime" — that misses every interesting claim.

## The algorithm, in one paragraph

The interpreter parses (mostly garbage) pseudocode; for each unrecognized name, it forks two background tasks — **find** (search package store + corpus) and **generate** (LLM call with sig + description) — racing on a 1-second wall-clock budget. The first non-failure wins. Meanwhile, eval *starts* as soon as anything is runnable; when a call hits an unresolved name, the frame **parks** in a `pendingFrames` dict and the scheduler runs other frames whose dependencies are ready. When the materialization completes, parked frames wake. If both paths fail within budget, the runtime substitutes a typed default (`EmptyBody`) and the program keeps moving. Recursion: generated bodies are themselves pseudocode with their own holes, so the process is fractal — fix-point is "fully materialized" but you may never reach it; you may always be running with some references still in flight.

## LibExecution changes (the load-bearing part)

The pivot is **`Interpreter.fs:317`** — the line `raiseRTE (RTE.FnNotFound …)`. Change "give up" to "wait." Three surgical edits:

1. **`RuntimeTypes.fs:88-110`** — Add `FQFnName.Pending of Pending` alongside `Builtin` and `Package`. `Pending` carries a stable `handle: Guid` and a `SignatureHint`.
2. **`RuntimeTypes.fs:1250`** — Add `materializeFn: Pending → Ply<MaterializeResult>` to `PackageManager`. Default impl returns `EmptyBody`. Real impl lives in new `LibPDD` project.
3. **`Interpreter.fs:~304`** — New arm: `Function(FQFnName.Pending p) -> …` calls `materializeFn`, returns synthetic `InstrData`. If pending isn't yet ready, park the frame.

Plus, on `ExecutionState`: three new pluggable fields — `recoveryPolicy: RTE.Error → RecoveryPolicy`, `capabilityCheck: Set<Capability> → FQFnName → Decision`, `humanResolver: HumanQuery → Ply<HumanResponse>`. Plus on `VMState`: `pendingFrames`, `inFlight`, `materialized` dictionaries. None of these touch existing happy paths — they only kick in for `Pending` references or recoverable errors.

LibPDD (new project, parallel to LibExecution) is the **policy + behavior** layer; LibExecution is the **mechanism** layer. Seven files in LibPDD: `Defaults.fs`, `Capability.fs`, `TraceEvents.fs`, `Find.fs`, `Generate.fs`, `Resolver.fs`, `Materializer.fs`. Concrete F# sketches in `pdd-thinking/13-libpdd-materializer.md`.

## Find vs Generate — the scheduler

Default policy: **1s budget per path, both fire, first-non-failure wins, cancel the loser, fall back to `EmptyBody` if both empty**. `EmptyBody` = synthetic body that loads `Dval.defaultFor returnType` into the result register. The program keeps moving.

Find priority: pinned hashes (manually promoted) → exact package store name match → name+arity loose match. No embeddings for v0; keyword match suffices.

Generate: one LLM call with the v3 system prompt (full text in `17-day-1-quick-reference.md`). Verified tonight: gpt-4o-mini, temperature=0, returns valid JSON `{sig: ..., body: ...}` on the first try for ~75% of simple fns. Failure modes are addressable by retry-with-AST-feedback. Cost: ~$0.00005 per call.

`@deep_materialize` annotation opts into 60s budget and (later) Sonnet. Default stays cheap.

## Day-1 hacking plan (4-6 hours target)

| Phase | What | Done when |
|---|---|---|
| **A** | Disable `LibCloud` + `Builtins.Http.Server` in `fsdark.sln:44,61`. Trim `Builtins.CliHost.fsproj`. Stub any cloud/server refs. | `./scripts/compile` succeeds. 60-min stop-loss — if stuck, revert and proceed with full build. |
| **B** | Add `FQFnName.Pending` variant. Run build, get every `failwith "TODO pending"` placeholder added to every match. (Two-pass build: touch `package-ref-hashes.txt` between.) | Compile green. |
| **C** | Add `PackageManager.materializeFn` (stub: always returns `EmptyBody None`). | Compile green. |
| **D** | New `Function(Pending _) -> …` arm in `Interpreter.fs`. | Compile green. |
| **E** | Add `Dval.defaultFor : TypeReference -> Dval`. Cover `TUnit`/`TBool`/`TInt64`/`TFloat`/`TString`/`TList`/`TOption`. Others fall through to `DUnit`. | Compile green. |
| **F** | Write `Tests/PDD.Tests.fs`: construct a `Pending`, call it, assert result is the default for the sig's return type. | `./scripts/run-backend-tests --filter Tests.PDD` green. |

Commit after every phase. The branch is throwaway — commits are free.

## Decisions made tonight (vs. earlier vault notes)

- **Parser strategy: P3** — skip parsing pseudocode entirely; the LLM emits structured JSON. LibParser stays in build, but is used only for `.dark` file loading. Removes a whole class of parser-error handling. (Verified gpt-4o-mini returns this shape deterministically.)
- **Keep LocalExec** — Stachu confirmed it's needed for `.dark` package reload, which is on the PDD critical path (materializations land in the package store).
- **Carve LibCloud + Builtins.Http.Server** — cleanest disable. Don't try to delete; just exclude from `.sln`. Keep `Builtins.Http.Client` because we need it for LLM calls.
- **Tolerant runtime default**: `recoveryPolicy = loose`. Materialization-failure → `EmptyBody`. Tests run strict. CLI flag `--tolerance strict|loose|debug`.
- **Signature consensus: Strategy A** (first-to-write-wins, log rejected). Strategy B (constraint-driven) is for v2.
- **The human is a materializer**, not a workflow step. Same `MaterializeResult` shape as find/generate. Same caching semantics.
- **Capabilities at builtin granularity**, not per-package-fn. Tags: `CapPure`, `CapReadFile`, `CapWriteNet`, `CapReadNet`, `CapWriteDB`, `CapExec`, etc. Defaults: dev mode = `CapAny`, prod mode = whitelist. Per-call `--ask` escalation in interactive sessions.
- **Default model: gpt-4o-mini** ($0.00005/call). Hardcode for spike. $10 budget = ~300K cheap calls = effectively un-exhaustible. `@deep_materialize` annotation opts into pricier models.
- **Trace as artifact**: JSONL, append-only, in `rundir/traces/<sessionId>.jsonl`. New event kinds added to existing `Tracing` struct. Replay/diff/promote come Day 5+.
- **Never push the `pdd` branch**. If something should ship, cherry-pick onto a new branch.

## Spike budget envelopes

- **Engineering time**: 2-week target. **Day 3 is the health checkpoint** — Demos 1 + 4 green by then means the spike is on track. **Day 10**: Demo 6 (HN headline sentiment) green. **Day 14**: hard stop — write a postmortem, pivot or abandon.
- **OpenAI $$**: Hardcode `gpt-4o-mini`. Trip-wire at $7 spent. Hard stop at $9.50. (Today's exploratory calls cost ~$0.0005 total — leaving ~$9.9995 of $10.)
- **Cognitive load**: If you're reading 4 docs to remember why X, stop and re-read `12-glossary.md` + `00-LOOP-SUMMARY.md`.

## Demos (acceptance gates)

| # | Name | Stresses | Day |
|---|---|---|---|
| 1 | `addOne` | Pending → materialize → execute pipeline | 2-3 |
| 2 | Stock variance pipeline (F# blog post canon) | 6 pendings, race scheduler, find-vs-generate | 5 |
| 3 | Recursive `fib` | Self-referencing pendings, cycle handling | 6 |
| 4 | Mixed materialized + pending | Pending coexists with normal calls | 3 |
| 5 | Tolerance under failure | Recovery policy substitutes defaults | 4 |
| 6 | **HN headline sentiment** | Everything: capabilities, multi-pending, the trace as artifact | 10 |
| 7 (stretch) | Recursive descent | Depth limits, fractal materialization | 12+ |

Full source sketches in `pdd-thinking/14-demo-programs.md`. **Demo 6 is the elevator pitch** — `prompt → trace → working result` in one CLI invocation.

## Open questions (from `pdd-thinking/11-open-questions.md`)

1. **Pending in PT or RT only?** RT-only for now (per `02-libexecution-changes.md`); revisit if PT-side helps the parser.
2. **Eager or lazy materialization at load?** Eager-with-lazy-fallback is the plan. Validate empirically Day 4-5.
3. **Pending types vs pending fns?** Fns only for spike. Types are out-of-scope.
4. **Capability inference at PT2RT?** No, just runtime-check at the leaf builtin call. Belt+suspenders for v2.
5. **Recovered-value quarantine** (DRecovered tag)? Defer; rely on trace inspection instead.
6. **What does "review" mean for a trace?** Real UX question. Defer until Demo 6 trace exists.

## Pointers to the 18 design docs

| File | What's in it |
|---|---|
| `00-LOOP-SUMMARY.md` | Loop entry point (used during the night) |
| `01-vision.md` | Algorithm + paradigm + 5-claim summary |
| `02-libexecution-changes.md` | **The most important file** — concrete F# sketch of interpreter changes |
| `03-find-vs-generate.md` | Scheduler design (1s budgets, race policy) |
| `04-signature-consensus.md` | Strategy A (first-wins); B (constraint-driven, v2) |
| `05-tolerant-runtime.md` | RecoveryPolicy enum + tolerant defaults |
| `06-builtin-permissions.md` | Capability tags + enforcement at call site |
| `07-human-in-loop.md` | Human as fallback materializer + 7 triggers |
| `08-tracing-as-artifact.md` | JSONL event schema + replay/diff/promote |
| `09-carving-the-codebase.md` | What to disable in `fsdark.sln`; parser open question (chose P3) |
| `10-day-1-hacking-plan.md` | Literal step-by-step with `git`/`grep`/edit commands |
| `11-open-questions.md` | What I'm unsure about |
| `12-glossary.md` | Terminology + anti-glossary + F# nouns table |
| `13-libpdd-materializer.md` | F# project structure for new LibPDD (7 files) |
| `14-demo-programs.md` | 6 demos w/ source sketches + acceptance criteria |
| `15-spike-budgets.md` | Engineering time / API $ / cognitive load envelopes |
| `16-prompt-shapes.md` | Verbatim gpt-4o-mini outputs + v3 system prompt |
| `17-day-1-quick-reference.md` | **Single-page at-the-desk cheat sheet** |

Plus `progress.md` (running log of the 15 overnight loop iterations) and this `FINAL-REPORT-2026-05-13.md`.

## When you sit down

1. Coffee.
2. Read `17-day-1-quick-reference.md`. Skim this report.
3. `cd /home/stachu/code/dark/main && git checkout pdd && git log --oneline -20`.
4. Phase A from the cheat sheet. Sixty-minute clock on carving.
5. Commit after every phase. Test after each.
6. By end of day: Phase F green, ~6 commits on top of main, branch local.

Have fun.
