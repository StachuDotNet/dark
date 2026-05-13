# Build-speed refactor — no-op consolidation pass

Goal: **make Tests, LocalExec, and Cli faster to build, with zero functional change.** All three transitively pull in essentially the same set of code (every `Builtins.*` ends up loaded via `Builtins.CliHost`), so the current 19-fsproj fan-out buys us almost no isolation — only overhead.

This is an inventory of moves we should consider, ordered by ROI. Skim the headings and shoot down anything you don't like.

---

## 0. The current shape

19 fsprojects in `backend/`. ~70k LOC of F#, ~54k in `src/`.

```
Prelude
  └── LibConfig
        LibTreeSitter
        LibExecution
              ├── LibSerialization
              │     └── LibDB
              │           ├── LibCloud
              │           └── LibParser
              │
              └── Builtins.{Pure, Random, Time, Http.Client}
                  Builtins.Cli                (+ LibConfig)
                  Builtins.Language           (+ LibTreeSitter)
                  Builtins.Http.Server        (+ LibDB)
                  Builtins.Matter             (+ LibDB, LibSerialization, LibCloud)
                  Builtins.CliHost            (+ all of the above)
                                                          │
                                  ┌───────────────────────┼───────────────────────┐
                                  ▼                       ▼                       ▼
                           Cli (2 files)         LocalExec (8 files)         Tests / TestUtils
```

Builtins-by-the-numbers:

| Project              | .fs files | LOC    |
|---------------------|-----------|--------|
| Builtins.Pure        | 28        | 10,774 |
| Builtins.Matter      | 10        |  3,557 |
| Builtins.Cli         |  9        |  2,918 |
| Builtins.Http.Client |  1        |    921 |
| Builtins.Http.Server |  4        |    714 |
| Builtins.CliHost     |  2        |    583 |
| Builtins.Language    |  3        |    333 |
| Builtins.Random      |  2        |    259 |
| Builtins.Time        |  2        |    208 |
| **total**            | **70**    |**20,267** |

Builtins are **~37% of `backend/src/` LOC** and 9 of the 19 projects.

There are also three empty directories — `Builtins.PM`, `Builtins.DB`, `Builtins.Tracing` — that contain only stale `obj/` and aren't in the sln. Dead weight.

577 `BuiltInFn` record literals total across the 9 projects. 413 of them are in `Builtins.Pure` alone.

---

## 1. The build-cost picture

Why fewer/larger projects is actually faster *for our shape*:

- `dotnet build` parallelizes projects by their dep graph. Our graph already collapses: every end artifact needs essentially every Builtins project, so the parallelism we get from "9 separate Builtins projects" is just three waves (LibExecution-only → DB-needers → CliHost). Same wave structure works with 1–2 Builtins projects.
- Each `fsproj` has fixed per-build overhead: msbuild target evaluation, paket resolution check, fsc startup, project-graph traversal. Compile-script comments already note that `--no-dependencies` is a measurable win, which is a tell that we're paying real money for graph evaluation.
- `Directory.Build.props` already enables `--test:GraphBasedChecking --test:ParallelOptimization --test:ParallelIlxGen`, so within a project, file checking parallelizes. That means **a single ~20k-LOC `Builtins` project compiles roughly as well as nine 2k-LOC projects** for the parallel-checking phase, and avoids the per-project overhead.
- The two-pass build (the embedded `package-ref-hashes.txt` thing) is orthogonal to all of this — fewer projects don't fix it, but they don't worsen it either.

So the right model is: **collapse projects aggressively, keep files small enough that the per-file checker can fan out, and cut the LOC the compiler has to chew through.**

---

## 2. Project consolidation (highest ROI, pure no-op)

### 2a. Collapse the 9 Builtins into 1
Single `Builtins.fsproj` with a `Libs/` subtree:

