# 18 — Minimum Viable Spike

> If you only have 4 hours tomorrow, here's the smallest possible subset of all this design that reaches Demo 1 (`addOne` materialized via LLM, returning 6).
>
> **Everything else is optional polish.** Skip without guilt.

## The 4-hour cut

If you sit down at 9am and need to be done by 1pm, drop the following from the full Day-1 plan:

- **Skip carving entirely.** Run the full build. Slower, fine. (Saves up to 60 min.)
- **Skip `Capability`, `RecoveryPolicy`, `humanResolver`.** These are pluggable hooks; default-to-no-op stubs are fine. (Saves ~30 min of plumbing.)
- **Skip `pendingFrames`/parked-frame scheduler.** First demo only needs *synchronous* materialization. The call site awaits the materializer and continues. No parking, no waking. (Saves 1-2 hours.)
- **Skip the trace.** Use `printfn`. We can add real tracing after Demo 1 works. (Saves 30 min.)
- **Skip the corpus / Find path entirely.** Generate-only. Hardcode the LLM call. (Saves an hour.)
- **Skip JSON parsing of the LLM output's `body` field into PT.** For Demo 1, the body returned is a literal — `"x + 1"`. We *don't need* to parse and lower it for this demo: we can stub the result to a known value (`DInt64 6L`) and just verify the materializer was called. This sounds like cheating but it isn't — it proves the pipeline end-to-end without the parser detour.

## What you keep (the load-bearing 1%)

1. **`FQFnName.Pending` variant** in `RuntimeTypes.fs:88`. Just one new constructor.
2. **`PackageManager.materializeFn` field** in `RuntimeTypes.fs:1250`. Default returns a hardcoded `MaterializedFn` of an `addOne`-shaped body.
3. **`Function(Pending p) -> …` arm** in `Interpreter.fs:~304`. Calls `materializeFn`, returns the synthetic instructions.
4. **`Tests/PDD.Tests.fs`** that exercises the whole thing.

That's it. Four edits. Maybe 100-200 lines total.

## The four edits, concretely

### Edit 1 — `RuntimeTypes.fs:88-110`

```fsharp
module FQFnName =
  type Builtin = { name : string; version : int }
  type Package = Hash

  // NEW:
  type Pending = { name : string }   // 4-hour mode: no SignatureHint

  type FQFnName =
    | Builtin of Builtin
    | Package of Package
    | Pending of Pending   // NEW

  // ... existing ...

  let fqPending (name : string) : FQFnName = Pending { name = name }
```

### Edit 2 — `RuntimeTypes.fs:1250`

Add one field to `PackageManager`:

```fsharp
materializeFn : FQFnName.Pending -> Ply<PackageFn.PackageFn option>
```

Default in `PackageManager.empty`:

```fsharp
materializeFn = fun _ -> uply { return None }
```

For the 4-hour spike, override this at test-setup time with a hardcoded stub that returns a synthetic `PackageFn` for `addOne`. Real LLM call comes later.

### Edit 3 — `Interpreter.fs:~304`

In the `match` on `executionPoint`, add:

```fsharp
| Function(FQFnName.Pending p) ->
    uply {
      match! exeState.fns.materialize p with
      | Some fn ->
          let instrData =
            { instructions = List.toArray fn.body.instructions
              resultReg = fn.body.resultIn }
          return instrData
      | None ->
          // Just error for the 4-hour cut. No tolerance yet.
          return raiseRTE (RTE.FnNotFound(FQFnName.Pending p))
    }
```

You may need to also add `materialize : Pending -> Ply<...>` to the `fns` record on `ExecutionState`, wiring it through from the `PackageManager`. Should be 3 lines in `Execution.createState`.

### Edit 4 — `Tests/PDD.Tests.fs`

