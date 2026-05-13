# Progress log

> Append-only. Each loop iteration, add a short entry.

Format:
```
### YYYY-MM-DD HH:MM — iteration N
- Did: <files touched, ideas committed>
- Decided: <new opinions formed>
- Next: <what's queued for the next iteration>
- Commits: <git short-hash list>
```

---

### 2026-05-13 00:48 — iteration 0 (setup)
- Did: created `notes/pdd/` directory with `00-LOOP-SUMMARY.md`, `01-vision.md`, `02-libexecution-changes.md`, this `progress.md`.
- Read enough of `Interpreter.fs`, `RuntimeTypes.fs`, `Execution.fs` to ground the LibExecution design doc (#02). Identified the exact line that's the pivot — the `raiseRTE (FnNotFound)` in `Function(Package fn)` at `Interpreter.fs:317`.
- Decided: the LibExecution-side change is **three concrete additions** + **two supporting bits**: (1) new `FQFnName.Pending` variant, (2) `PackageManager.materializeFn`, (3) `VMState.pendingFrames` + scheduler. Plus tracing events + a `recoveryPolicy` hook for "tolerant runtime."
- Decided: bias toward `EmptyBody` (signature-only with default return) over delay/retry. Keeps the program moving.
- Decided: budgets are 1s/1s by default (find/generate). Configurable per call via `MaterializeOptions`.
- Next: write `03-find-vs-generate.md` (the scheduler), `04-signature-consensus.md` (parallel attempt coordination), `05-tolerant-runtime.md` (RTE recovery design in depth).
- Commits: pending (will batch first commit on `pdd` after a few more files).

### 2026-05-13 01:05 — iteration 1 (move to pdd-thinking, scheduler + consensus + tolerance + stubs)
- Stachu pinged mid-iteration to move `notes/pdd/` → `pdd-thinking/` because `notes/` is gitignored.
- Did: moved the directory (`git mv`). Updated `00-LOOP-SUMMARY` references. Wrote `03-find-vs-generate.md`, `04-signature-consensus.md`, `05-tolerant-runtime.md` substantively. Stubbed `06-builtin-permissions.md`, `07-human-in-loop.md`, `08-tracing-as-artifact.md`, `09-carving-the-codebase.md`, `10-day-1-hacking-plan.md`, `11-open-questions.md` with clear topic outlines.
- Decided: signature consensus default = first-to-write-wins, log rejected candidates (Strategy A). Strategy B (constraint-driven) is for v2.
- Decided: tolerant runtime via `RecoveryPolicy` per-RTE; default `loose` policy substitutes empty/default values for most errors. CLI flag `--tolerance strict|loose|debug`.
- Decided: `LibPDD` will be a new F# project alongside `LibExecution`, holding the materializer + capability hooks + trace event types. Keeps LibExecution surgical.
- Decided: corpus for "find" v0 = Darklang stdlib + already-materialized fns this session, via name+arity match (no embeddings).
- Decided: eager materialization at load is the default; lazy is the interpreter-side fallback path.
- Next: deepen stubs in priority order — `09-carving-the-codebase.md` (concrete list of what to disable, with paths) first, since that's what unblocks day-1 hacking; then `10-day-1-hacking-plan.md` made literally step-by-step with specific commands; then `06-builtin-permissions.md` deepening. After that, iterate on whatever's thin.
- Commits: 2 so far on pdd (initial seed + move-to-pdd-thinking).

### 2026-05-13 01:18 — iteration 2 (deepen 09 — carving)
- Did: enumerated the actual `fsdark.sln` projects + dep graph (verified from `.fsproj`s). Deepened `09-carving-the-codebase.md` with a project-by-project keep/disable table, the exact procedure (edit `fsdark.sln`, trim `Builtins.CliHost.fsproj`, rebuild, smoke-test), notes on `LibTreeSitter` and `LocalExec`, tests subset, structure of the new `LibPDD/` project, and a stop-loss escape hatch.
- Decided: critical cuts = `LibCloud`, `Builtins.Http.Server`. Probable cuts = `LibTreeSitter`, `LocalExec`. Probable adds = `LibPDD/` (new) with files `Materializer.fs`, `TraceEvents.fs`, `Capability.fs`, `Find.fs`, `Generate.fs`, `Defaults.fs`.
- Decided: if carving burns >1 hr, revert sln/CliHost changes and proceed with full build. Carving is optimization, not prerequisite.
- Next: deepen `10-day-1-hacking-plan.md` into a literal step-by-step with grep/sed/git commands. Then deepen `06-builtin-permissions.md`. Then deepen `07-human-in-loop.md`.
- Commits: pending iter-2 commit after this entry.

### 2026-05-13 01:00 — iteration 3 (deepen 10-day-1-hacking-plan)
- User pinged: tighten loop to 10min.
- Did: rewrote `10-day-1-hacking-plan.md` as a literal step-by-step. 6 phases (A-F): carve sln, add Pending variant, materializeFn stub, interpreter arm, Dval.defaultFor, first passing test. Each phase has explicit commands + line numbers + commit messages. Verified line numbers against `RuntimeTypes.fs:88-110` and `fsdark.sln:44,61`.
- Decided: skip typed `SignatureHint` on Day 1 — use raw strings to avoid forward-decl headache; promote to typed in Day 2.
- Decided: `WarningsAsErrors=FS0025` trick to force every match-exhaustiveness site to show up. 74 FQFnName match sites in LibExecution alone, more elsewhere.
- Decided: Phase A (carving) has a 60-min stop-loss → revert if needed.
- Next: deepen `06-builtin-permissions.md` (capability model concrete sketch), then `07-human-in-loop.md`, then `08-tracing-as-artifact.md`. Then write a glossary doc (`12-glossary.md`?) and concrete `LibPDD/Materializer.fs` sketch (`13-libpdd-materializer.md`?).
- Commits: 1 pending (carving plan).

### 2026-05-13 01:13 — iteration 4 (deepen 06 — capabilities)
- Did: rewrote `06-builtin-permissions.md`. Concrete capability tag list (CapPure, CapReadTime, CapReadRandom, CapReadEnv, CapReadFile, CapReadNet, CapWriteFile, CapWriteNet, CapWriteDB, CapExec, CapSendSecret, CapAny). Maps to existing `Previewable` + `Harmful` patterns in `RuntimeTypes.fs:1340` for continuity. Enforcement happens at call site in `Apply` in `Interpreter.fs`. CLI surface: install-time defaults, per-invocation `--allow X,Y,Z`, per-call escalation in `--ask` mode. LLM-side: hide ungranted-cap builtins from the prompt.
- Decided: extend `BuiltInFn` with `capabilities : Set<Capability>` field (~5 lines of type, then default everything to `{CapPure}` and bump the dozen builtins that need more).
- Decided: package fns inherit caps transitively but for PoC we just check at the leaf builtin call. Sound + cheap.
- Decided: don't try to compute caps at PT2RT for PoC; rely on runtime checks. Recovery for `CapabilityDenied` follows the `RecoveryPolicy` model from #05.
- Decided: defer domain-restricted capabilities ("HTTP to anthropic.com only") + time-bound capabilities ("allow for 5 min") to post-PoC.
- Next: deepen `07-human-in-loop.md` (concrete sketch of where the human enters + the CLI surface for that), then `08-tracing-as-artifact.md` (trace event schema + replay design), then a new `12-glossary.md` (terminology), then a `13-libpdd-materializer.md` (full F# sketch of the new LibPDD project).
- Commits: 1 pending (iter 4).

### 2026-05-13 01:17 — iteration 5 (deepen 07 — human in loop)
- Did: rewrote `07-human-in-loop.md`. Core framing: **the human is a fallback materializer** (same protocol as find/generate, output becomes a real PackageFn). 7 concrete trigger types: MaterializationFailed, CapabilityDeniedAsk, ConsensusRequired, RepeatedFailure, TraceDivergent, AskUserAnnotation, breakpoint. 3 modes: sync TTY (Mode A, the spike default), async inbox (Mode B, same primitive), webhook (Mode C, out of scope). Designed `humanResolver : HumanQuery -> Ply<HumanResponse>` field for `ExecutionState`. Trace events `human_ask`/`human_answer` capture latency. CLI commands: `pdd inbox`, `resolve`, `resume`, `review`, `promote`.
- Decided: caching of human answers — Option 3 (cached in package store) for materialization, Option 2 (cached in session) for capabilities, Option 1 (one-shot) for breakpoints.
- Decided: build `humanResolver` field early even if first impl is `failwith "TODO"` — cheap insurance against later interpreter changes.
- Next: deepen `08-tracing-as-artifact.md` (full event schema + replay semantics + diff design), then write a `12-glossary.md`, then a `13-libpdd-materializer.md` (concrete F# sketch of LibPDD project structure).
- Commits: 1 pending (iter 5).

### 2026-05-13 01:21 — iteration 6 (deepen 08 — tracing; absorb user feedback on LocalExec + parser)
- Did: rewrote `08-tracing-as-artifact.md` with full event schema (JSONL, per-event JSON shape, `t`-ms-since-session-start). Listed the new fields to add to existing `Tracing` struct. Replay procedure (cache pre-populate + intercept builtins). Diff design (align by t/ev). Promote command (writes to `pdd_pinned_fns`). SCM connection (branches diverge on sketches + hashes + traces).
- Absorbed Stachu's mid-iteration feedback:
  - **LocalExec must stay** (at least the package-reload bits) — updated `09-carving-the-codebase.md` table row + dedicated section. PDD materializations land in the package store via this pipeline.
  - **Parser uncertainty** (LibParser vs new) — added a "Parser open question" section to `09-carving-the-codebase.md` with three real options (P1 LibParser + lenient post-processor; P2 new combinator-based tolerant parser; P3 skip parsing, LLM emits structured JSON). Tentative rec: P3 for spike, P1 later. Cross-referenced into `11-open-questions.md` Q0.
- Decided: trace format = JSONL with versioned event kinds. Don't pre-optimize to binary. Don't even add zstd until disk hurts.
- Decided: Day-1 tracing surface is just `materialize_start`, `materialize_done`, `recovery` writing to a file + a `--no-trace` flag. Replay/diff/promote come later.
- Next: write a new `12-glossary.md` (consistent terminology for the docs), then a `13-libpdd-materializer.md` (concrete F# structure of the new LibPDD project), then revisit `01-vision.md` to incorporate the parser question and the trace-replay framing.
- Commits: 1 pending (iter 6 fold-in).

### 2026-05-13 01:25 — iteration 7 (write 12-glossary; lock terminology)
- Did: wrote `12-glossary.md`. Pins core terms (PDD, sketch, cache, trace, materialization, pending, handle, pinned hash), the two-paths terms (find/generate/race/budget/EmptyBody), runtime concepts (tolerant runtime, strict mode, RecoveryPolicy, recovered value), capability terms (capability/grant/grant scope/capability check), sig + consensus terms (SignatureHint/constraint/Strategy A,B), human-in-loop terms (HumanQuery/Response/Resolver/Inbox), trace terms (event/session/replay/diff/promote), F# nouns table (every new type and where it lives), anti-glossary (terms we're deliberately NOT using: agent, hole, stub, lazy, sketch-as-verb), and style notes. Updated `00-LOOP-SUMMARY.md` to include 12 and 13 in the file listing.
- Decided: anti-glossary explicit — "agent" and "stub" are forbidden in PDD docs because they pull in baggage; "deferred" or "pending" beats "lazy" because we're talking about source-level laziness not value-level.
- Next: write `13-libpdd-materializer.md` (concrete F# project structure for new LibPDD — Materializer.fs, Find.fs, Generate.fs, Capability.fs, Defaults.fs, TraceEvents.fs — with shapes of each module). Then iterate on 01-vision.md to absorb the parser open question + trace-replay framing.
- Commits: 1 pending (iter 7).

### 2026-05-13 01:29 — iteration 8 (write 13-libpdd-materializer)
- Did: wrote `13-libpdd-materializer.md`. Full F# project sketch — 7 files (`Defaults.fs`, `Capability.fs`, `TraceEvents.fs`, `Find.fs`, `Generate.fs`, `Resolver.fs`, `Materializer.fs`) plus `LibPDD.fsproj`. Real code for `defaultFor : TypeReference -> Dval`, the `Capability` enum + `mkChecker`, the JSONL `TraceWriter`, `tryFind` priority order (pinned → exact → loose), `tryGenerate` shape (LLM provider abstraction + JSON-out path per parser-option-P3), and the `race` orchestrator (`Task.WhenAny` + `CancellationTokenSource`, fallback to `EmptyBody` if both fail). Wired-up sketch showing how it plugs into `Execution.createState`. Build order. Test surface. Time estimate (Day 1 = LibExecution; Days 2-3 = LibPDD shippable v0).
- Decided: `LibPDD` is the **policy + behavior** layer; `LibExecution` is the **mechanism** layer. New types/fields live in `LibExecution.RuntimeTypes` so the interpreter can see them; implementations live in `LibPDD`. Keeps the interpreter surgical.
- Decided: LLM provider is abstracted via `Generate.LLMProvider` record — Anthropic, OpenAI, Ollama all hide behind that. (Matches Feriel's unified-provider work from the Mar-15 advisor call.)
- Decided: hand-rolled JSON serializers in `TraceEvents.fs` for Day 1; switch to `System.Text.Json` converters Day 2.
- User dropped an OpenAI key for later testing — held only in conversation context, **never written to disk** (not in progress.md, not in any file, not in any memory). If used later tonight will pass via env var inline.
- Next: iterate on `01-vision.md` to absorb the parser open question + trace-replay framing + the "human as fallback materializer" framing. Then either deepen `11-open-questions.md` further OR write a new doc on **demo programs** (what test programs we'd run end-to-end to validate the spike). Probably the latter — it'd make the spike feel concrete.
- Commits: 1 pending (iter 8).

### 2026-05-13 01:30 — iteration 9 (write 14-demo-programs; absorb key + budget feedback)
- Did: wrote `14-demo-programs.md` — six concrete demo programs in increasing difficulty: (1) `addOne` trivial, (2) Stock variance pipeline from F# blog post, (3) recursive Fibonacci, (4) Mixed materialized + pending, (5) Tolerance under failure, (6) HN headline sentiment (the headliner / Stachu's chat-fragment vision), plus a stretch (7) Recursive descent. Each has source sketch, expected result, what it stresses, day-N acceptance criterion, and connection to other docs.
- Stored OpenAI key at `~/.config/darklang/llm-keys.env` (mode 600, dir mode 700) — Stachu said it's OK to put on disk now that conv-context isn't durable. Repo confirmed clean of any key-prefix strings (grep over pdd-thinking returns no matches).
- Absorbed Stachu's $10 budget directive: spike defaults to **cheap fast models** (`gpt-4o-mini` for OpenAI, `claude-haiku-4-5` for Anthropic). `@deep_materialize` still defaults to cheap; opt-in to richer. Updated `13-libpdd-materializer.md` with provider-config section.
- Decided: F# unit-test path (Phase F of Day 1) is the *first* validation. `.dark`-file demos come Day 3+. Programs live in `backend/dark-packages/pdd_demos/` (TBD path).
- Decided: storytelling order for video/blog — start with Demo 1, end with Demo 6.
- Next: iterate on `01-vision.md` to absorb everything we've decided (parser P3, human-as-materializer, $10 cheap-model default, the demo-6 north star). Then write a short `15-spike-budgets.md` to pin down what we're optimizing for in terms of dollar/time/token.
- Commits: 1 pending.
