# CLI Bootstrap Plan

Goal: Get a basic CLI application running using the newly-bootstrapped setup,
without depending on LibPackageManager or LibBinarySerialization.

## Current Status

- [x] Created `bootstrap` branch with PackagesBootstrap project skeleton
- [x] PackagesBootstrap has PackageManager.fs with in-memory storage
- [x] On `main` branch, wrote export script (blob format)
- [x] Copied PT deserializers into PackagesBootstrap
- [x] Implemented blob loading in PackagesBootstrap
- [x] Wired up CLI to use PackagesBootstrap
- [x] Created DummyBuiltins.fs for pm* stubs
- [x] CLI builds and runs (eval, help, version work)
- [ ] Package browser commands (ls, tree) return empty - need real PM builtins

## Schema

Bootstrap SQLite DB has 3 tables, each with 3 columns:

```sql
CREATE TABLE types (
  id TEXT PRIMARY KEY,      -- UUID as string
  location TEXT NOT NULL,   -- "owner.module1.module2.name"
  data BLOB NOT NULL        -- Binary-serialized PT.PackageType.PackageType
);

CREATE TABLE values (
  id TEXT PRIMARY KEY,
  location TEXT NOT NULL,
  data BLOB NOT NULL        -- Binary-serialized PT.PackageValue.PackageValue
);

CREATE TABLE fns (
  id TEXT PRIMARY KEY,
  location TEXT NOT NULL,
  data BLOB NOT NULL        -- Binary-serialized PT.PackageFn.PackageFn
);
```

Note: The current binary serializers include the `id` field within the serialized
data, so it's duplicated (in both the column and the blob). This is slightly
wasteful but keeps serialization simple for now. Can optimize later.

## Steps

### Phase 1: Export Script (on `main` branch) - COMPLETE

- [x] Add `export-packages` command to LocalExec
- [x] Add `export-blob` command for binary blob format
- [x] Add `verify-export` and `benchmark-*` commands

### Phase 2: Complete PackagesBootstrap (on `bootstrap` branch) - COMPLETE

- [x] Copy binary deserialization code from LibBinarySerialization into PackagesBootstrap
- [x] Implement `loadFromBlob` in PackageManager.fs
- [x] Copy packages-bootstrap.blob to bootstrap branch
- [x] Create module-level singletons (pt, rt) for CLI compatibility

### Phase 3: Wire up CLI (on `bootstrap` branch) - COMPLETE

- [x] Uncomment Cli project in fsdark.sln
- [x] Update Cli to use PackagesBootstrap instead of LibPackageManager
- [x] Update BuiltinCliHost similarly
- [x] Disable BuiltinPM (depends on LibPackageManager)
- [x] Create DummyBuiltins.fs with stub pm* builtins
- [x] Uncomment RT.PackageManager.withExtras
- [x] Update DvalReprDeveloper to use PackagesBootstrap

### Phase 4: Test Basic CLI - PARTIAL

- [x] Run CLI with simple command (version, help) - WORKS
- [x] Test eval command (1L + 2L, string concat) - WORKS
- [x] Test builtin functions (Builtin.int64Add) - WORKS
- [ ] Test package browser (ls, tree, search) - NEEDS REAL PM BUILTINS
- [ ] Test package function calls (Stdlib.List.map) - NEEDS INVESTIGATION

## LibPackageManager Analysis

### What LibPackageManager Does

1. **Data Storage** (SQLite tables):
   - `package_ops` - source of truth, content-addressed ops
   - `package_types`, `package_values`, `package_functions` - projection tables
   - `locations` - maps (owner, modules, name) -> item_id
   - `accounts_v0` - account management
   - `branches` - development branches

2. **Core Operations**:
   - `find*` - lookup ID by location (owner.modules.name)
   - `get*` - get item by ID
   - `getLocation*` - lookup location by ID
   - `search` - complex query with filters, depth, entity types