```fsharp
module Tests.PDD

open Expecto
module RT = LibExecution.RuntimeTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes

let testPendingFnMaterializes : Test =
  testTask "calling a Pending fn invokes the materializer and executes the body" {
    // 1. Build a stub materializer that returns addOne-shaped PackageFn.
    let stubMaterializer (p : RT.FQFnName.Pending) : Ply<RT.PackageFn.PackageFn option> =
      uply {
        if p.name = "addOne" then
          let fn = makeAddOnePackageFn ()  // helper that builds a one-Instruction body
          return Some fn
        else
          return None
      }

    let pm =
      { RT.PackageManager.empty with materializeFn = stubMaterializer }

    let pending = RT.FQFnName.fqPending "addOne"

    // Build a call expr: Apply(pending, [5L])
    let exeState = mkTestState pm
    let instrs = ... // construct via PT2RT or by hand

    let! result = LibExecution.Execution.execute exeState (None, instrs)
    Expect.equal result (Ok (RT.DInt64 6L)) "addOne 5 = 6"
  }
```

The trickiest part is `makeAddOnePackageFn` — constructing a `PackageFn` with a body that does `arg0 + 1`. Look at how existing tests construct package fns and copy the pattern. Or, for the simplest possible test, have the synthetic body just be `LoadVal 0 (DInt64 6L)` (returns 6, ignoring the input). That proves the pipeline but skirts the question of "did it actually execute the materialized body."

## The acceptance check

```bash
./scripts/compile && \
  ./scripts/run-backend-tests --filter Tests.PDD
```

Green? You've **proved the pipeline** in 4 hours. Source → Pending reference → materializer → executed body → result. That's the load-bearing claim of the entire spike.

Everything else — concurrency, tolerance, capabilities, find-vs-generate, the LLM call, the trace, the demos — is iteration *on top of* this load-bearing thing.

## After Day 1

Once Demo 1 green:

- **Day 2 morning:** Replace the stub materializer with a real `gpt-4o-mini` call. Parse the JSON. Construct PT from `body` field. (4 hours.)
- **Day 2 afternoon:** First *actually-generated* fn runs. The trace prints to stdout.
- **Day 3:** Demo 4 (mixed materialized + pending). Demo 5 (tolerance recovery).
- **Day 5:** Demo 2 (stock variance). Parallel materialization.
- **Day 10:** Demo 6.

The full Day-1 plan in `10-day-1-hacking-plan.md` is the **maximum**. This doc is the **minimum**. Pick wherever between feels right based on how the morning is going.

## Two failure modes to watch

1. **Match-exhaustiveness explosion.** Adding `Pending` to `FQFnName` is going to make the F# compiler flag ~100+ match sites across the codebase. The minimum-viable version: add `Pending _ -> failwith "TODO pending"` everywhere mechanically. Don't try to handle each thoughtfully. Just get it compiling.

2. **`PackageManager` construction sites.** Adding a field forces every place that builds a `PackageManager` to set it. Find them with `grep -rn "PackageManager.empty\|getFn = \|getType = \|getValue ="`. There are 5-10 of them. Plug `materializeFn = fun _ -> uply { return None }` into each.

## If you have *less* than 4 hours

Even tighter cut: skip Edit 2 + 4 (the `PackageManager` integration and test). Just add the `Pending` variant and the new interpreter arm. The arm always errors. Verify the build still works. Commit "scaffold for PDD," stop, sleep.

That's 30 minutes. The variant + the arm + the failed match cases. Nothing functional, but the scaffolding is in.

## If you have *more* than 4 hours

Layer back in:
- The real LLM call (replace stub).
- The trace (`printfn` → JSONL).
- Recovery policy (just add `KillFrame | EmptyBody` enum; default to KillFrame for now).
- `Capability` enum (no enforcement yet, just the data).
- The `SignatureHint` on `Pending` (move from `string` to a richer type).

Each one is an hour or two on its own.

---

## The single sentence

If the spike teaches you one thing, it should be: **the runtime can call code it doesn't have yet**. That's it. Everything else is engineering around that one fact.

This doc is the path to learning that one thing the fastest.
