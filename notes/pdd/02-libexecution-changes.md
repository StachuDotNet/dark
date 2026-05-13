# 02 — LibExecution Changes (THE key doc)

> Stachu said: "early on, think: what are the changes to LibExecution, in particular? If you figure that out, the rest will come much more easily."

This doc sketches the F#-level changes. **Concrete code shapes** > high-level prose.

## TL;DR

Three surgical changes:

1. **Add `FQFnName.Pending of name * sigHint`** — a new third variant alongside `Builtin` and `Package`. Means "I've seen this name in source, I have a guess at its signature, but no body yet."
2. **Extend `PackageManager` with `materializeFn`** — kicks off find-or-generate for a pending name, returns a `Ply<PackageFn>` that resolves when ready. The interpreter awaits it the same way it awaits `getFn`.
3. **Add `pendingFrames` to `VMState`** — when eval hits a `Pending` and no body is ready yet, the frame parks in `pendingFrames` keyed by the pending-fn handle, the interpreter switches to another runnable frame, and the parked frame resumes when the materialization completes.

Plus a handful of supporting bits (trace events, RTE recovery as continuation, suspension primitives). Detailed below.

## Today's call resolution path (the thing we're changing)

From `Interpreter.fs:262`, in the `executeInner` loop, when we encounter `Function(FQFnName.Package fn)`:

```fsharp
| Function(FQFnName.Package fn) ->
    uply {
      match exeState.packageFnInstrCache.TryGetValue fn with
      | true, cached -> return cached
      | false, _ ->
        match! exeState.fns.package fn with
        | Some fn ->
          let instrData = { instructions = …; resultReg = … }
          exeState.packageFnInstrCache[fn.hash] <- instrData
          return instrData
        | None -> return raiseRTE (RTE.FnNotFound(FQFnName.Package fn))
    }
```

That `raiseRTE (FnNotFound ...)` is the **single most important line in the codebase** for PDD purposes. It's the moment we say "I give up." We need it to instead say "I'll wait."

## Change 1 — `FQFnName.Pending`

In `RuntimeTypes.fs` around line 88:

```fsharp
module FQFnName =
  type Builtin = { name : string; version : int }

  /// A pending-materialization handle. The interpreter has seen a
  /// reference to this name but no body has been resolved yet.
  /// `sigHint` is the parser's / LLM's best guess at the signature.
  type Pending =
    { handle : System.Guid  // stable across speculation attempts
      name : string         // human-facing name
      sigHint : SignatureHint }

  and SignatureHint =
    { typeParams : List<string>
      paramHints : List<string * TypeReference option>   // (name, optional type)
      returnHint : TypeReference option }

  type FQFnName =
    | Builtin of Builtin
    | Package of Hash
    | Pending of Pending

  let fqPending (name : string) (hint : SignatureHint) : FQFnName =
    Pending { handle = System.Guid.NewGuid()
              name = name
              sigHint = hint }
```

**Important properties of `Pending`:**
- `handle` is stable. Two parallel speculation attempts on the same name see the same handle.
- `sigHint` is *optional everywhere*. The parser only fills in what it has. The runtime can run with `paramHints = []`.
- Resolution: once a body is materialized, the runtime *replaces* the `Pending` reference with `Package(hash)` in the cached instructions. Subsequent calls go through the fast path.

## Change 2 — `PackageManager.materializeFn`

In `RuntimeTypes.fs` around line 1250:

```fsharp
type PackageManager =
  { getType : FQTypeName.Package -> Ply<Option<PackageType.PackageType>>
    getValue : FQValueName.Package -> Ply<Option<PackageValue.PackageValue>>
    getFn : FQFnName.Package -> Ply<Option<PackageFn.PackageFn>>

    /// NEW. Resolve a pending fn handle to a real PackageFn, by either
    /// finding it in the package store (by name/sig match) or generating
    /// it via LLM. Returns when *either* path succeeds, or when both
    /// budgets expire. Default budget: 1 second per path.
    materializeFn : FQFnName.Pending -> MaterializeOptions -> Ply<MaterializeResult>

    // ... existing fields ...
  }

and MaterializeOptions =
  { findBudgetMs : int   // default 1000
    generateBudgetMs : int   // default 1000
    allowEmptyBody : bool   // if both fail, return a no-op body
    preferPath : MaterializePref }

and MaterializePref = PreferFind | PreferGenerate | RaceBoth

and MaterializeResult =
  | Materialized of PackageFn.PackageFn * MaterializeSource
  | EmptyBody of PackageFn.PackageFn   // signature-only, body returns default
  | Failed of string

and MaterializeSource = FromFind | FromGenerate
```

