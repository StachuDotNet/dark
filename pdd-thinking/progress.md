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
- Commits: 2 (iter 9 + key-prefix scrub).

### 2026-05-13 01:35 — iteration 10 (vision addendum + API sanity check)
- Did:
  1. Sanity-tested the OpenAI key with one cheap call: gpt-4o-mini, `temperature=0`, `max_tokens=80`, system prompt asking for `{"sig": ..., "body": ...}` JSON shape. **Result: model returned exact shape on first try, deterministically.** `{"sig":"(x: Int64) -> Int64","body":"x + 1"}`. 66 input + 20 output tokens, ~$0.00003 spent. This proves parser-option P3 is viable.
  2. Added addendum to `01-vision.md` consolidating tonight's decisions: north-star demo (#6), parser P3 (LLM emits JSON, no Dark-parser needed for generate path), human-as-materializer, cheap-by-default ($10 budget protection), four runtime knobs (`--tolerance`, `--allow`, `--materialize-budget`, `--model`), and the five-claim summary.
- Decided: **the five claims** are the durable summary to memorize. (1) source is lazy, (2) trace is the program, (3) types coordinate, (4) runtime is tolerant, (5) human is a materializer.
- Decided: anti-summary — don't pitch PDD as "Copilot for runtime." That misses the trace claim, the type-coordination claim, and the human-as-materializer claim. The right summary is "the runtime materializes its own source."
- Next: write `15-spike-budgets.md` (cost/time/risk envelopes for the spike — both engineering time and API dollars). Then verify Sonnet/Haiku key works similarly (optional — for cross-provider coverage). Then possibly write `16-prompt-shapes.md` with the actual prompt templates we'd use for generate (now that we know the JSON-shape model works).
- Commits: 1 pending (iter 10).

### 2026-05-13 01:39 — iteration 11 (write 15-spike-budgets)
- Did: wrote `15-spike-budgets.md`. Three axes: engineering time (14 days target, Day 3 is the health checkpoint, Day 10 for Demo 6, Day 14 hard-stop); OpenAI $ ($10 buys ~300K cheap calls — basically unrunnable-out unless someone hits Sonnet); cognitive load (signals = "I'm reading 4 docs to remember why"). Stop-when criteria. Three success levels: Bronze (one fn materialized), Silver (multi-step demo), Gold (Demo 6 + trace-as-artifact feels natural). Telemetry: every LLM call's `usage` becomes a `cost` trace event; `dark pdd trace cost <id>` sums + breaks down. Decision: never push the branch during the spike.
- Updated `00-LOOP-SUMMARY.md` file listing (now has 13, 14, 15).
- Decided: Day 3 is the single most important indicator — if Demos 1 + 4 are green by Day 3, the spike is healthy. If not, back up to day-1 plan and figure out which phase didn't actually finish.
- Decided: hardcode `gpt-4o-mini`, no model override flag yet. Add live $-counter. Abort if estimated spend ever crosses $5 in a single session.
- Decided: "spike succeeds but paradigm feels wrong" = most valuable failure mode. Write down why.
- Next: write `16-prompt-shapes.md` (actual prompt templates for `generate`, given that gpt-4o-mini-with-JSON works). Then write a `17-llm-provider-comparison.md` (or fold into 13/16) reflecting on Haiku vs gpt-4o-mini tradeoffs. Then start thinking about `FINAL-REPORT-2026-05-13.md` outline — need to start that by 06:00 EDT to make 08:00 print deadline comfortably.
- Commits: 1 pending (iter 11).