```
src/Builtins/
  Builtins.fsproj
  Libs/
    Pure/        Int8.fs, Int16.fs, ..., Json.fs, Crypto.fs, ...
    Random/      Random.fs, Uuid.fs
    Time/        Time.fs, DateTime.fs
    Http/        HttpClient.fs, HttpServer.fs, ...
    Cli/         Directory.fs, File.fs, ...
    Language/    Reflection.fs, LanguageTools.fs, Parser.fs
    Matter/      DB.fs, PM/*.fs, Traces.fs
    CliHost/     Cli.fs
  Builtin.fs   (single combine)
```

Dep set is the union of current Builtins deps: `Prelude + LibExecution + LibConfig + LibTreeSitter + LibDB + LibSerialization + LibCloud`. That's the same set `Builtins.CliHost` already pulls in today, and the same set `LocalExec` and `Tests` already pull in. **Nothing gains a new transitive dep.**

Win: 9 fsproj evaluations → 1. One paket restore check instead of nine. One fsc instance for builtins instead of nine. No loss of within-builtins parallelism (GraphBasedChecking handles it).

If you want a tiny safety hedge, split into 2 — e.g. `Builtins.Core` (Pure/Random/Time/Http.Client/Language) and `Builtins.Platform` (Cli/Http.Server/Matter/CliHost). I'd skip the hedge; 1 is fine.

### 2b. Fold `LibCloud` into `LibDB`
`LibCloud` is **5 files / 480 LOC**: `Config.fs` (pass-through), `File.fs` (file IO), `Account.fs` (single-instance stub), `Serialize.fs` (op-bytes ↔ PT), `Toplevels.fs` (sqlite TL storage). Every caller already references `LibDB`. The "cloud" framing is a vestige of multi-tenant Dark; with single-instance, there's no longer a meaningful cloud-vs-db boundary.

Rename `LibCloud.X` → `LibDB.Cloud.X` (or just `LibDB.X`) and delete the project.

### 2c. Fold `LibConfig` into `Prelude`
**2 files / 125 LOC**, used by `LibCloud`, `LibDB`, `Builtins.Cli`. It's just a config DSL + a config record. It belongs in Prelude or in a new `Prelude.Config` namespace.

### 2d. Fold `LibTreeSitter` into `LibParser`
**3 files / 366 LOC.** Only callers are `LibParser` and `Builtins.Language`. After (2a) `Builtins.Language` lives inside the merged Builtins project, which already sits above `LibParser` on the graph. So fold tree-sitter into LibParser.

