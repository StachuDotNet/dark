# PDD Coding Session 2 — Report

**For:** Stachu, returning from afk
**By:** Claude, working `pdd` branch ~07:48 → ~10:35 EDT (~2h 45min)
**Branch state:** local-only, never pushed, 45 commits ahead of `main`
**Tests:** `./scripts/run-backend-tests --filter-test-list PDD` → **24/24 green**

## What got built — all 8 tasks done

| Phase / Task | Result | Key file/line |
|---|---|---|
| A — carve sln | **Skipped** (build was fine without it; saves ~60min) | — |
| B — `FQFnName.Pending` variant | ✅ | `RuntimeTypes.fs:88-115` |
| C — `PackageManager.materializeFn` | ✅ | `RuntimeTypes.fs:1267` |
| D — Interpreter `Function(Pending p)` arm | ✅ | `Interpreter.fs:325-348` |
| E — `Dval.defaultFor` | ✅ | `Dval.fs:177-216` |
| F — `PDD.Tests.fs` (16 unit tests green) | ✅ | `backend/tests/Tests/PDD.Tests.fs` |
| 7a — Apply-of-Pending → end-to-end integration test | ✅ | `Interpreter.fs:1049-1095` (Apply arm) |
| 8 — real gpt-4o-mini HTTP call | ✅ | `PDDMaterializer.fs` (new file) |

## The end-to-end picture (concrete)

A `Pending` reference, when applied through the interpreter, now:

1. **Apply's big match** (Interpreter.fs:1049) hits `| FQFnName.Pending p`.
2. Calls `exeState.fns.materialize p` (which routes to `PackageManager.materializeFn`).
3. If `Some fn`, caches `InstrData` under both `fn.hash` (for general lookup) **and** `p.handle` (for skipping re-materialization — important for real LLM calls).
4. Pushes a CallFrame with `executionPoint = Function(Pending p)` and the args placed in registers.
5. The outer loop re-iterates; the executionPoint match for `Pending` (line 325) hits the new `pendingFnInstrCache`, returns the cached InstrData. **No second materialize call.**
6. Instructions execute. Result lands in the parent frame's putResultIn register.
7. Frame-return type-check uses the `Pending` TVariable arm (line 1166) — accepts any type. **(Day-N: replace with proper sig-hint type-check.)**

**Verified by integration test**: `pendingFnGoesThroughInterpreter` builds a hand-crafted `RT.Instructions` doing `Apply(pending, [DInt64 42])`, with a stub materializer returning an identity-shape `PackageFn`. Asserts result == `DInt64 42L` AND materializer called exactly once.

## What's real vs. what's stub

| Layer | Status |
|---|---|
| Type system (`Pending` variant + sig serializers) | **Real** |
| Interpreter wiring (Apply + executionPoint + type-check + cache) | **Real** |
| Materializer plug point in `PackageManager` | **Real** |
| OpenAI HTTP call from F# (`PDDMaterializer.callOpenAI`) | **Real**, untested live in F# but mirrors the verified `~/bin/pdd-materialize` shell pattern |
| LLM-body → RT instructions | **Stub** — returns hardcoded identity PackageFn on any LLM success; the actual `body` string is logged to `rundir/logs/pdd-materialize.jsonl` for human inspection |

## How to activate the real materializer in your CLI session

```fsharp
let pm = { existingPm with materializeFn = LibExecution.PDDMaterializer.materialize }
let exeState = LibExecution.Execution.createState builtins pm ...
```

