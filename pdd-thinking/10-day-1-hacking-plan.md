# 10 — Day-1 Hacking Plan (literal)

> What you type, in order, on Day 1. Copy-paste friendly.

## Goal of Day 1

End the day with:
1. The build is green.
2. `FQFnName.Pending` variant exists.
3. `PackageManager.materializeFn` field exists (stub returning EmptyBody).
4. The interpreter has a `Function(Pending _)` arm that calls `materializeFn`.
5. One passing test that exercises the pending path and gets back the default value.

**Estimate:** 4-6 hours, mostly fighting match-exhaustiveness errors. The compiler will be your friend (and pain).

## Setup (10 min)

```bash
cd /home/stachu/code/dark/main
git checkout pdd
git status   # should be clean
git log --oneline -5
./scripts/run-cli docs for-ai-internal   # refresh on the build/test layout
```

## Phase A — Carve the sln (30-60 min)

Per `09-carving-the-codebase.md`. Recap with exact line numbers (verified 2026-05-13):

```bash
# fsdark.sln lines to remove:
#  - line 44: LibCloud project
#  - line 61: Builtins.Http.Server project
# (plus their corresponding "ProjectSection" + "ConfigurationPlatforms" entries — search "{3FC57943-9D51-49AE-9FBD-4A112B4F68D6}" and "{7A3E4BC2-D5F1-4A8E-9B3C-6F2D1E8A4C5B}" and remove all matching lines)

# Then edit Builtins.CliHost.fsproj — remove these <ProjectReference> lines:
#   <ProjectReference Include="../../LibCloud/LibCloud.fsproj" />
#   <ProjectReference Include="../Builtins.Http.Server/..." />

# Find and stub usages in Builtins.CliHost source:
grep -rn "LibCloud\." backend/src/Builtins/Builtins.CliHost/
grep -rn "Builtins.Http.Server" backend/src/Builtins/Builtins.CliHost/
# Comment out or stub each call site.

# Build:
./scripts/compile

# If it compiles, smoke-test:
./scripts/run-cli docs for-ai

git add -A
git commit -m "pdd: carve LibCloud + Builtins.Http.Server from build"
```

**If carving breaks something subtle and you've spent >60 min**, revert and proceed:
```bash
git checkout main -- backend/fsdark.sln backend/src/Builtins/Builtins.CliHost/Builtins.CliHost.fsproj
./scripts/compile
git commit -am "pdd: bail on carving; proceed with full build"
```

Carving is optimization, not a prerequisite. Don't sink the morning here.

## Phase B — Add `FQFnName.Pending` (60-90 min)

### B.1 — Define the variant

Edit `backend/src/LibExecution/RuntimeTypes.fs:88-110`:

```fsharp
module FQFnName =
  type Builtin = { name : string; version : int }
  type Package = Hash

  // NEW types:
  type SignatureHint =
    { typeParams : List<string>
      paramHints : List<string * Option<TypeReference>>
      returnHint : Option<TypeReference> }

  type Pending =
    { handle : System.Guid
      name : string
      sigHint : SignatureHint }

  type FQFnName =
    | Builtin of Builtin
    | Package of Package
    | Pending of Pending   // NEW

  // ... existing fns ...

  // NEW constructor:
  let fqPending (name : string) (hint : SignatureHint) : FQFnName =
    Pending { handle = System.Guid.NewGuid(); name = name; sigHint = hint }
```

**Subtlety:** `SignatureHint` references `TypeReference`, which is defined *later* in the file (line 212). You'll need to either:
- (a) Use forward declarations with `and`,
- (b) Make `SignatureHint` parametric, or
- (c) Move the `Pending`/`SignatureHint` defs further down in the file (preferred — less invasive).

Recommendation: skip `SignatureHint.paramHints / returnHint` typed fields for Day 1. Store them as `List<string>` and `Option<string>` (raw text). Type-typing is a Day-2 concern.

### B.2 — Fix the match-exhaustiveness errors

There are ~74 match sites on `FQFnName` in LibExecution alone, plus more in other projects. Run:

```bash
./scripts/compile 2>&1 | tee /tmp/build1.log | grep -E "FS0025|FS0049" | head -50
```

For each warning/error, add a `Pending _ -> failwith "TODO pending"` case. This is intentional — we want the runtime to *crash* on a `Pending` for now; the interpreter's new arm (Phase D) is the only place that should match `Pending`.

Heavy files to expect work in:
- `RuntimeTypes.fs` (definition site + internal matches)
- `RuntimeTypesToDarkTypes.fs` (1525 lines — big serialization)
- `ProgramTypesToRuntimeTypes.fs` (1301 lines — but `Pending` only exists at RT; should be minimal new cases)
- `Interpreter.fs` (the meaningful place)
- All Builtins (anything that pretty-prints fn names)
- LibSerialization (binary serializers)

**Trick to spot every match site:** the F# compiler doesn't always warn on every incomplete match. Force it: enable `<WarningsAsErrors>FS0025</WarningsAsErrors>` in the relevant `.fsproj`. (Revert later.)

```bash
git add -A
git commit -m "pdd: scaffold FQFnName.Pending variant + failwiths"
```

### B.3 — Re-build (two-pass)

Per the memory: Dark type changes need two F# build passes.

```bash
./scripts/compile
touch backend/src/LibExecution/package-ref-hashes.txt
./scripts/compile
```

## Phase C — `PackageManager.materializeFn` stub (30 min)

Edit `backend/src/LibExecution/RuntimeTypes.fs:1250`:

