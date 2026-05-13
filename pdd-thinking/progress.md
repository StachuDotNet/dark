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
- Commits: 1 pending.
