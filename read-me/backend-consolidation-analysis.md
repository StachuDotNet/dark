# Backend F\# Project Consolidation Analysis

**Date:** 2026-01-30 **Status:** Research complete, pending BwdServer
migration

## Executive Summary

The Darklang backend currently has 24 F\# projects (22 source, 2 test)
containing 206 files and \~51k lines of code. A significant portion of
this code (6+ projects, \~28 files) exists solely to support the cloud
execution environment (BwdServer). Once BwdServer is migrated to
Darklang, these projects become obsolete, making a consolidation to 2
projects (Darklang + Darklang.Tests) much more feasible.

## Current Project Structure

### Source Projects (22 total, 181 files)

| Project                | Files | Purpose                                           |
| ---------------------- | ----- | ------------------------------------------------- |
| BuiltinExecution       | 32    | Core builtin functions available everywhere       |
| Prelude                | 20    | F\# utility functions, base types                 |
| LibBinarySerialization | 19    | Binary serialization for Darklang values          |
| LibExecution           | 19    | Core execution engine, RuntimeTypes, ProgramTypes |
| LibCloud               | 15    | **Cloud-only**: Canvas, Routing, Secrets, UserDB  |
| LibPackageManager      | 12    | Package management, loading, caching              |
| LibParser              | 10    | Darklang source code parser                       |
| BuiltinCli             | 10    | CLI-specific builtin functions                    |
| BuiltinDarkInternal    | 7     | Cloud admin operations (dark-internal canvas)     |
| LibService             | 7     | Service infrastructure (logging, telemetry)       |
| LocalExec              | 6     | Local execution, package loading from disk        |
| BuiltinPM              | 5     | Package manager builtins                          |
| BuiltinCliHost         | 3     | CLI host-level builtins                           |
| LibCloudExecution      | 3     | **Cloud-only**: Cloud execution setup             |
| LibTreeSitter          | 3     | Tree-sitter bindings for parsing                  |
| BuiltinCloudExecution  | 2     | **Cloud-only**: Cloud-specific builtins           |
| Cli                    | 2     | CLI entry point                                   |
| LibConfig              | 2     | Configuration management                          |
| BwdServer              | 1     | **Cloud-only**: HTTP server for user canvases     |
| DvalReprDeveloper      | 1     | Developer-facing value representations            |
| LibDB                  | 1     | Database connection utilities                     |
| LibHttpMiddleware      | 1     | **Cloud-only**: HTTP middleware                   |

### Test Projects (2 total, 25 files)

| Project   | Files | Purpose                    |
| --------- | ----- | -------------------------- |
| Tests     | 22    | Main test suite            |
| TestUtils | 3     | Test utilities and helpers |

## Project Dependencies

Most-referenced projects (by ProjectReference count):

    16 references: Prelude, LibExecution
     8 references: LibPackageManager
     6 references: LibDB, LibCloud
     5 references: DvalReprDeveloper
     4 references: LibConfig, LibBinarySerialization
     3 references: BuiltinPM, BuiltinExecution

## Cloud-Only Code Analysis

### What LibCloud Provides

Located in `/backend/src/LibCloud/` (15 files):

  - `Account.fs` - User account management
  - `Canvas.fs` - Canvas CRUD operations
  - `Config.fs` - Cloud-specific configuration
  - `DvalReprInternalHash.fs` - Hashing for cloud storage
  - `DvalReprInternalQueryable.fs` - Queryable representations
  - `DvalReprInternalRoundtrippable.fs` - Serialization for DB storage
  - `File.fs` - File operations (webroot, etc.)
  - `Init.fs` - LibCloud initialization
  - `Password.fs` - Password hashing
  - `Routing.fs` - HTTP request routing to handlers
  - `Secret.fs` - Secret management
  - `Serialize.fs` - Canvas serialization
  - `Stats.fs` - Usage statistics
  - `Tracing.fs` - Request tracing
  - `UserDB.fs` - User database operations

### Who Uses LibCloud

| Consumer                  | What It Uses                                          | Why                                   |
| ------------------------- | ----------------------------------------------------- | ------------------------------------- |
| **BwdServer**             | Canvas, Routing, File, Config, Init                   | Serves HTTP requests to user canvases |
| **LocalExec**             | Canvas.createWithExactID, Canvas.saveTLIDs, Serialize | Saves canvases to DB for BwdServer    |
| **LibCloudExecution**     | Config, general                                       | Cloud execution setup                 |
| **LibHttpMiddleware**     | Routing                                               | HTTP request handling                 |
| **BuiltinCloudExecution** | (indirect)                                            | Cloud-specific builtins               |
| **BuiltinDarkInternal**   | Config reference                                      | Admin operations                      |
| **Tests/TestUtils**       | Various                                               | Test setup                            |

### The LocalExec → LibCloud → BwdServer Chain