A reference implementation of `materializeFn` lives in a new `LibPDD` (or whatever) project, parallel to `LibCloud`. The default `PackageManager` shipped with the experimental CLI swaps in the PDD-aware impl. Existing code (which always calls `getFn` directly) keeps working — only the `Pending` path in the interpreter calls `materializeFn`.

## Change 3 — Suspendable frames in `VMState`

This is the gnarliest change. The current VM is single-threaded over a frame map. We need to:
- Park a frame when its current instruction is `Apply(thingToApply = Pending h)` and the materialization hasn't completed.
- Wake it when the materialization completes.
- Allow *other* frames to run in the meantime.

### State additions

In `VMState`:

```fsharp
type VMState =
  { ...
    /// Frames waiting for a Pending fn to materialize.
    /// Keyed by the pending handle so all frames blocked on the same
    /// handle wake up together.
    pendingFrames : Dictionary<System.Guid, List<uuid>>  // pendingHandle → frameIDs

    /// Materializations in flight. Lookup: "is this one already being worked?"
    inFlight : Dictionary<System.Guid, Ply<MaterializeResult>>

    /// Result cache once materialized.
    materialized : Dictionary<System.Guid, MaterializeResult>
    ... }
```

### The interpreter's intervention

In `Interpreter.fs`, the `Function(...)` resolution block becomes:

```fsharp
| Function(FQFnName.Package fn) ->
    // ... existing path unchanged ...

| Function(FQFnName.Pending p) ->
    uply {
      match vm.materialized.TryGetValue p.handle with
      | true, Materialized(fn, _) | true, EmptyBody fn ->
          // Already done. Treat as Package fn.
          let instrData = { instructions = ...; resultReg = ... }
          return instrData

      | true, Failed msg ->
          return raiseRTE (RTE.MaterializationFailed(p, msg))

      | false, _ ->
          // Park or start.
          if not (vm.inFlight.ContainsKey p.handle) then
            let task = exeState.pm.materializeFn p defaultOpts
            vm.inFlight[p.handle] <- task
            do! kickOffSpeculation exeState vm p task   // fire-and-forget

          // Park this frame.
          parkFrame vm vm.currentFrameID p.handle
          // Yield: return a sentinel that the outer loop interprets as "switch frames"
          return SwitchFrames
    }
```

### The outer loop scheduler

The current `executeInner` is a `while vm.callFrames.ContainsKey vm.currentFrameID do ...` loop. We change it to:

```fsharp
while not (allFramesParkedOrDone vm) do
    if vm.currentFrameID is parked then
        pickNextRunnable vm   // switches vm.currentFrameID
    else
        ... existing instruction loop ...

    // After each instruction batch, check the inFlight table for completions.
    // Wake any parked frames whose pendings have resolved.
    checkInFlightAndWake vm
```

And `kickOffSpeculation` writes back via:

```fsharp
let kickOffSpeculation exeState vm pending task =
    uply {
      let! result = task
      vm.materialized[pending.handle] <- result
      // Caller-side wake happens in checkInFlightAndWake on the main loop.
      return ()
    }
```

### Why this works with Ply

`Ply` is essentially F#'s `ValueTask` — single-threaded cooperative async. The "fire off speculation" is `Ply.start (uply { ... })`. The main loop polls `inFlight` for completion. **No actual threads.** This is good — it sidesteps the "Ply isn't great for parallel" concern from the dev notes.

For real parallelism (concurrent LLM calls), `materializeFn` internally uses `Task` (true .NET tasks) and bridges back via `Task |> Ply.ofTask`. The find and generate coroutines fire on the threadpool. The interpreter doesn't see threads — it sees Plys.

## Change 4 — Recovery as continuation (RTEs that don't kill the world)