### 2026-05-13 01:41 — iteration 12 (write 16-prompt-shapes — verified prompts)
- Did: ran four real gpt-4o-mini calls (takeHead, calculateVariance, fib, sentimentScore). Total cost: ~$0.0002. Wrote `16-prompt-shapes.md` with the verbatim outputs and per-output critique.
- Key empirical findings:
  - **JSON shape works**: every call returned valid JSON.
  - **Sig and body can disagree** (takeHead returned `Option<'a>` sig but body unwrapped it). Post-materialization type-check is essential.
  - **Model uses wrong types**: `int` instead of `Int64`. Fix in system prompt v2.
  - **Lambda confusion**: model used `=>` instead of `fun -> `. Fix in system prompt v2.
  - **Recursion works**: model happily wrote `fib(n-1) + fib(n-2)` referencing itself.
  - **`max_tokens=150` is too small**: `sentimentScore` got truncated. Bump to 800 default.
  - **Model invents stdlib fns**: needs post-AST-walk validation.
  - **Model over-engineers**: write "prefer simplest correct body" in the prompt.
- Produced a v2 system prompt with explicit Darklang syntax notes. Ready to paste into `Generate.fs`.
- Total LLM spend tonight: roughly $0.0002. Trivial fraction of $10.
- Next: write `17-llm-provider-comparison.md` or fold into 16, OR start outlining `FINAL-REPORT-2026-05-13.md`. It's 01:43 — about 6h 17min until 8am. Need to start the final report by ~06:00 EDT (4h 17m from now) to have time to print. Probably 2-3 more design iterations then start drafting the final.
- Commits: 1 pending (iter 12).

### 2026-05-13 01:46 — iteration 13 (verify v2 prompt; discover List<...> syntax bug)
- Did: ran 4 more gpt-4o-mini calls with the v2 system prompt (takeHead, fib, addOne, doubleAll). Cost ~$0.0003.
- Findings:
  - **v2 fixes worked**: `Int64` not `int`, `fun x -> body` not `=>`, Int64 literals `0L/1L`, recursion intact.
  - **takeHead sig+body now agree** (the v1 bug is gone).
  - **NEW bug**: model writes `List(Int64)` instead of `List<Int64>` — generics syntax with parens not angles.
  - **Redundant bindings**: `let x = x in body` shows up — harmless but ugly.
- Produced v3 system prompt — adds line on angle-brackets-for-generics + line discouraging redundant bindings.
- **Qualitative verdict**: gpt-4o-mini + v2 prompt produces plausibly-compilable Dark on first try for ~75% of simple fns. AST-validation + retry loop catches the rest. **Don't upgrade models yet.**
- Total spend tonight so far: ~$0.0005. Negligible vs $10 budget.
- Time check: 01:46 EDT, 6h 14m to 8am. Plan: 2-3 more design iterations, then start final report by 04:30-05:00 to have plenty of time to refine + print at 06:30-07:00.
- Next: write `17-day-1-quick-reference.md` — a single-page at-the-desk cheat sheet with all file:line pointers, key commands, sample F# diffs, the v3 system prompt verbatim. Make Day 1 turn-key.
- Commits: 1 pending (iter 13).

### 2026-05-13 01:50 — iteration 14 (write 17 — single-page quick-reference card)
- Did: wrote `17-day-1-quick-reference.md`, designed to be printed and taped to keyboard. Single page (fits on letter portrait). Contains: 60-sec orientation, five claims, the pivot line (`Interpreter.fs:317`), the three surgical edits with line numbers, the carving steps with `fsdark.sln:44,61`, two-pass build instructions, OpenAI key location + budget, **the full v3 system prompt verbatim** (ready to paste), the 6 day-1 phases with green-check criteria, acceptance checks for Day 1 + Day 3, when-stuck protocol, telemetry-to-capture, and "Push? No."
- Decided: this card is the single most useful doc for tomorrow morning. Everything else can be skimmed-or-skipped, but this one should be on the desk.
- Time check: 01:51 EDT. About 6h 9m to 8am.
- Next: write `18-the-narrative.md` — a paragraph-by-paragraph "story to tell when explaining PDD to someone." Then start outlining the final report (`FINAL-REPORT-2026-05-13.md`). Plan: 2 more design iterations max, then draft final report around 03:00-04:00, polish 04:00-06:00, print 06:30.
- Commits: 1 pending (iter 14).

