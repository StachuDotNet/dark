# Task: Remove Ply from the Codebase

## Overview
Remove Ply library in favor of F#'s native `task` computation expression. The codebase has already started this migration (33 files use `task {`), but 61 files still use `uply {`.

## Research Summary
- **Ply usage**: 61 files use `uply {` or `ply {`
- **Type annotations**: ~250+ `Ply<T>` type annotations across the codebase
- **Ply module functions**: Used in ~30 locations (Ply.List.mapSequentially, etc.)
- **Existing Task module**: Already provides many equivalent functions
- **Missing functions in Task module**:
  - `foldSequentiallyWithIndex`
  - `mapSequentiallyWithIndex`
  - NEList, Map, Result, Option helper modules
- **Package reference**: `nuget Ply = 0.3.1` in paket.dependencies

## Implementation Plan

### Phase 1: Extend Task Module (Add Missing Functionality)
- [x] Add `foldSequentiallyWithIndex` to Task module
- [x] Add `mapSequentiallyWithIndex` to Task module
- [x] Add Task.NEList submodule with `mapSequentially`
- [x] Add Task.Map submodule with `foldSequentially`, `mapSequentially`, `filterSequentially`, `filterMapSequentially`
- [x] Add Task.Result submodule with `map`
- [x] Add Task.Option submodule with `map`

### Phase 2: Convert File-by-File (High Priority Core Files)
- [x] Convert backend/src/LibExecution/Interpreter.fs
- [x] Convert backend/src/LibExecution/TypeChecker.fs
- [x] Convert backend/src/LibExecution/Execution.fs
- [x] Convert backend/src/LibExecution/RuntimeTypes.fs
- [x] Convert backend/src/LibExecution/ProgramTypes.fs

### Phase 3: Convert LibParser Files
- [x] Convert backend/src/LibParser/WrittenTypesToProgramTypes.fs
- [x] Convert backend/src/LibParser/NameResolver.fs
- [x] Convert backend/src/LibParser/Canvas.fs
- [x] Convert backend/src/LibParser/Package.fs
- [x] Convert backend/src/LibParser/TestModule.fs

### Phase 4: Convert LibPackageManager Files
- [x] Convert backend/src/LibPackageManager/PackageManager.fs
- [x] Convert backend/src/LibPackageManager/ProgramTypes.fs
- [x] Convert backend/src/LibPackageManager/RuntimeTypes.fs
- [x] Convert backend/src/LibPackageManager/Stats.fs
- [x] Convert backend/src/LibPackageManager/Sync.fs
- [x] Convert backend/src/LibPackageManager/Caching.fs
- [x] Convert backend/src/LibPackageManager/Instances.fs

### Phase 5: Convert Builtin Libraries
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]
- [x]

### Phase 6: Convert LibCloud and Other Infrastructure
- [ ] Convert backend/src/LibCloud/UserDB.fs
- [ ] Convert backend/src/LibCloud/Canvas.fs
- [ ] Convert backend/src/LibCloud/DvalReprInternalQueryable.fs
- [ ] Convert backend/src/LibCloudExecution/CloudExecution.fs
- [ ] Convert backend/src/LibDB/Db.fs
- [ ] Convert backend/src/LocalExec/LocalExec.fs
- [ ] Convert backend/src/LocalExec/Canvas.fs
- [ ] Convert backend/src/LocalExec/LoadPackagesFromDisk.fs
- [ ] Convert backend/src/Cli/Cli.fs

### Phase 7: Convert Prelude and Test Files
- [ ] Convert backend/src/Prelude/Prelude.fs
- [ ] Convert backend/tests/Tests/Prelude.Tests.fs
- [ ] Convert backend/tests/Tests/BwdServer.Tests.fs
- [ ] Convert backend/tests/Tests/HttpClient.Tests.fs
- [ ] Convert backend/tests/Tests/LibExecution.Tests.fs
- [ ] Convert backend/tests/TestUtils/TestUtils.fs
- [ ] Convert backend/tests/TestUtils/LibTest.fs

### Phase 8: Remove Ply Infrastructure
- [ ] Delete backend/src/Prelude/Ply.fs
- [ ] Remove Ply package reference from backend/paket.dependencies
- [ ] Run `paket install` to update lock file
- [ ] Remove any remaining Ply references in project files

### Phase 9: Testing and Verification
- [ ] Run full test suite to verify all conversions work
- [ ] Check build logs for any compilation errors
- [ ] Verify no remaining references to Ply in the codebase

## Conversion Pattern

For each file:
1. Replace `uply {` with `task {`
2. Replace `Ply<T>` type annotations with `Task<T>`
3. Replace `Ply.List.*` with `Task.*` (for list operations)
4. Replace `Ply.Map.*` with `Task.Map.*`
5. Replace `Ply.NEList.*` with `Task.NEList.*`
6. Replace `Ply.Result.*` with `Task.Result.*`
7. Replace `Ply.Option.*` with `Task.Option.*`
8. Replace `Ply.toTask` with identity (just remove it)
9. Replace `Ply(value)` with `Task.FromResult(value)`
10. Replace `Ply.map` and `Ply.bind` with `Task.map` and `Task.bind`

## Notes
- The Task module already has most of the functionality needed
- Need to add missing helper functions first (Phase 1)
- Some files may have both `task {` and `uply {` - need to convert all to `task {`
- Build system auto-rebuilds, so wait for compilation after each phase
