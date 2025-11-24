# Debug Metadata System: Broader Context

## The Core Problem We're Solving

Runtime types need to be:
- **Lean** - Small memory footprint for execution
- **Verifiable** - Hash ensures content integrity
- **Debuggable** - Meaningful error messages and tooling

But we can't have all three with just a Hash. We need additional metadata that's available when debugging but doesn't bloat runtime execution.

---

## What is "Debug Metadata"?

**Debug Metadata** = Any information that:
1. Is NOT required for correct program execution
2. IS required for human understanding/tooling
3. Can be derived or reconstructed from source

### Examples of Debug Metadata

| Metadata Type | Example | Used For |
|---------------|---------|----------|
| Symbol names | Hash → "Stdlib.String.append" | Error messages |
| Source positions | Hash → file.dark:line 42 | LSP, stack traces |
| Source text | Hash → "type ID = Int64" | Tooltips, docs |
| Dependencies | Hash → [dep1, dep2] | Refactoring, analysis |
| Inference info | Pos → inferred type | Tooltips, verification |
| Profiling data | Hash → 150ms avg | Performance analysis |
| Documentation | Hash → "This type..." | Generated docs |

---

## The PT/RT Split and Debug Metadata

### Three Layers of Representation

```
┌─────────────────────────────────────────┐
│ Source Code (.dark files)               │
│ "type ID = Int64"                       │
└─────────────────┬───────────────────────┘
                  │ Parse
                  ▼
┌─────────────────────────────────────────┐
│ ProgramTypes (PT)                       │
│ - Rich type information                 │
│ - Locations, names, source positions    │
│ - Everything needed for tooling         │
└─────────────────┬───────────────────────┘
                  │ PT2RT + Extract Debug Metadata
                  ▼
┌─────────────────────────────────────────┐    ┌──────────────────┐
│ RuntimeTypes (RT)                       │    │ Debug Metadata   │
│ - Lean execution types                  │◄───│ - Hash→Location  │
│ - Just Hash for references              │    │ - Hash→SourcePos │
│ - Optimized for execution               │    │ - Hash→SourceText│
└─────────────────────────────────────────┘    │ - Dependencies   │
                                                │ - Profiling      │
                                                └──────────────────┘
```

### When Each Layer Matters

| Operation | Uses | Why |
|-----------|------|-----|
| Parsing | Source → PT | Need full structure |
| Type checking | PT | Need full type info |
| Code completion | PT + Debug | Need names, types |
| Execution | RT | Need lean, fast types |
| Error formatting | RT + Debug | Need Hash + location |
| Pretty printing | RT + Debug | Need Hash + location |
| Profiling | RT + Debug | Need Hash + timing |
| Refactoring | PT + Debug | Need full structure + deps |

---

## Design Principles

### 1. Separation of Concerns
- **Runtime** = Correctness + Performance
- **Debug** = Understandability + Tooling

Don't mix them. Runtime shouldn't carry debug baggage.

### 2. Graceful Degradation
If debug metadata is missing/stale:
- Runtime continues working (Hash is sufficient)
- Errors show Hash instead of name
- Tooling degrades (no tooltips, etc.)

Never crash due to missing debug info.

### 3. Rebuild from Source
Debug metadata should be **derivable**:
- If lost, can rebuild from .dark files
- If stale, can refresh
- If inconsistent, can validate

Source is truth; debug metadata is cache.

### 4. Pay for What You Use
- Production deployment: Include basic metadata (error messages)
- Development: Include rich metadata (LSP, profiling)
- CI/testing: Minimal metadata (fast builds)

Configurable granularity.

---

## Where Debug Metadata Fits in Current Architecture

### Current PackageManager
```fsharp
type PackageManager = {
  // Forward lookups (Location → Hash)
  typeLocMap: Map<PackageLocation, Hash>
  fnLocMap: Map<PackageLocation, Hash>
  valueLocMap: Map<PackageLocation, Hash>

  // Reverse lookups (Hash → Item)
  types: Map<Hash, PackageType>
  fns: Map<Hash, PackageFn>
  values: Map<Hash, PackageValue>

  // Missing: Hash → Location (for reverse lookup)
  // This is where debug metadata fits!
}
```