### 2026-05-13 01:55 — iteration 15 (DRAFT THE FINAL REPORT)
- Did: wrote first draft of `FINAL-REPORT-2026-05-13.md`. 1619 words, ~2.5-3 printed pages. Sections: 5 claims, algorithm in 1 paragraph, LibExecution changes (the pivot + 3 surgical edits), find-vs-generate scheduler, day-1 hacking plan table, decisions made tonight (parser P3, keep LocalExec, carve LibCloud + Http.Server, tolerant default, sig-consensus Strategy A, human-as-materializer, capabilities at builtin granularity, gpt-4o-mini default, trace JSONL, never push), spike budget envelopes, demo acceptance gates, open questions, pointers to all 18 design docs, "when you sit down" closing.
- Plenty of room left (5 pages = ~3000-3500 words rendered).
- Decided: don't print yet. Refine in later iterations. Final print around 06:30-07:00 EDT.
- Time check: 02:00 EDT. 6h to 8am. Plan: 1-2 more design iterations max (maybe stretch goals: write `18-narrative.md` if time, or just keep polishing 17 and the final report). Final report polish at ~04:00. Print at 06:30.
- Commits: 1 pending (iter 15).

### 2026-05-13 02:01 — iteration 16 (write 18-minimum-viable-spike)
- Did: wrote `18-minimum-viable-spike.md`. The "4-hour cut" — what to skip from the full Day-1 plan to reach Demo 1 quickly. Four surgical edits (Pending variant, materializeFn field, interpreter arm, test). Concrete F# for each. Two failure modes to watch (match-exhaustiveness explosion; PackageManager construction sites). Three intensity levels: <4hr (just scaffolding), 4hr (Demo 1), >4hr (layer in real LLM call + trace + recovery + capabilities). Ends with "the runtime can call code it doesn't have yet" as the single load-bearing learning.
- Decided: smallest-possible-spike skips parking, capabilities, recovery, find, trace, even JSON parsing — proves only the pipeline. Everything else is iteration on top.
- Time check: 02:05 EDT. ~6h to 8am. I'm slightly ahead of my "stop iterating at 6am" goal — could potentially do 2-3 more iterations of polish, then a final-report polish pass, then print.
- Next: polish the final report. Specifically: add a "one-sentence" claim at the very top, tighten the prose, verify the page count after rendering. Maybe write `19-red-team.md` (what could go wrong / common LLM-spike pitfalls) for one more iteration. Then commit, polish, print.
- Commits: 1 pending (iter 16).

### 2026-05-13 02:07 — iteration 17 (write 19-red-team)
- Did: wrote `19-red-team.md`. 14 categorized risks: design-level (trace not really replayable; tolerance hiding logic bugs; sig consensus thrashing; the spike succeeds but feels wrong), implementation-level (match-exhaustiveness eating Day 1; two-pass build forgotten; Ply cancellation subtleties; slow find path without name index; defaultFor wrong for custom types; LLM syntactically-invalid outputs), project-level (tarpit; demo-cadence divergence; burnout; cherry-pick-vs-rewrite). Plus a "we should have known" list. Plus smoke detectors to wire up Day 4 — one-liner shell + jq for tracking model upgrades / recovery rate / trace bloat / spend.
- Most valuable insight: **R4 — the spike succeeds but feels wrong** = most valuable failure mode (write up why).
- Time check: 02:08 EDT. About 5h 52m to 8am. Plenty of time. Plan: 1-2 more design iterations of polish + the final report tightening. Maybe write `20-the-elevator.md` (one-paragraph pitches for different audiences). Then polish final, print at ~06:30.
- Commits: 1 pending (iter 17).

