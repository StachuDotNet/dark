# Coding Loop — Day-1 Phases A→F + Day-2 LLM wiring

> Different mode from the overnight design loop. **This loop edits real F# code on the `pdd` branch.** Stachu is afk, expects to come back to many F# + Dark changes.
>
> Entry point for the `/loop` invocation that follows.

## Goal hierarchy

1. **Floor:** Phase A done (build still works, even if carving was skipped).
2. **Target:** Phases A→F done — first F# test passing with stub materializer.
3. **Stretch:** Day-2 done — real gpt-4o-mini call materializes `addOne`, test asserts `DInt64 6L`.

Anything past Day-2 (parallelism, parking, recovery, tracing) is bonus.

## Tasks

The harness has 7 tasks created (#1–#7) covering all the phases. Use TaskList / TaskUpdate to claim, in-progress, complete. Order is the doc order but feel free to skip A if the build already works.

## Hard constraints

- **Never push `pdd`.** Local-only. Cherry-pick later if anything's worth keeping.
- **Commit after every successful compile.** Free, atomic, easy to revert.
- **30-minute rule on stuck:** if blocked on the same problem for 30 minutes, *revert* the offending change and try a different angle. Don't dig deeper into a hole.
- **No `--no-verify`, no destructive rebases, no stash games** (per Stachu's standing memory).
- **Don't touch the OpenAI key as a string in any file** — only loaded inline via env. Same rules as overnight loop.

## Phase quick-summary (cross-ref `10-day-1-hacking-plan.md`)

| Phase | What | Stop-loss |
|---|---|---|
| A | Carve LibCloud + Builtins.Http.Server | 60 min — if stuck, revert sln + CliHost fsproj; proceed with full build |
| B | `FQFnName.Pending` variant + failwith every match site | None — must complete |
| C | `PackageManager.materializeFn` field, stub returns `EmptyBody None` | None |
| D | Interpreter arm `Function(Pending _) -> …` | None |
| E | `Dval.defaultFor : TypeReference -> Dval` | None |
| F | `Tests/PDD.Tests.fs` passes | None — but if test infra is gnarly, write a one-off F# script instead |

## Build rules

```bash
./scripts/compile                                   # standard
touch backend/src/LibExecution/package-ref-hashes.txt && ./scripts/compile  # after RT type changes
./scripts/run-backend-tests --filter Tests.PDD       # run just PDD tests
```

Per the `feedback_dark_type_change_two_builds.md` memory: Dark type changes need two passes — first build regenerates `package-ref-hashes.txt`, second picks them up.

## What to do each iteration

1. **TaskList** — see what's claimed / pending / in_progress.
2. Pick the next pending task (lowest ID first, since they're in order).
3. TaskUpdate to in_progress with owner.
4. Work it. Commit. If the build is green, commit again with the green state.
5. TaskUpdate to completed when actually done (build green + tests where applicable).
6. **Append a 2-3 line entry to `pdd-thinking/progress.md`** with iteration time, what changed, commit short-hashes.
7. ScheduleWakeup for the next iteration. Cadence: 1500-1800s for code (slower than design — compilation alone is heavy).

## End-of-session protocol

When the loop ends (Stachu comes back, or all reachable tasks are done):
- Write a final `pdd-thinking/SESSION-2-REPORT-2026-05-13.md` (≤2 pages) summarizing what was built + what's left.
- Print it (`~/bin/print-md`) so Stachu has it on paper.
- Leave the branch in a known state: clean compile + clean tests, *or* clearly-documented broken state with a `WIP:` commit explaining what's incomplete.

## Failure modes to expect

- **Match-exhaustiveness explosion in Phase B**: ~74 sites just in LibExecution. Mechanical `Pending _ -> failwith "TODO pending"` everywhere. Don't think about each one.
- **`SignatureHint` forward declaration issues**: if it references `TypeReference`, move the def lower in the file (or use raw strings for Day-1).
- **`PackageFn` construction in tests is fiddly**: copy from existing test fixtures; don't try to build from scratch.
- **`Ply` vs `Task` impedance**: existing code uses `Ply` heavily. Use `uply { }` blocks; bridge to `Task` only where forced by HttpClient.

## What I'm NOT going to attempt

- The parked-frame scheduler (Day-4+ in the spec).
- The find path (Day-3+).
- Capabilities, recovery policy, human resolver (all deferred).
- Trace JSONL output (use `printfn` for now).
- Building LibPDD as a separate project — for the session, just add a module inside LibExecution or use a stub in tests.

If I have time after Demo 1 works, I'll write a *very rough* Day-3 materializer that actually invokes OpenAI. Otherwise, stop and let Stachu pick up.

## OpenAI key access

```bash
set -a; source ~/.config/darklang/llm-keys.env; set +a
```

For HttpClient in F#: read `Environment.GetEnvironmentVariable("OPENAI_API_KEY")` at startup. The build process inherits env, so as long as the test runner is invoked from a shell that's sourced the keys file, it'll work.

Cost budget for this session: $0.50. If we hit that, stop and re-evaluate.

## Session start

Time: 2026-05-13 ~07:30 EDT.
Branch state: clean, 32 commits ahead of main, last commit `1fbaa5f1c pdd-thinking: note post-loop pdd-materialize CLI tool`.