(Hold off if there's a P/Invoke / native DLL packaging reason it lives separately — quick check the paket.references when doing this.)

### 2e. Fold `TestUtils` into `Tests`
`TestUtils` exists mostly so a Builtins project (`LibTest`) and `Tests` can share it. Once Tests is the only consumer (LocalExec consumes it too — easy to flip), it's just a sub-folder of Tests. Saves one fsproj.

### 2f. Delete the empty `Builtins.PM/`, `Builtins.DB/`, `Builtins.Tracing/` directories
They contain only stale `obj/`. Confusing, useless.

### Net project count
**19 → 7** (or 8 if you keep TestUtils):
`Prelude, LibExecution, LibSerialization, LibDB, LibParser, Builtins, Cli, LocalExec, Tests`. (8 listed; subtract Cli or merge TestUtils to land lower.)

---

## 3. Builtin authoring — collapse the boilerplate

Today each of 577 builtins is a 10-field record literal:

```fsharp
{ name = fn "int8Add" 0
  typeParams = []
  parameters = [ Param.make "a" TInt8 ""; Param.make "b" TInt8 "" ]
  returnType = TInt8
  description = "Adds two 8-bit signed integers together"
  fn = (function | _, vm, _, [ DInt8 a; DInt8 b ] -> ... | _ -> incorrectArgs ())
  sqlSpec = NotYetImplemented
  previewable = Pure
  deprecated = NotDeprecated
  accessibility = Any }
```

For most fns, **5 of those 10 fields are the default**: `typeParams = []`, `sqlSpec = NotYetImplemented`, `previewable = Pure` (or `Impure`), `deprecated = NotDeprecated`, `accessibility = Any`.

### 3a. Builder helper with sane defaults
Add to `LibExecution.Builtin.Shortcuts`:

```fsharp
let pureFn name version params retType desc impl =
  { name = fn name version
    typeParams = []
    parameters = params
    returnType = retType
    description = desc
    fn = impl
    sqlSpec = NotYetImplemented
    previewable = Pure
    deprecated = NotDeprecated
    accessibility = Any }

// ...and impureFn, deprecatedFn, packageOnlyFn, etc.
```

Then call sites become:

```fsharp
pureFn "int8Add" 0
  [ Param.make "a" TInt8 ""; Param.make "b" TInt8 "" ]
  TInt8
  "Adds two 8-bit signed integers together"
  (function | _, vm, _, [ DInt8 a; DInt8 b ] -> ... | _ -> incorrectArgs ())
```

Plain function with positional args. No fluent chains, no SRTP — those would *slow* the F# compiler. This shape collapses each fn from ~10 lines to ~5, drops half the record-literal inference work, and reads better. **Estimated reduction: ~3k LOC across builtins, plus a meaningful inference-time saving on the 577 record literals.**

For the rare fn that needs a non-default field (`previewable = Impure`, `sqlSpec = ...`, deprecated, FromLocation), provide `withImpure`, `withSql`, `withDeprecated`, `withFromLocation` as `BuiltInFn -> BuiltInFn` modifiers used after the builder.

### 3b. Numeric builtins — the boilerplate festival
`Int8/16/32/64/128 + UInt8/16/32/64/128` = 10 files, ~5,800 LOC, ~95% identical shape (Add, Sub, Mul, Div, Mod, Remainder, Negate, Parse, ToString, comparisons, conversions). A crude diff between Int8 and Int16 shows ~228 of 619 lines differ — and most of that diff is just the literal type token swapping.

Options, ranked:

1. **(Recommended)** A single `Numerics.fs` (~1.2–1.5k LOC) that defines per-arity helpers like `arithFn name op DInt8 TInt8` and emits the `BuiltInFn` for every numeric type by table-driven calls. **No SRTP** (SRTP is the slowest thing fsc does). Just plain dispatch on a small algebraic descriptor. Net: ~5,800 LOC → ~1,500 LOC.
2. T4 / source-generation that emits the existing 10 files at build time. Faster compile because there's less inference per file, but adds a build step. Less appealing.
3. Leave the 10 files but apply (3a) — gets you maybe a 30% reduction without the design risk.

Go with option 1, but verify the resulting record literals don't end up giant generic-typed tuples that make inference slow — a quick benchmark run after the rewrite is cheap insurance.

### 3c. Drop the always-`Any` `accessibility` field at call sites
If 90%+ of builtins are `accessibility = Any`, fold it into the builder default and only the `FromLocation _` ones write it. Same idea for `typeParams = []`.

---

## 4. Within-project file consolidation / splitting

### 4a. Split `LibExecution/RuntimeTypes.fs` (2,040 lines)
This is the foundation type module. Splitting respects F#'s file order (so it's a careful refactor), but `GraphBasedChecking` parallelizes file checking once split. Suggested partition:

```
RuntimeTypes/
  Common.fs       (ids, simple types)
  FQNames.fs      (FQ*Name modules)
  TypeReference.fs
  Dval.fs
  Vm.fs           (VMState, ExecutionState)
  Builtins.fs     (BuiltInFn, BuiltInValue, Accessibility)
```

Same applies to `ProgramTypes.fs` (1,133) — split along the same lines.

### 4b. Tackle the `*ToDarkTypes.fs` triplet
`ProgramTypesToDarkTypes.fs` (1,860) + `RuntimeTypesToDarkTypes.fs` (1,525) + `ProgramTypesToRuntimeTypes.fs` (1,308) = **4,693 lines** of hand-written reflection of every PT/RT type to a Darklang-typed Dval and back. These are the slowest single files to compile.

Two paths, not mutually exclusive:
- **Split** into per-type-family files (Expr/Toplevel/Package/Branch/...) so the parallel checker can fan out.
- **Source-generate** them from a tiny schema. PT/RT types are stable enough that a generator pays for itself; the embedded `package-ref-hashes.txt` mechanism already shows we're comfortable with build-time codegen here.

The split is the safe first move; codegen is the bigger win but bigger lift.

### 4c. `Builtins.Cli/Libs/Posix.fs` (1,317 lines)
One file with every POSIX builtin. After (2a) it's just one file in the merged Builtins project — split into `Posix/Process.fs`, `Posix/User.fs`, `Posix/Signal.fs`, etc., to parallelize.

### 4d. `Builtins.Matter/Libs/Traces.fs` (1,028 lines)
Same story.

### 4e. `LibParser/FSharpToWrittenTypes.fs` (1,139) and `WrittenTypesToProgramTypes.fs` (1,013)
Same story. Split per syntactic category.

---

## 5. Lower-leverage but worth flagging

- **`Cli.fsproj` is just `EmbeddedResources.fs` + `Cli.fs`.** Fine as-is, but if you want to be ruthless, fold into a single `LocalExec` with subcommands; the only thing the standalone Cli executable gives you is the AOT publish path.
- **`paket.references` per project** — fewer projects means fewer paket files to keep in sync, and fewer chances of drift.
- **Watch mode for dev**: `dotnet watch build` would amortize fsc startup across edits. Not in scope here, but a related win.
- **`--graph:True`** — the compile script comments mention it speeds things up but breaks something. Worth re-investigating after consolidation; the current bug it triggers may not survive a 7-project graph.

---

## 6. Suggested rollout order (zero functional change at every step)

Each step is independently shippable and reversible.

1. **Delete empty `Builtins.PM/DB/Tracing/` dirs.** 30 seconds.
2. **Add the `pureFn`/`impureFn` builder helpers** to `LibExecution.Builtin.Shortcuts`. Don't migrate any call sites yet. (Sets up step 5.)
3. **Fold `LibConfig` → `Prelude`.** Smallest project merge; warms up the muscle.
4. **Fold `LibCloud` → `LibDB`.** Next-smallest; isolated.
5. **Fold `LibTreeSitter` → `LibParser`.** Verify no native-package issues first.
6. **Collapse the 9 Builtins into 1 (or 2).** Biggest single win. Move sources, not source code; rewrite imports; rebuild.
7. **Migrate builtin call sites to the builder pattern**, file by file. Each file is a small PR.
8. **Rewrite numeric builtins to one `Numerics.fs`.** Largest behavior surface — wants careful test coverage. (Tests project already covers numerics heavily.)
9. **Split the 1k+ LOC files** in LibExecution and LibParser so GraphBasedChecking can parallelize them.
10. **Fold `TestUtils` into `Tests`** if the LocalExec coupling is gone by then.

After step 6 you'll already feel the difference; steps 7–9 compound it.

---

## 7. What I'd avoid

- **SRTP-based generic numerics.** F# SRTP (`^T (static member ...)`) is the single slowest construct fsc has. Compresses code, expands compile time. Don't.
- **Heavy fluent builders with chained lambdas.** Same problem at smaller scale.
- **One mega-file per project.** Loses GraphBasedChecking's parallelism. Sweet spot is probably 100–600 LOC per file.
- **Cross-project `<Compile Include="..." Link="..."/>` sharing.** Doubles work, surprises future-you.

---

## 8. Honest expectation on speed

Per-project overhead and 5,800 LOC of numeric boilerplate are the two biggest concrete wins. Plausible compile-time deltas:

- Project consolidation alone (steps 1–6): **15–25%** off cold backend build, more off warm/incremental builds (because dep-graph traversal cost drops).
- Builder + numeric collapse (steps 7–8): another **5–15%** off Builtins phase.
- Big-file splitting (step 9): another **5–10%**, mostly visible in incremental builds where you only edit one of the splits.

Combined: realistic ballpark **25–40% off cold builds, more on incrementals.** Not transformative — but the bigger win is that the codebase becomes meaningfully smaller and the dep graph becomes the same shape as the actual artifact dep graph instead of an aspirational multi-tenant fan-out.
