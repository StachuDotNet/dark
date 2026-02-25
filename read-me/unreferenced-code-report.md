# Unreferenced F# Code Report

## Static Analysis Tooling

The F# ecosystem has significant gaps in dead code detection. There is **no tool that detects unused public API members across projects**. The best options:

| Tool | What it finds | CLI/CI ready? |
|------|--------------|---------------|
| F# compiler `--warnon:1182` | Unused parameters (buggy for let-bindings) | Yes |
| FSAC/Ionide (IDE only) | Unused opens, unused declarations | No |
| FSharpLint (`dotnet-fsharplint`) | Useless bindings only | Yes |
| Roslyn analyzers | N/A -- C#/VB only, doesn't work with F# | -- |

To enable FS1182 in a project, add to `.fsproj`:
```xml
<OtherFlags>$(OtherFlags) --warnon:1182</OtherFlags>
```

---

## 1. Entirely Unused Files

| File | Notes |
|------|-------|
| `src/LibCloud/Password.fs` | Module `LibCloud.Password` -- defines `fromPlaintext`, `invalid`, `fromHash`. Zero references anywhere. |
| `src/LibCloud/Stats.fs` | Module `LibCloud.Stats` -- only has two unused type definitions (`DBStat`, `DBStats`) and commented-out code. |

### Orphaned .fs File (on disk but not in any .fsproj)

| File | Notes |
|------|-------|
| `src/LibPackageManager/InMemory/ProgramTypes.fs` | Exists on disk but is not in any `<Compile Include>`. |

---

## 2. Project Missing from Solution

`BuiltinPM` (`src/BuiltinPM/BuiltinPM.fsproj`) is referenced by other projects (LibCloudExecution, BuiltinCliHost, LocalExec) but is **not listed in `fsdark.sln`**. It builds transitively but should probably be added to the solution.

---

## 3. Unused Public `let` Bindings

### Prelude

**Task.fs** -- 10 of 13 public functions unused:
- `Task.foldSequentially`, `Task.mapSequentially`, `Task.mapInParallel`, `Task.execWithSemaphore`, `Task.mapWithConcurrency`, `Task.iterWithConcurrency`, `Task.filterSequentially`, `Task.iterSequentially`, `Task.findSequentially`, `Task.filterMapSequentially`

**NEList.fs** -- 17 unused functions:
- `iter`, `head`, `tail`, `mapWithIndex`, `map2WithIndex`, `ofSeq`, `reverse`, `forall2`, `filter`, `filterMap`, `zip`, `iteri`, `collect`, `mapi`, `append`, `prependList`, `withInitial`

**List.fs**: `count`, `any`

**Map.fs**: `fromListOverwritingDuplicates`, `mapWithIndex`, `filterWithIndex`

**Option.fs**: `unwrap`

**Lazy.fs**: `map`, `bind`

**Tuple2.fs**: `toKeyValuePair`, `mapFirst`, `mapSecond`

**ResizeArray.fs**: `empty`, `iter`, `map`, `toSeq`

**String.fs**: `isEmpty`

**UTF8.fs**: `toBytesOpt`

**Base64.fs** -- nearly all functions unused:
- `fromUrlEncoded`, `fromDefaultEncoded`, `fromEncoded`, `asUrlEncodedString`, `asDefaultEncodedString`, `decode`, `decodeFromString`, `decodeOpt`

**Exception.fs**: `nestedMetadata`, `taskCatch`, `catchError`

**Prelude.fs** -- debug helpers unused:
- `debuGByteArray`, `debugByteArray`, `debugList`, `debuGSet`, `debugSet`, `debuGArray`, `debugArray`, `debuGMap`, `debugMap`, `debugBy`, `debugPly`, `debugTask`, `debugString`, `assertFn3`

### LibExecution

- `RuntimeTypes.consoleReporter`, `RuntimeTypes.consoleNotifier`
- `Dval.dictFromMap`, `Dval.uuid`
- `DvalReprInternalHash.supportedHashVersions`

### LibCloud

**Canvas.fs**: `addDomain`, `loadTLIDsWithContext`, `loadForEvent`, `loadAllDBs`, `loadAllWorkers`, `loadTLIDsWithDBs`

**Account.fs**: `getUserByName`

**Secret.fs**: `insert`, `delete`

**Serialize.fs**: `fetchAllLiveTLIDs`, `fetchActiveCrons`

**UserDB.fs**: `create`, `renameDB`

**Config.fs**: `packageManagerUrl`

### LibService

**Config.fs**: `envDisplayName`, `hostName`, `traceSamplingRuleDefault`

**Logging.fs**: `noLogger`, `consoleLogger`

**HSTS.fs**: `setConfig`

### LibCloudExecution

**CloudExecution.fs**: `reexecuteFunction` -- defined but never called

### LibParser

**Parser.fs**: `parseSimple`

### LibPackageManager

**Queries.fs**: `getAllOpsSince`

**Scripts.fs**: `scriptTypeName`, `fromDT`

---

## 4. Unused Type Definitions

| File | Type |
|------|------|
| `src/LibParser/Utils.fs` | `AvailableTypes` |
| `src/LibCloud/Stats.fs` | `DBStat`, `DBStats` |
| `src/LibExecution/AnalysisTypes.fs` | `FunctionArgHash`, `HashVersion`, `FnName`, `InputVars` (type aliases never used by name) |

---

## 5. Summary

The biggest wins from cleanup:
1. **Delete `Password.fs` and `Stats.fs`** from LibCloud (entirely dead files)
2. **Delete or archive `InMemory/ProgramTypes.fs`** from LibPackageManager (orphaned)
3. **Add `BuiltinPM` to `fsdark.sln`**
4. **Prune Prelude** -- large number of unused Task/NEList/Base64/debug helpers
5. **Remove unused LibCloud functions** -- Canvas loading variants, Account/Secret/UserDB/Serialize functions that are no longer called