### Enhanced with Debug Metadata
```fsharp
type PackageManager = {
  // Existing (unchanged)
  typeLocMap: Map<PackageLocation, Hash>
  types: Map<Hash, PackageType>
  ...

  // NEW: Debug metadata
  debugMetadata: DebugMetadata
}

type DebugMetadata = {
  // Basic: Hash → Location (solves immediate problem)
  typeLocations: Map<Hash, List<PackageLocation>>  // Multi-valued!
  fnLocations: Map<Hash, List<PackageLocation>>
  valueLocations: Map<Hash, List<PackageLocation>>

  // Rich: Additional debug info (future)
  sourcePositions: Map<Hash, SourcePosition>
  sourceText: Map<Hash, string>
  dependencies: Map<Hash, List<Hash>>
  ...
}
```

### Why Multi-Valued?
```darklang
module A =
  type ID = Int64  // Hash: abc123

module B =
  type ID = Int64  // Hash: abc123 (same!)

// Debug metadata:
// typeLocations[abc123] = [A.ID, B.ID]
//
// Pretty printer can:
// - Show both locations if ambiguous
// - Pick the "closest" one in context
// - Warn about duplicates
```

---

## PT2RT: The Critical Juncture

PT2RT is where we have ALL the information (PT) and need to:
1. Create lean RT for execution
2. Extract debug metadata for tooling

### Current PT2RT (Simplified)
```fsharp
let pt2rtTypeName (pt: PT.FQTypeName) : RT.FQTypeName =
  match pt with
  | PT.FQTypeName.Package ref ->
    RT.FQTypeName.Package ref.id  // Just extract Hash
  | PT.FQTypeName.Builtin b ->
    RT.FQTypeName.Builtin b

// Location info is lost!
```

### Enhanced PT2RT with Debug Extraction
```fsharp
let pt2rtTypeName
  (debugDB: DebugMetadata)
  (pt: PT.FQTypeName)
  : RT.FQTypeName =
  match pt with
  | PT.FQTypeName.Package ref ->
    // Extract debug metadata
    debugDB.recordTypeLocation ref.id ref.location

    // Create lean RT
    RT.FQTypeName.Package ref.id

  | PT.FQTypeName.Builtin b ->
    RT.FQTypeName.Builtin b

// Location info preserved in debug DB!
```

### Where PT2RT Happens
```fsharp
// Converting whole toplevels
let pt2rtToplevel (tl: PT.Toplevel) : RT.Toplevel =
  match tl with
  | PT.Toplevel.TLFunction fn ->
    let rtFn = pt2rtFunction fn  // Recursively converts all type refs
    RT.Toplevel.TLFunction rtFn

  | PT.Toplevel.TLType typ ->
    let rtTyp = pt2rtType typ
    RT.Toplevel.TLType rtTyp

// Each type reference in the function body/params gets converted
// Each conversion should record debug metadata
```

**Challenge**: PT2RT is currently stateless (pure functions). Adding debug metadata recording makes it stateful.

**Solutions**:
1. **Pass debugDB through**: Thread it as parameter
2. **Global mutable**: Use global ref (simpler but impure)
3. **Writer monad**: Collect metadata as side output
4. **Two-pass**: First PT→RT, then scan RT for debug extraction

---

## Debug Metadata Lifecycle

### 1. Creation (Parse Time)
```darklang
// Parser creates PT with full location
let typeDecl = parseTypeDef source
let location = getCurrentLocation()

PT.PackageType {
  id = computeHash typeDecl
  location = location  // Available!
  declaration = typeDecl
  ...
}
```

### 2. Recording (PT2RT Time)
```fsharp
// PT2RT extracts and records debug metadata
let pt2rtTypeName (debugDB: DebugDB) (pt: PT.FQTypeName) =
  match pt with
  | PT.FQTypeName.Package ref ->
    debugDB.record ref.id ref.location  // Record here!
    RT.FQTypeName.Package ref.id
```

### 3. Persistence (Storage Time)
```fsharp
// Save to database/file
debugDB.flush()  // Write to SQLite/JSON/etc.
```

### 4. Loading (Runtime Startup)
```fsharp
// Load debug metadata alongside PM
let pm = PackageManager.load()
let debugDB = DebugMetadata.load()

// Make available globally or pass around
```

### 5. Querying (Error/Pretty Print Time)
```fsharp
// Error formatter queries debug DB
let formatError fnHash =
  match debugDB.getFnLocation fnHash with
  | Some loc -> $"Error in {loc}"
  | None -> $"Error in {fnHash}"
```