```fsharp
type PackageManager =
  { getType : FQTypeName.Package -> Ply<Option<PackageType.PackageType>>
    getValue : FQValueName.Package -> Ply<Option<PackageValue.PackageValue>>
    getFn : FQFnName.Package -> Ply<Option<PackageFn.PackageFn>>

    // NEW:
    materializeFn :
      FQFnName.Pending
        -> Ply<MaterializeResult>

    // ... existing fields ...
  }

and MaterializeResult =
  | MaterializedFn of PackageFn.PackageFn
  | EmptyBody of returnType : Option<TypeReference>
  | Failed of message : string
```

In `PackageManager.empty`, add:

```fsharp
materializeFn = fun _ -> uply { return EmptyBody None }
```

In `PackageManager.withExtras`, pass-through:

```fsharp
materializeFn = pm.materializeFn
```

Also: places that *construct* a `PackageManager` (probably in `LibPackageManager/`, `Tests/TestConfig.fs`, etc.) need the new field. Compiler will tell you.

```bash
./scripts/compile
git add -A
git commit -m "pdd: PackageManager.materializeFn stub returning EmptyBody"
```

## Phase D — Interpreter's `Function(Pending _)` arm (45 min)

Edit `backend/src/LibExecution/Interpreter.fs` around line 304 (the `Function(FQFnName.Package fn)` arm):

```fsharp
| Function(FQFnName.Package fn) ->
    // ... existing path unchanged ...

| Function(FQFnName.Pending p) ->
    uply {
      let! result = exeState.pm.materializeFn p
      match result with
      | EmptyBody returnType ->
          // Construct a synthetic instrData that just loads
          // the default value of `returnType` and returns.
          let defaultDval = Dval.defaultFor (Option.defaultValue TUnit returnType)
          // We want to return from this frame with `defaultDval`.
          // Easiest path: build a 1-instruction synthetic body.
          let instrs : List<Instruction> = [ LoadVal(0, defaultDval) ]
          let instrData = { instructions = List.toArray instrs; resultReg = 0 }
          return instrData
      | MaterializedFn fn ->
          let instrData =
            { instructions = List.toArray fn.body.instructions
              resultReg = fn.body.resultIn }
          return instrData
      | Failed msg ->
          return raiseRTE (RTE.MaterializationFailed(p, msg))
    }
```

(`Dval.defaultFor` is a new function — see Phase E.)

Also: add `RTE.MaterializationFailed` to the RTE union in `RuntimeTypes.fs`. (One line.)

## Phase E — `Dval.defaultFor` (15 min)

Edit `backend/src/LibExecution/Dval.fs`:

```fsharp
let rec defaultFor (t : TypeReference) : Dval =
  match t with
  | TUnit -> DUnit
  | TBool -> DBool false
  | TInt64 -> DInt64 0L
  | TFloat -> DFloat 0.0
  | TString -> DString ""
  | TList _ -> DList(VT.unknown, [])
  | TOption _ -> DEnum(FQTypeName.fqPackage "Option", FQTypeName.fqPackage "Option", [], "None", [])
  // ... fill in the common cases; default DUnit ...
  | _ -> DUnit
```

For Day 1, the test only needs the `TUnit` case to work. Don't over-engineer.

```bash
./scripts/compile
git commit -am "pdd: interpreter handles Pending fns; Dval.defaultFor scaffold"
```

## Phase F — The first passing test (45 min)

Create `backend/tests/Tests/PDD.Tests.fs`:

```fsharp
module Tests.PDD

open Expecto
open Prelude
module RT = LibExecution.RuntimeTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes

let testPendingFnReturnsEmptyBody : Test =
  testTask "pending fn resolves via materializer to EmptyBody (DUnit)" {
    // Build a tiny program: just call a pending fn.
    let pendingFn =
      RT.FQFnName.fqPending "myMissingFn" {
        typeParams = []
        paramHints = []
        returnHint = None
      }

    // Build instructions: Apply(pendingFn, ...)
    let instrs = ... // shape this per CreateLambda / Apply mechanics

    let exeState = ... // use the test harness
    let! result = LibExecution.Execution.execute exeState (None, instrs)
    Expect.equal result (Ok RT.DUnit) "pending fn returned default Unit"
  }

let tests = testList "PDD" [ testPendingFnReturnsEmptyBody ]
```

Add to `backend/tests/Tests/Tests.fsproj`:
```xml
<Compile Include="PDD.Tests.fs" />
```

And register in `Tests.fs` (the entry point):
```fsharp
tests
|> List.append [ Tests.PDD.tests ]
```

Run:
```bash
./scripts/run-backend-tests --filter Tests.PDD
```

When it goes green:
```bash
git commit -am "pdd: first test — pending fn → EmptyBody → DUnit"
```

## Stretch (if time, end of day)

- **Materializer with a precanned response**: instead of always `EmptyBody`, return a hardcoded `PackageFn` whose body adds 1 to its arg. Confirm the test now returns 1+arg.
- **Two pending fns in one program**: confirm both resolve. (No parallelism yet.)
- **Trace emit**: dump JSONL to a file. Look at it.

## What to ignore on Day 1

- LLM calls. None. Stub everything.
- The find path. Skip. Generate-only when you start materializing.
- Parser changes. Hand-construct everything.
- The scheduler / parked frames. Day 4.
- Recursive pending references. Day 5+.

## End-of-day commit checklist

Before quitting:
- `git log --oneline` — should have 4-6 small commits with `pdd:` prefix.
- `./scripts/compile` — green.
- `./scripts/run-backend-tests --filter Tests.PDD` — green.
- `cat pdd-thinking/progress.md` — append a Day-1 done entry.
- `git status` — clean.

## A note on cadence

Don't aim for elegance on Day 1. The branch is throwaway. Every shortcut is fine:
- `failwith` over real error types.
- Pasting code over factoring.
- `// TODO` over fixing.
- Hardcoded test data over fixtures.

Day 2 is when you start making it nice. Day 1 is "prove the pipeline."