`LocalExec/Canvas.fs` loads canvases from disk and saves them to the
database:

<div id="cb2" class="sourceCode">

``` sourceCode fsharp
// LocalExec/Canvas.fs:88-127
do! LibCloud.Canvas.createWithExactID canvasID domain
// ... parse handlers and DBs ...
let tls = (dbs @ handlers) |> List.map (fun tl -> tl, LibCloud.Serialize.NotDeleted)
do! LibCloud.Canvas.saveTLIDs canvasID tls
```

</div>

This exists so BwdServer can look up canvases by domain and route
requests to handlers. If BwdServer is rewritten in Darklang, this entire
mechanism changes.

## Post-BwdServer Migration: What Can Be Deleted

Once BwdServer is migrated to Darklang, these projects become obsolete:

| Project               | Files | Reason                                   |
| --------------------- | ----- | ---------------------------------------- |
| LibCloud              | 15    | All cloud concerns move to Darklang      |
| LibCloudExecution     | 3     | Cloud execution setup no longer needed   |
| BuiltinCloudExecution | 2     | Cloud builtins become Darklang functions |
| BuiltinDarkInternal   | 7     | Admin ops become Darklang functions      |
| BwdServer             | 1     | Replaced by Darklang implementation      |
| LibHttpMiddleware     | 1     | HTTP handling moves to Darklang          |

**Total removable: \~29 files across 6 projects**

Additionally, parts of LocalExec that deal with canvas loading to DB
would be removed or rewritten.

## Consolidation Options

### Option 1: Wait for BwdServer Migration (Recommended)

**Do nothing now.** After BwdServer migration: - Delete the 6 cloud-only
projects - Remaining: \~150 files across \~16 projects - Then
consolidate to 2 projects (Darklang + Darklang.Tests)

**Pros:** - Avoids doing work twice - Cleaner final result - Natural
breakpoint

**Cons:** - Current complexity remains until migration

### Option 2: Consolidate Now to 2 Projects

Merge all 22 source projects into `Darklang.fsproj`.

**Pros:** - Simpler mental model immediately - Easier refactoring across
current boundaries

**Cons:** - 181 files in one project requires careful file ordering (F\#
is order-sensitive) - Would need to redo work after BwdServer migration
- Some code being merged would be deleted soon anyway

### Option 3: Partial Consolidation Now

Merge related small projects while keeping logical boundaries: - All
`Builtin*` → `Darklang.Builtins` - `LibCloud` + `LibCloudExecution` +
`LibDB` → `Darklang.Cloud` - Keep `LibExecution`, `LibParser`, `Prelude`
separate

**Pros:** - Reduces project count (22 → \~10) - Preserves useful
boundaries - Less risky than full consolidation

**Cons:** - Still doing work that may be obsoleted - Intermediate state,
not final

## Technical Considerations for Consolidation

### F\# File Ordering

F\# requires files to be listed in dependency order in `.fsproj`. With
150+ files, this becomes tedious. Current multi-project structure
handles this implicitly via project references.

### Namespace Organization

Without project boundaries enforcing separation, namespace discipline
becomes critical. Current namespaces follow project names (e.g.,
`LibExecution.RuntimeTypes`). These should be preserved.

### Build Performance

  - **Multiple projects**: Can build independent projects in parallel
  - **Single project**: All-or-nothing compilation, but F\# incremental
    builds are good
  - Net effect: Probably minimal difference for a \~50k LOC codebase

### IDE Experience

Single project may improve IDE navigation (no cross-project reference
issues) but could slow down intellisense for very large projects.

## Recommended Next Steps

1.  **Complete BwdServer migration to Darklang** - This is the blocking
    dependency
2.  **Delete cloud-only projects** - LibCloud, LibCloudExecution,
    BuiltinCloudExecution, BuiltinDarkInternal, BwdServer,
    LibHttpMiddleware
3.  **Audit remaining code** - Identify any other dead code paths
4.  **Consolidate to 2 projects**:
      - `Darklang.fsproj` - All source code
      - `Darklang.Tests.fsproj` - All tests
5.  **Establish file ordering** - May need tooling to manage F\# file
    order
6.  **Update build scripts** - Simplify now that there’s only one target

## Files to Reference

  - Project files: `/backend/src/*/\*.fsproj`
  - LocalExec canvas loading: `/backend/src/LocalExec/Canvas.fs`
  - LibCloud modules: `/backend/src/LibCloud/*.fs`
  - BwdServer entry: `/backend/src/BwdServer/Server.fs`
  - Build scripts: `/scripts/build/`

## Open Questions

1.  What’s the timeline for BwdServer migration?
2.  Are there any other consumers of LibCloud not identified here?
3.  Should LibService (logging, telemetry) remain separate or be
    consolidated?
4.  How will the Darklang-based BwdServer handle canvas storage/lookup?