### 6. Invalidation (Source Change)
```fsharp
// When .dark file changes
onSourceChange (file: string) =
  // Re-parse affected file
  let newPT = parse file

  // Re-extract debug metadata
  let newDebugData = extractDebugMetadata newPT

  // Update debug DB
  debugDB.update newDebugData

  // Runtime continues with existing RT (until recompile)
```

---

## Alternative Architectures

### Architecture A: Debug DB in PackageManager
```
┌────────────────────────────┐
│ PackageManager             │
│ ┌────────────────────────┐ │
│ │ Types, Fns, Values     │ │
│ └────────────────────────┘ │
│ ┌────────────────────────┐ │
│ │ Debug Metadata         │ │
│ │ - typeLocations        │ │
│ │ - fnLocations          │ │
│ └────────────────────────┘ │
└────────────────────────────┘
```

**Pros**: Centralized, easy to query
**Cons**: Mixes concerns, PM becomes larger

### Architecture B: Separate Debug Database
```
┌────────────────────┐  ┌──────────────────┐
│ PackageManager     │  │ DebugDatabase    │
│ - Types            │  │ - typeLocations  │
│ - Fns              │  │ - fnLocations    │
│ - Values           │  │ - sourcePos      │
└────────────────────┘  └──────────────────┘
        ▲                        ▲
        │                        │
        └────── Both loaded ─────┘
```

**Pros**: Clean separation, can disable debug
**Cons**: Two databases, synchronization

### Architecture C: Debug Metadata Service
```
┌─────────────┐
│ Application │
└──────┬──────┘
       │
       ├──────► PackageManager (runtime data)
       │
       └──────► DebugMetadataService (debug data)
                ├── In-memory cache
                ├── Persistent storage
                └── Source file scanner
```

**Pros**: Service can be local/remote, cacheable, rebuildable
**Cons**: More complex, network latency if remote

---

## Open Architectural Questions

### Q1: Should PT Include Location at All?

**Option 1**: PT has location (current proposal)
```fsharp
type PT.PackageRef = {
  id: Hash
  location: PackageLocation  // Part of PT
}
```

**Option 2**: PT has just Hash, debug metadata separate
```fsharp
type PT.PackageRef = {
  id: Hash
  // No location here!
}

// Location stored only in debug DB
type DebugMetadata = {
  typeLocations: Map<Hash, PackageLocation>
}
```

**Trade-off**: Option 1 is simpler (location travels with PT), Option 2 is purer (PT truly just structure).

---

### Q2: When to Resolve Locations?

**Option A**: Resolve at parse time (early)
```darklang
// Parser immediately resolves type references
"MyModule.ID" → lookup → Hash abc123, Location MyModule.ID
```

**Option B**: Resolve at PT2RT time (late)
```fsharp
// PT stores unresolved references
PT.UnresolvedRef "MyModule.ID"

// PT2RT resolves them
PT2RT: "MyModule.ID" → lookup → Hash abc123
```

**Trade-off**: Option A is simpler, Option B allows multi-phase parsing.

---

### Q3: How to Handle Hash Collisions in Debug Metadata?

When two types have same Hash (structurally identical):

**Option A**: Store all locations (multi-valued map)
```fsharp
typeLocations[abc123] = [A.ID, B.ID, C.ID]
```

**Option B**: Store only first/canonical location
```fsharp
typeLocations[abc123] = A.ID  // First one wins
```

**Option C**: Store all + mark canonical
```fsharp
typeLocations[abc123] = {
  canonical = A.ID
  aliases = [B.ID, C.ID]
}
```

**Recommendation**: Option A (store all), let pretty printer/error formatter choose best one for context.

---

### Q4: Scope of Debug Metadata?

**Option A**: Global (all packages, all branches)
```fsharp
// One giant debug DB
debugDB.getTypeLocation branchID=None hash
```

**Option B**: Per-branch
```fsharp
// Separate debug DB per branch
debugDB.getTypeLocation branchID=Some(uuid) hash
```

**Option C**: Per-package
```fsharp
// Each package has its own debug metadata
package.debugMetadata.getTypeLocation hash
```

**Recommendation**: Per-branch (matches PM architecture), with optional global fallback.

---

### Q5: Format for Persistence?