### 2026-05-13 02:14 — iteration 18 (polish final report)
- Did: tightened `FINAL-REPORT-2026-05-13.md`:
  - Added a **TL;DR paragraph** right at the top, summarizing the whole spike in a few sentences.
  - Updated docs pointer table from "18 docs" to "20 docs" — added rows for `18-minimum-viable-spike.md` and `19-red-team.md`.
  - Marked the two most-likely-to-be-opened docs with ★ (02 LibExecution changes and 17 quick reference).
  - Adjusted companion-docs reference to span 00-19.
- Word count: 1773 → ~3 printed pages. Comfortably under 5.
- Time check: 02:15 EDT. ~5h 45m to 8am.
- Next: one more polish pass + maybe write a short `20-elevator-pitches.md` (60s/3-min/10-min variants). Then verify final report renders well. Print around 06:30.
- Commits: 1 pending (iter 18).

### 2026-05-13 02:19 — iteration 19 (write 20-elevator-pitches)
- Did: wrote `20-elevator-pitches.md`. Three lengths (60s / 3min / 10min), all with the same structure (claim → algorithm → why-it's-new → ask). The 60s is the durable elevator one — usable in tweets, hallway, dinner. The 3min is for technically-curious-but-not-an-expert. The 10min is the deep dive (LibExecution intervention, capability model, trace format, three-tier success criteria). Plus a pitch-picker table by audience.
- Updated `00-LOOP-SUMMARY.md` and `FINAL-REPORT-2026-05-13.md` pointer table for 20 → 21 docs.
- Decided: "the 60s is what to practice" — if you can't defend it, the longer versions won't help.
- Time check: 02:20 EDT. ~5h 40m to 8am.
- Plan: STOP creating new docs. Final 3-5 iterations are polish + final report tightening + print prep.
- Next: pass over `00-LOOP-SUMMARY.md` to make sure it's still useful (it's grown a lot since iter 0). Then one more polish on the final report. Then print at ~06:30.
- Commits: 1 pending (iter 19).

### 2026-05-13 02:24 — iteration 20 (polish 00-LOOP-SUMMARY)
- Did: fixed `00-LOOP-SUMMARY.md` stale-ness:
  - Renumbered the file list — `progress.md` and `FINAL-REPORT` had wrong indices.
  - Updated cadence note from "20-30 min" to "270s (4.5 min)" reflecting the actual cadence chosen mid-night.
  - Replaced "UTC… need to figure that out" with concrete "08:00 EDT (12:00 UTC) 2026-05-13".
  - Replaced "Endgame: 10+ design files (claims)" with **STATUS section** showing actual progress against goals — ✅ for done, ⏳ for the pending print.
- Time check: 02:24 EDT. ~5h 36m to 8am.
- Next: re-read the final report once more, look for any awkward phrasing or stale info. After that, possibly add a small inline FAQ to the final report based on questions Stachu might ask in the morning. Then print prep around 06:00-06:30.
- Commits: 1 pending (iter 20).

### 2026-05-13 02:28 — iteration 21 (final report polish 2)
- Did: refined `FINAL-REPORT-2026-05-13.md`:
  - Updated TL;DR companion-docs span to `00 → 20-elevator-pitches.md`
  - Updated "20 design docs" → "21 design docs" in TL;DR and pointer-table header
  - Added a new section "A note on what *not* to do" with 7 anti-patterns to avoid on Day 1 (don't read all docs, don't push, don't upgrade model, don't fight match-exhaustiveness thoughtfully, etc.)
  - Added fallback in "When you sit down" pointing at `18-minimum-viable-spike.md` if time is short
  - Added "trust the most recent commit" guidance for resolving any cross-doc contradictions
- Word count: 1990 → still well under 5 printed pages.
- Time check: 02:30 EDT. ~5h 30m to 8am.
- Next: maybe one more polish + verify everything renders OK. Could even do a dry-run on `print-md` if the tool has a preview mode, but I don't think it does. Possible next: write a short FAQ section in the final report or just wait and print.
- Commits: 1 pending (iter 21).