3. **Composition**:
   - `createInMemory(ops)` - build PM from list of PackageOps
   - `combine(overlay, fallback)` - layer two PMs (first wins)
   - `withExtraOps(basePM, ops)` - combine(createInMemory(ops), basePM)
   - `stabilizeOpsAgainstPM` - ID stabilization for two-phase parsing

4. **Caching**:
   - `withCache` wrapper for memoization

### Current Dummy Builtins

DummyBuiltins.fs provides stubs for:
- `pmGetAccountByName`, `pmGetAccountNameById` - return None
- `pmSearch` - returns empty results
- `pmFindType`, `pmFindValue`, `pmFindFn` - return None
- `pmGetType`, `pmGetValue`, `pmGetFn` - return None
- `pmGetLocationBy*` - return None
- `pmScripts*` - no-op

### What's Needed for Package Browser

The `ls` and `tree` commands call `Builtin.pmSearch`. Currently returns empty.

**Option A: Implement real pmSearch in DummyBuiltins using PackagesBootstrap**
- PackagesBootstrap already has all data in memory
- Add a module-level reference to the loaded store
- Implement search by scanning all items

**Option B: Rewrite CLI ls/tree commands to use PM directly**
- CLI code already has access to `pmRT` (the RT.PackageManager)
- Could call `pm.getType`, etc. directly instead of via builtins

**Option C: Composition approach**
- Create new Darklang-based PM using UserDBs for user packages
- Compose with PackagesBootstrap for stdlib
- `combine(userPM, bootstrapPM)`

### Recommended Approach

For the bootstrap CLI, **Option A** is simplest:

1. Expose `PackagesBootstrap.PackageManager.store` (the loaded PackageStore)
2. Implement `pmSearch` in DummyBuiltins that scans the store:
   - Filter by currentModule prefix
   - Filter by searchText
   - Respect searchDepth (direct vs all descendants)
   - Group by submodules
3. Implement `pmFind*` using store.typeLocationToId, etc.

This gives us a working package browser without needing LibPackageManager or UserDBs.

### Future: Darklang-based Package Manager

Long-term goal: Replace F# package manager with Darklang code + UserDBs.

**UserDB Schema**:
```darklang
// Per-canvas UserDBs
DB TypesById : Dict<Uuid, PackageType>
DB TypesByLocation : Dict<String, Uuid>  // "owner.modules.name" -> id
// Same for Values, Fns

// Or use Darklang.LanguageTools.ProgramTypes directly
```

**Composition**:
```darklang
let userPM = createPMFromUserDBs()
let bootstrapPM = PackagesBootstrap.rt
let pm = PackageManager.combine userPM bootstrapPM
```

This allows:
- User packages stored in UserDBs (mutable)
- Stdlib/builtin packages from bootstrap blob (immutable, fast)
- Seamless lookup across both

## Benchmark Results

| Approach | Total Load | Types | Values | Functions | File Size |
|----------|-----------|-------|--------|-----------|-----------|
| SQLite   | **319ms** | 104ms | 37ms   | 176ms     | 2.4MB     |
| Blob     | **130ms** | 30ms  | 16ms   | 83ms      | 1.7MB     |

**Decision: Use blob format** - ~2.5x faster and 30% smaller.

### Blob Format

```
Header: [typeCount:int32][valueCount:int32][fnCount:int32]
Items:  [idLen:int32][id:bytes][locLen:int32][loc:bytes][dataLen:int32][data:bytes]
```

### Commands

```bash
./scripts/run-local-exec export-packages   # Export to packages-bootstrap.db (SQLite)
./scripts/run-local-exec export-blob       # Export to packages-bootstrap.blob (binary)
./scripts/run-local-exec verify-export     # Verify SQLite DB deserializes correctly
./scripts/run-local-exec benchmark-load    # Benchmark SQLite load time
./scripts/run-local-exec benchmark-blob    # Benchmark blob load time
```

### Export Stats (2024-12-31)
- 599 types, 154 values, 1888 functions (2641 total)
- All items deserialize successfully
- Blob file: 1.7MB, load time ~130ms

## Architecture Design

### Current Flow