**Option A**: SQLite (structured, queryable)
```sql
CREATE TABLE debug_type_locations (
  hash TEXT,
  location TEXT
);
```

**Option B**: JSON (human-readable, versionable)
```json
{
  "types": {
    "abc123": ["A.ID", "B.ID"]
  }
}
```

**Option C**: Binary (compact, fast)
```
Same format as package serialization
```

**Recommendation**: SQLite for flexibility, indexed queries, and debugging.

---

## Implementation Roadmap

### Phase 1: Minimal Viable Debug Metadata
1. Add `location: PackageLocation` to PT.PackageRef (required)
2. Create `DebugMetadata` type with `typeLocations: Map<Hash, List<PackageLocation>>`
3. Thread `debugDB` through PT2RT
4. Record location during PT2RT conversion
5. Update error formatter to query debugDB
6. Update pretty printer to query debugDB

**Goal**: Fix hash collision issue, get better error messages.

### Phase 2: Persistence
1. Add SQLite tables for debug metadata
2. Save debug metadata during package compilation
3. Load debug metadata on PM startup
4. Handle missing/stale metadata gracefully

**Goal**: Debug metadata survives across runs.

### Phase 3: Rich Metadata
1. Add source positions
2. Add source text
3. Add dependency graph
4. Add profiling hooks

**Goal**: Enable LSP, refactoring tools, profilers.

### Phase 4: Tooling Integration
1. LSP "go to definition" uses debug metadata
2. Hover tooltips use debug metadata
3. Refactoring tools use dependency graph
4. Profiler visualizes hot spots

**Goal**: Full developer tooling experience.

---

## Comparison with Other Languages

### Go: DWARF Debug Info
- Separate `.debug` section in binary
- Contains symbol names, source positions, types
- Stripped in production (`go build -ldflags="-s -w"`)
- Debuggers read DWARF to show source

**Similar to our approach**: Separate debug metadata, optional in production.

### Rust: `.pdb`/`.dSYM` files
- Debug symbols in separate files
- Not shipped with release binaries
- Debuggers load symbols on demand

**Similar to our approach**: Separate debug database.

### JavaScript: Source Maps
- Map minified code → original source
- JSON format, separate file
- Browser dev tools load on demand

**Similar to our approach**: Derivable from source, loaded when debugging.

### Erlang: `.beam` files with debug_info
- Compiled modules can include debug_info chunk
- Used for crash dumps, tracing
- Optional (can compile without)

**Similar to our approach**: Debug metadata alongside runtime code, optional.

---

## Recommendation

**Implement Solution 4a (Minimal Debug Metadata) as follows:**

1. **PT Structure**:
   ```fsharp
   type PT.PackageRef = {
     id: Hash
     location: PackageLocation  // Required
   }
   ```

2. **RT Structure** (unchanged):
   ```fsharp
   type RT.Package = Hash  // Just the Hash
   ```

3. **Debug Metadata** (new):
   ```fsharp
   type DebugMetadata = {
     typeLocations: Map<Hash, List<PackageLocation>>
     fnLocations: Map<Hash, List<PackageLocation>>
     valueLocations: Map<Hash, List<PackageLocation>>
   }
   ```

4. **Storage** (in PackageManager for now):
   ```fsharp
   type PackageManager = {
     // Existing
     types: Map<Hash, PackageType>
     ...

     // New
     debugMetadata: DebugMetadata
   }
   ```

5. **PT2RT** (extract debug metadata):
   ```fsharp
   let pt2rtTypeName (pt: PT.FQTypeName) : RT.FQTypeName =
     match pt with
     | PT.FQTypeName.Package ref ->
       pm.debugMetadata.recordTypeLocation ref.id ref.location
       RT.FQTypeName.Package ref.id
   ```

6. **Error Formatting** (query debug metadata):
   ```fsharp
   let formatError fnHash =
     match pm.debugMetadata.getFnLocations fnHash with
     | [] -> $"Error in {fnHash}"
     | [loc] -> $"Error in {formatLocation loc}"
     | locs -> $"Error in {fnHash} (one of: {formatLocations locs})"
   ```

This gives us:
- ✅ Lean runtime (RT is just Hash)
- ✅ Rich debugging (location available)
- ✅ Fixes hash collision issue
- ✅ Foundation for future tooling
- ✅ Clear separation of concerns