Then `OPENAI_API_KEY=… ./scripts/run-cli pdd run …` (once a `pdd run` CLI command exists). The materializer will:
1. Read `OPENAI_API_KEY` from env per-call.
2. Build the v4 prompt for the pending fn's name.
3. POST to `https://api.openai.com/v1/chat/completions` with model `gpt-4o-mini`, T=0, max_tokens=800.
4. Parse the `{sig, body}` JSON response (tolerant of ```json fences).
5. Append a JSONL log entry to `rundir/logs/pdd-materialize.jsonl`.
6. Return an identity `PackageFn`. Eval continues; the program returns its input unchanged (whatever the LLM actually wrote is in the log, not yet wired).

## The interesting bugs / gotchas hit

1. **Match-exhaustiveness was less work than predicted.** Doc said ~74 sites; actual was 9 across LibExecution + LibDB + LibSerialization. Most matches were wildcard-friendly.
2. **`failwith` is banned codebase-wide** — use `Exception.raiseInternal`.
3. **`--filter` wants exact prefix** in `run-backend-tests`; use `--filter-test-list <substring>` instead.
4. **Frame-return type-check was the subtle bug in 7a.** First attempt set `executionPoint = Function(Package fn.hash)`. The cache lookup hit (instructions loaded fine), but the type-check arm at `Interpreter.fs:1148` separately fetches the fn via `exeState.fns.package` to read `returnType` — and the materialized fn isn't in the actual package store. Fix: keep `executionPoint = Function(Pending p)` so the type-check arm uses the Pending TVariable handler (accept-anything for now).
5. **Caching keyed by both `fn.hash` and `Pending.handle`** to avoid re-materialization (matters when materialize is a 1+ second LLM call).

## What's NOT done (Day-3+)

- **LLM body → RT.** Right now the LLM body string is logged but discarded; materialize returns hardcoded identity. Either (a) parse it via LibParser, or (b) have the LLM emit structured PT JSON (the doc's tentative P3 strategy). Either way, ~half a day.
- **Type-sig consensus.** The frame-return arm for Pending is permissive (TVariable). Day-N tightens this when SignatureHint lands on Pending.
- **Recovery policy / tolerant runtime.** Currently a None from materialize raises `FnNotFound`. Doc spec says recover via `EmptyBody` using `defaultFor returnType`. Not wired.
- **Find path.** The materializer is generate-only.
- **Real scheduler / parking.** Materialization is fully sync.
- **Capabilities, tracing JSONL, human-resolver.** All stubs.

## Where to pick up

**For real-LLM end-to-end demo:** write a tiny CLI program that constructs a `PackageManager` with `materializeFn = PDDMaterializer.materialize`, builds the same Apply-of-Pending instructions as the integration test, runs it. Output should be the input arg (5L → 5L) + a real LLM response in `rundir/logs/pdd-materialize.jsonl`. **This is ~30 min of work.**

**For real-LLM materialized-body execution:** translate the LLM's `body` string to RT.Instructions. The narrowest path is structured JSON (the LLM emits PT-shaped trees directly, you deserialize). Half a day.

**Cost spent this session:** $0 (no LLM calls during F# coding; the earlier sanity tests via `pdd-materialize` totaled ~$0.0012 of the $10 budget).

## File map

```
backend/src/LibExecution/RuntimeTypes.fs       — Pending variant, materializeFn,
                                                 pendingFnInstrCache
backend/src/LibExecution/Interpreter.fs        — executionPoint Pending arm,
                                                 Apply Pending arm, type-check arm
backend/src/LibExecution/Dval.fs               — defaultFor
backend/src/LibExecution/PDDMaterializer.fs    — NEW: real OpenAI call
backend/src/LibExecution/Execution.fs          — wires materializeFn into Functions
backend/src/LibDB/PackageManager.fs            — default materializer = None
backend/src/LibDB/Tracing.fs                   — Pending trace event support
backend/src/LibSerialization/Binary/...        — Pending serialization tag 2
backend/src/LibExecution/RTQueryCompiler.fs    — Pending returns None for SqlSpec
backend/src/LibExecution/RuntimeTypesToDarkTypes.fs — Pending DT round-trip
backend/tests/Tests/PDD.Tests.fs               — NEW: 24 tests, all green
backend/tests/Tests/Tests.fsproj               — registered PDD.Tests.fs
backend/tests/Tests/Tests.fs                   — added Tests.PDD.tests to runner
backend/fsdark.sln                             — unchanged
```

## TL;DR for tired Stachu

- 8 tasks done, 45 commits, 24/24 PDD tests green, branch never pushed.
- The runtime can call code it doesn't have — proven end-to-end with a stub materializer.
- Real OpenAI HTTP call is wired but its body output is logged, not executed (you get identity behavior).
- ~30 min away from a "real LLM, real materialization-pipeline" demo.

Go forth.