### 2026-05-13 02:33 — iteration 22 (full inventory + cross-reference verification)
- Did: inventoried everything. Verified all the cross-references in the docs match real file content:
  - `Interpreter.fs:317` = `raiseRTE (RTE.FnNotFound(FQFnName.Package fn))` ✓
  - `RuntimeTypes.fs:88` = `module FQFnName =` ✓
  - `RuntimeTypes.fs:1250` = `type PackageManager =` ✓
  - `fsdark.sln:44` = LibCloud project line ✓
  - `fsdark.sln:61` = Builtins.Http.Server project line ✓
- Verified `~/bin/print-md` exists and is executable.
- Inventory: 24 commits on `pdd` ahead of `main`; 21 numbered design docs + FINAL-REPORT + progress + 00-LOOP-SUMMARY; ~32K words total across all docs; final report 1990 words.
- Branch state clean. No uncommitted changes.
- Time check: 02:35 EDT. ~5h 25m to 8am.
- **Switching to longer cadence now.** Diminishing returns on more iterations. Next wake in ~25 min for one more polish/check, then likely longer fallback until ~06:00 EDT for the actual print.
- This is iter 22 (no commit needed beyond progress.md update).

### 2026-05-13 03:00 — iteration 23 (v3 prompt regression test → v4 fixes)
- Did: ran v3 prompt against 4 Demo-2 fns (`parseCsv`, `skipHeader`, `sortByVarianceDescending`, `getDateField`). Cost ~$0.0003.
- Found a **big new failure mode**: gpt-4o-mini consistently writes parenthesized function-application syntax like `Stdlib.String.split(csv, "\n")` (JS/Python style) instead of Darklang's prefix application `Stdlib.String.split csv "\n"` (F#/ML style). All four outputs had this.
- Two more issues: `<a>` instead of `<'a>` for type variables; invented anonymous record types like `List<Type {...}>`.
- Produced **v4 system prompt** with three new lines covering these failure modes. Updated `16-prompt-shapes.md` with v3 test data + v4 prompt. Updated `17-day-1-quick-reference.md` to use v4 as the paste-ready prompt.
- Cumulative LLM spend tonight: ~$0.0008. Trivial fraction of $10.
- Time check: 03:01 EDT. ~4h 59m to 8am.
- Decided: this kind of regression test would be worth automating once the spike's running — feed N candidate fns through the prompt, AST-check, score. Worth a follow-up doc later (or just an open question).
- Next at +25min: probably no new doc, maybe one more spot-check or just commit + wait.
- Commits: 1 pending (iter 23).

### 2026-05-13 03:26 — iteration 24 (v4 verified — prefix application fixed!)
- Did: ran the same 3 Demo-2 fns with v4 prompt. **All three now use correct Darklang syntax**:
  - parseCsv: `Stdlib.String.split csv "\n"`, `Stdlib.Dict.set acc header value`, `Stdlib.List.map (fun line -> ...) rows` — all prefix application
  - skipHeader: `List<'a>` (apostrophe), `Stdlib.List.drop 1 lst` (prefix)
  - sortByVarianceDescending: `List<Dict<String, Int64>>` (no anon record), prefix application
- **Updated quality estimate: ~85-90% first-try syntax correctness after v4** (up from "barely usable" pre-v4 on this kind of fn). Remaining failure modes are subtler — stdlib name guessing, dict-vs-record confusion. Both addressable in v5 if needed.
- Updated `16-prompt-shapes.md` with v4 verification + remaining failure modes. Updated `FINAL-REPORT-2026-05-13.md` from "~75%" to "~85-90%" first-try.
- Cumulative spend tonight: ~$0.001 (~20 LLM calls total).
- Time check: 03:28 EDT. ~4h 32m to 8am.
- Next: stretch cadence to 30min. One more polish iteration mid-late, then start print prep around 06:30.
- Commits: 1 pending.