Today, `raiseRTE` throws `RuntimeErrorException`. The whole VM unwinds. For PDD's "tolerant runtime" we want **error → empty value → keep going** in many cases. This is a separate, smaller change but co-located:

```fsharp
type RecoveryPolicy =
  | KillFrame   // current behavior
  | EmptyBody   // replace the failed call with default-of-T
  | EmptyFrame  // unwind to caller, give caller a default
  | AskUser     // surface to human-in-loop (see 07-human-in-loop.md)

// New ExecutionState field:
recoveryPolicy : RTE.Error -> RecoveryPolicy
```

In `executeInner`, where today we `raiseRTE`, we instead consult the policy:

```fsharp
let handleRTE rte =
    match exeState.recoveryPolicy rte with
    | KillFrame -> raise (RuntimeErrorException(...))
    | EmptyBody ->
        // Write a default value into the result register and skip to next instr.
        registers[resultReg] <- Dval.defaultFor expectedType
        counter <- counter + 1
    | EmptyFrame ->
        // Pop the frame, give caller a default.
        popFrameWithDefault vm
    | AskUser ->
        parkFrameForHuman vm rte
```

For now, the default policy is `EmptyBody` for `MaterializationFailed`, `KillFrame` for everything else. We tighten over time.

## Change 5 — Trace events

Every materialization event needs to enter the trace, so we can debug.

In `RT.Tracing.Tracing`:

```fsharp
type Tracing =
  { ...
    materializeStart : FQFnName.Pending -> unit
    materializeEnd : FQFnName.Pending * MaterializeResult * elapsedMs:int -> unit
    frameParked : uuid * FQFnName.Pending -> unit
    frameResumed : uuid * FQFnName.Pending -> unit
    ... }
```

The default impl writes JSONL to a trace file in `rundir/traces/<sessionId>.jsonl`.

## What we're NOT changing (yet)

- **TypeChecker**: stays oblivious to `Pending`. PT2RT erases the distinction — `Pending` at the call site looks like any other call. The type-of-result is `sigHint.returnHint` if set, else `'fresh`.
- **ProgramTypes**: no changes. Pending exists only at RT.
- **Parser**: no changes (yet). The "pseudocode parser" lives outside LibExecution — see `09-carving-the-codebase.md`. For now, callers can manually construct `Pending` references in tests.
- **Stream.fs**: probably unrelated. Streams already handle laziness, but our laziness is at the source level, not the value level.
- **Builtin RTE handling**: separate concern, lives in `Builtin.fs`. Builtins still raise normally.

## Implementation order

When you sit down:

1. **Hour 1**: Add the `FQFnName.Pending` variant. Make the F# compile. Get all the pattern matches updated (lots of them — RuntimeTypesToDarkTypes is a beast). Don't run anything yet, just compile.
2. **Hour 2**: Add `materializeFn` to `PackageManager`, with a stub impl that always returns `EmptyBody`. Hook into the `Function(Pending _)` case. Compile.
3. **Hour 3**: Write a test in `backend/tests/Tests/PDD.Tests.fs` that constructs a hardcoded `Pending` reference, calls it, and asserts it returns the empty body. Get green.
4. **Hour 4**: Wire up the parked-frame scheduler. Test with two `Pending` references in the same program — they should both materialize and resume.
5. **Day 2**: Real `materializeFn` implementation with find + generate coroutines.
6. **Day 3**: Tracing, recovery policy, the rest.

## Open issues to think about more

- **Lambdas with `Pending` in their body**: parking inside a lambda is fine, but we need to make sure the closure's environment is preserved. Should be automatic given how `LambdaImpl` already captures `closedRegisters`.
- **Self-referencing pending**: a `Pending` fn whose body, once materialized, references itself recursively. The hash isn't known until after materialization. We need to allow forward references inside a freshly-generated body.
- **GC of failed pendings**: if materialization fails (or the user kills it), we have to clean up the parked frames. `pendingFrames[handle]` gets walked, frames get killed with `MaterializationFailed` error.
- **Mutation through Pending in DBs**: not relevant in this experiment (no DB writes in the demo). Park for later.
- **Concurrent vs serial materialization**: do we let the LLM be called N times in parallel? Budget: yes, with a global semaphore (default `Environment.ProcessorCount`). Materialization N tied to .NET threadpool tasks.
