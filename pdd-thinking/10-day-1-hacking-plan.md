# 10 — Day-1 Hacking Plan

> What do you type when you sit down with coffee at 8am.

**Status:** Stub — to be made concrete-as-hell in a later loop iteration.

## Goal of Day 1

**End the day with the F# build green, a `Pending` variant defined, and one passing test that exercises the new code path with a stub materializer.**

Not "make it work end to end" — that's days 2-5. Day 1 is plumbing the channels.

## Step-by-step (rough)

```
0. git checkout pdd
   git status   # should be clean
   git log --oneline -5

1. # Carve down the .sln per 09-carving-the-codebase.md
   #   ~~disable BwdServer, LibCloud, QueueWorker, etc.~~
   #   confirm build: ./scripts/compile

2. # Add FQFnName.Pending to RuntimeTypes.fs:88
   #   - the variant
   #   - the SignatureHint type
   #   - the constructor fqPending
   #   - fix every match expression the compiler complains about (lots!)
   #     remember the memory: Dark type changes may need two F# build passes

3. # Add PackageManager.materializeFn (no real impl yet — stub returns EmptyBody)
   #   touch package-ref-hashes.txt to force rebuild on second pass

4. # Add to Interpreter.fs around line 304 (Function(FQFnName.Package fn) block):
   #   | Function(FQFnName.Pending p) -> <calls materializeFn, returns synthetic instrData>

5. # Write a test in backend/tests/Tests/ — call it PDD.Tests.fs:
   #   build a small expression that calls a Pending fn
   #   confirm it returns the default value
   #   confirm a trace event was emitted

6. git commit -m "pdd: scaffold Pending variant + materializeFn stub"
```

## Likely friction points

- **Match exhaustiveness**: every place that matches `FQFnName` will need a new case. The compiler will tell you all of them — embrace the error list. Use `failwith "TODO pending"` liberally.
- **ProgramTypesToRuntimeTypes.fs** and `RuntimeTypesToDarkTypes.fs` are big files; they have FQFnName conversions in lots of places.
- **Two-build issue**: per the memory, Dark type changes need two builds. First build to regenerate `package-ref-hashes.txt`, then `touch` + rebuild.
- **Tests**: the test harness loads everything. Be ready to disable many tests temporarily — see `09-carving-the-codebase.md`.

## What NOT to do on Day 1

- Don't try to implement real LLM calls. Stub the materializer.
- Don't touch the scheduler / parked frames yet. Single-call demos first.
- Don't write the parser. Hand-construct `Pending` references in the test.
- Don't worry about types of types. Only fns are pending.

## Where to look first

- `backend/src/LibExecution/RuntimeTypes.fs:88` — `FQFnName` module
- `backend/src/LibExecution/Interpreter.fs:304` — the `Function(Package fn)` case
- `backend/src/LibExecution/Execution.fs:90` — top-level execute
- `backend/src/LibExecution/RuntimeTypes.fs:1250` — `PackageManager`
- `backend/tests/Tests/` — find the smallest existing test to model from

## After Day 1

- Day 2: real materialize stub — return a pre-canned PackageFn with a body that does `x + 1`. Confirm the runtime can call it through `Pending` and get `x + 1`.
- Day 3: hook up Anthropic SDK; `materializeFn` calls Haiku.
- Day 4: parked frame scheduler; multiple pendings in flight.
- Day 5: tracing.
- Days 6-10: build a real demo program (the HN headline thing).

## The one rule

**Commit after every successful build.** The branch is throwaway, commits are free.