1. **CLI Startup** (Cli.fs):
   - Loads PackagesBootstrap.PackageManager.rt as the RT.PackageManager
   - Combines builtins from BuiltinExecution (includes DummyBuiltins) + BuiltinCliHost + BuiltinCli

2. **Script Execution** (Cli.fs line 173):
   - User-defined types/values/fns are grafted onto PM via `withExtras`
   - Composition already works at RT level!

3. **pm* Builtins** (what's broken):
   - Currently in DummyBuiltins.fs returning None/empty
   - Used by Darklang code for package browsing (ls, tree, search)
   - Need real implementations using PackagesBootstrap store

### Three-Layer Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    CLI Commands                          │
│           (ls, tree, search, eval, run)                 │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────┐
│                pm* Builtins (F#)                        │
│     pmSearch, pmFind*, pmGet*, pmGetLocationBy*         │
│                                                         │
│  ┌────────────────────┐  ┌────────────────────────────┐ │
│  │  BootstrapPM.fs    │  │    DummyBuiltins.fs        │ │
│  │  (real lookups)    │  │    (account/script stubs)  │ │
│  └────────────────────┘  └────────────────────────────┘ │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────┐
│            PackagesBootstrap Store                       │
│  (in-memory, loaded from blob at startup)               │
│                                                         │
│  types: Dict<Uuid, PT.PackageType>                      │
│  values: Dict<Uuid, PT.PackageValue>                    │
│  fns: Dict<Uuid, PT.PackageFn>                          │
│  *IdToLocation / *LocationToId bidirectional maps       │
└─────────────────────────────────────────────────────────┘
```

### Implementation Plan

**Phase 5A: BootstrapPM.fs (immediate)**

Create `BuiltinCliHost/Libs/BootstrapPM.fs` with real pm* builtins:
- Read from `PackagesBootstrap.PackageManager.lazyStore.Force()`
- Override DummyBuiltins by including AFTER in builtins list
- Gets `ls`, `tree`, `search` working

**Phase 5B: Future - UserDB-based User Packages**

For user packages (not in bootstrap blob):
```darklang
// Darklang-based PM using UserDBs
DB UserTypes : Dict<String, PackageType>    // location string -> type
DB UserValues : Dict<String, PackageValue>
DB UserFns : Dict<String, PackageFn>

// Composition at lookup time:
// 1. Check UserDB first
// 2. Fall back to bootstrap PM
```

This would require:
- New Darklang PM module with UserDB operations
- pm* builtins that check UserDB first, then fall back to BootstrapPM
- CLI init to set up UserDBs (possibly reset on start?)

**Phase 5C: Future - Script-local packages**

For inline type/value/fn definitions in scripts:
- Already handled by `RT.PackageManager.withExtras` (line 173 in Cli.fs)
- Script parser extracts definitions, grafts onto PM before execution

### Key Files

| File | Purpose |
|------|---------|
| `BuiltinCliHost/Libs/BootstrapPM.fs` | Real pm* builtins using PackagesBootstrap |
| `BuiltinExecution/Libs/DummyBuiltins.fs` | Stubs for pm* (overridden by BootstrapPM) |
| `PackagesBootstrap/PackageManager.fs` | Blob loading, in-memory store, RT.PackageManager |
| `Cli/Cli.fs` | CLI entry point, builtins assembly, PM setup |

## Next Steps

### Immediate (Phase 5A)

1. **Complete BootstrapPM.fs**
   - [x] Created file with pm* implementations
   - [ ] Add to BuiltinCliHost.fsproj
   - [ ] Include in builtinsToUse (after BuiltinExecution to override)
   - [ ] Test `ls` and `tree` commands

### Later

2. **Investigate Stdlib.List.map not found**
   - Parser may be constructing wrong package name?
   - Or name resolution issue?

3. **UserDB-based user packages (Phase 5B)**
   - Design Darklang PM module
   - Implement UserDB operations
   - Compose with bootstrap PM

4. **Clean up**
   - Simplify DummyBuiltins to just account/script stubs
   - Remove unused code paths
