# Solution 4: Debug Metadata System

## Core Idea

Keep runtime types lean (just Hash), store location and other debug information in a separate **debug metadata system** that's consulted only when needed (error messages, pretty printing, tooling).

---

## Two-Tier Architecture

### Tier 1: ProgramTypes (Rich, Source Representation)
```fsharp
module PT.FQTypeName =
  type PackageRef = {
    id: Hash
    location: PackageLocation  // REQUIRED - always present at parse time
  }
  type FQTypeName = Package of PackageRef
```

### Tier 2: RuntimeTypes (Lean, Execution Representation)
```fsharp
module RT.FQTypeName =
  type Package = Hash  // Just the Hash - minimal runtime overhead
  type FQTypeName = Package of Hash
```

### Tier 3: Debug Metadata (Separate, Queryable)
```fsharp
type DebugMetadata = {
  // Hash → original location (at parse time)
  hashToLocation: Map<Hash, PackageLocation>

  // Location → Hash (for lookups)
  locationToHash: Map<PackageLocation, Hash>

  // Source positions (for better errors)
  hashToSourcePos: Map<Hash, SourcePosition>

  // Original source text (for tooltips, etc.)
  hashToSourceText: Map<Hash, string>

  // Dependency graph (for tooling)
  typeDependencies: Map<Hash, List<Hash>>

  // ... other debug info as needed
}
```

---

## How It Works

### At Parse Time (PT)
```darklang
// Parser always creates PT with full location info
let parseTypeDeclaration (source: String) : PT.PackageType =
  let location = getCurrentLocation()
  let declaration = parseDeclaration(source)
  let hash = computeHash(declaration)

  // Store in debug metadata
  DebugMetadata.record hash location source

  PT.PackageType {
    id = hash
    location = location  // Always populated
    declaration = declaration
    ...
  }
```

### At PT→RT Conversion
```fsharp
// Conversion strips location, keeps just Hash
let pt2rt (ptTypeName: PT.FQTypeName.PackageRef) : RT.FQTypeName.Package =
  ptTypeName.id  // Just extract the Hash

// Debug metadata already recorded during parse
// No need to carry it through runtime
```

### At Error Formatting Time
```fsharp
// Error formatter queries debug metadata
let formatError (fnHash: Hash) (error: RuntimeError) : string =
  match DebugMetadata.getLocation fnHash with
  | Some loc ->
    $"Error in function {formatLocation loc}: {error.message}"
  | None ->
    // Fallback: no debug info available
    $"Error in function {fnHash}: {error.message}"
```

### At Pretty Print Time
```darklang
// Pretty printer queries debug metadata
let prettyPrintType (hash: String) : String =
  match DebugMetadata.getLocation hash with
  | Some loc -> formatTypeName loc currentContext
  | None -> hash  // Fallback to Hash
```

---

## Where Does Debug Metadata Live?

### Option A: In PackageManager
```fsharp
type PackageManager = {
  // Existing package data
  types: Map<Hash, PackageType>
  fns: Map<Hash, PackageFn>
  ...

  // NEW: Debug metadata tables
  debugTypeLocations: Map<Hash, PackageLocation>
  debugFnLocations: Map<Hash, PackageLocation>
  debugSourcePositions: Map<Hash, SourcePosition>
  ...
}
```

**Pros:**
- ✅ Centralized with other package data
- ✅ Easy to query alongside package lookups
- ✅ Survives across runs (persisted with PM)

**Cons:**
- ❌ Mixes runtime data with debug data
- ❌ PM becomes larger/more complex
- ❌ Debug data loaded even when not needed

---

### Option B: Separate DebugDatabase
```fsharp
// Completely separate from PackageManager
type DebugDatabase = {
  typeLocations: Map<Hash, PackageLocation>
  fnLocations: Map<Hash, PackageLocation>
  sourcePositions: Map<Hash, SourcePosition>
  sourceText: Map<Hash, string>
  ...
}

// Loaded only when needed
let debugDB = lazy (DebugDatabase.load())

// Error formatter uses it
let formatError (hash: Hash) =
  match debugDB.Value.typeLocations.TryFind(hash) with
  | Some loc -> ...
```

**Pros:**
- ✅ Clean separation (runtime vs debug)
- ✅ Can disable/strip in production
- ✅ Can grow without affecting PM
- ✅ Multiple debug databases (per-branch?)

**Cons:**
- ❌ More files/databases to manage
- ❌ Synchronization issues (PM vs DebugDB)
- ❌ Extra lookups (two database queries)

---

### Option C: In-Memory Only (Rebuild from Source)
```fsharp
// Don't persist debug metadata
// Rebuild it from source files on demand

type DebugMetadataBuilder = {
  rebuild: unit -> DebugMetadata
}

let getDebugInfo (hash: Hash) : Option<PackageLocation> =
  // Parse all .dark files
  // Extract locations for all types
  // Find the one matching this hash
  ...
```

**Pros:**
- ✅ No persistence needed
- ✅ Always fresh (reflects current source)
- ✅ No stale metadata

**Cons:**
- ❌ Slow (reparse on every error?)
- ❌ Requires source files available
- ❌ Can't work with compiled/binary packages

---

### Option D: Hybrid (Cache + Rebuild)
```fsharp
type DebugMetadata = {
  // In-memory cache
  mutable cache: Map<Hash, PackageLocation>

  // Persistent storage (SQLite)
  db: DebugDatabase

  // Source files (for rebuild)
  sourceDir: string
}

let getLocation (hash: Hash) : Option<PackageLocation> =
  // Try cache first
  match cache.TryFind(hash) with
  | Some loc -> Some loc
  | None ->
    // Try persistent DB
    match db.query(hash) with
    | Some loc ->
      cache <- cache.Add(hash, loc)
      Some loc
    | None ->
      // Rebuild from source
      let loc = rebuildFromSource(hash)
      cache <- cache.Add(hash, loc)
      db.insert(hash, loc)
      loc
```

**Pros:**
- ✅ Fast (cache)
- ✅ Persistent (DB)
- ✅ Self-healing (rebuild)

**Cons:**
- ❌ Most complex
- ❌ Cache invalidation issues

---

## Storage Schema (If Persisted)

### SQLite Tables
```sql
-- Type debug info
CREATE TABLE debug_type_locations (
  hash TEXT PRIMARY KEY,
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,  -- JSON array
  name TEXT NOT NULL,
  source_file TEXT,
  source_line INTEGER,
  source_col INTEGER
);

-- Function debug info
CREATE TABLE debug_fn_locations (
  hash TEXT PRIMARY KEY,
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,
  name TEXT NOT NULL,
  source_file TEXT,
  source_line INTEGER,
  source_col INTEGER
);

-- Source text (for tooltips)
CREATE TABLE debug_source_text (
  hash TEXT PRIMARY KEY,
  source_text TEXT NOT NULL
);

-- Dependency graph
CREATE TABLE debug_type_dependencies (
  type_hash TEXT NOT NULL,
  depends_on_hash TEXT NOT NULL,
  PRIMARY KEY (type_hash, depends_on_hash)
);
```

### Indexes
```sql
CREATE INDEX idx_type_location ON debug_type_locations(owner, modules, name);
CREATE INDEX idx_fn_location ON debug_fn_locations(owner, modules, name);
CREATE INDEX idx_source_file ON debug_type_locations(source_file);
```

---

## PT→RT Conversion with Debug Metadata

### Current PT2RT Process
```fsharp
// Convert PT types to RT types
let pt2rtTypeName (pt: PT.FQTypeName) : RT.FQTypeName =
  match pt with
  | PT.FQTypeName.Package ref -> RT.FQTypeName.Package ref.id
  | PT.FQTypeName.Builtin b -> RT.FQTypeName.Builtin b
```

### Enhanced with Debug Recording
```fsharp
let pt2rtTypeName
  (debugDB: DebugDatabase)
  (pt: PT.FQTypeName)
  : RT.FQTypeName =
  match pt with
  | PT.FQTypeName.Package ref ->
    // Record debug info during conversion
    debugDB.recordTypeLocation ref.id ref.location

    // Return lean RT version
    RT.FQTypeName.Package ref.id

  | PT.FQTypeName.Builtin b ->
    RT.FQTypeName.Builtin b
```

### Bulk PT2RT for Whole Canvas
```fsharp
// Convert entire canvas PT → RT
let pt2rtCanvas (canvas: PT.Canvas) : RT.Canvas =
  // Create/open debug database
  use debugDB = DebugDatabase.create()

  // Convert all toplevels
  let rtToplevels =
    canvas.toplevels
    |> List.map (pt2rtToplevel debugDB)

  // Debug info automatically recorded
  debugDB.flush()

  { toplevels = rtToplevels; ... }
```

---

## Use Cases for Debug Metadata

### 1. Runtime Error Messages
```fsharp
// RTE occurs during execution
match RuntimeError.catch() with
| FnError(fnHash, paramName, expected, actual) ->
  match DebugDB.getFnLocation(fnHash) with
  | Some loc ->
    $"Error in function {loc.name}: parameter '{paramName}' expected {expected} but got {actual}"
  | None ->
    $"Error in function {fnHash}: parameter '{paramName}' expected {expected} but got {actual}"
```

### 2. Pretty Printing
```darklang
let prettyPrintTypeRef (hash: String) : String =
  match DebugDB.getTypeLocation hash with
  | Some loc -> formatTypeName loc currentContext
  | None -> hash
```

### 3. LSP "Go to Definition"
```fsharp
// User clicks on type reference
let gotoDefinition (hash: Hash) : Option<SourcePosition> =
  DebugDB.getSourcePosition(hash)
```

### 4. Type Hover Tooltips
```fsharp
// User hovers over type reference
let getTooltip (hash: Hash) : string =
  match DebugDB.getTypeLocation(hash), DebugDB.getSourceText(hash) with
  | Some loc, Some source ->
    $"{formatLocation loc}\n\n{source}"
  | Some loc, None ->
    formatLocation loc
  | None, _ ->
    hash.toString()
```

### 5. Dependency Analysis
```fsharp
// Show all types that depend on this one
let findDependents (hash: Hash) : List<PackageLocation> =
  DebugDB.getTypeDependents(hash)
  |> List.map (fun h -> DebugDB.getTypeLocation(h))
  |> List.choose id
```

### 6. Refactoring Tools
```fsharp
// Rename type: update debug metadata
let renameType (oldLoc: PackageLocation) (newLoc: PackageLocation) =
  match DebugDB.getHashAtLocation(oldLoc) with
  | Some hash ->
    DebugDB.updateLocation(hash, newLoc)
    // Runtime continues to work (Hash unchanged)
  | None -> ()
```

---

## Comparison with Other Solutions

| Aspect | Sol 1 (PackageRef) | Sol 4 (Debug Metadata) |
|--------|-------------------|------------------------|
| Runtime size | ❌ Hash + Location | ✅ Just Hash |
| Error messages | ✅ Location in reference | ✅ Query debug DB |
| Pretty printing | ✅ Location in reference | ✅ Query debug DB |
| Serialization size | ❌ Larger | ✅ Smaller |
| Lookup speed | ✅ Direct access | ⚠️ Extra DB query |
| Stale locations | ✅ Degrades gracefully | ✅ Degrades gracefully |
| Tooling support | ⚠️ Limited | ✅ Rich (source pos, deps, etc.) |
| Complexity | ⚠️ Change all refs | ⚠️ New subsystem |

---

## Migration Path

### Step 1: Add DebugDatabase
```fsharp
// Create new debug database subsystem
type DebugDatabase = {
  typeLocations: ConcurrentDict<Hash, PackageLocation>
  ...
}

// Initialize alongside PM
let debugDB = DebugDatabase.create()
```

### Step 2: Update PT to Include Location
```fsharp
// PT.FQTypeName already has location
type PackageRef = {
  id: Hash
  location: PackageLocation  // Make this required
}
```

### Step 3: Record Debug Info During PT2RT
```fsharp
// Enhanced PT2RT records to debug DB
let pt2rtTypeName (pt: PT.FQTypeName) : RT.FQTypeName =
  match pt with
  | PT.FQTypeName.Package ref ->
    debugDB.recordTypeLocation(ref.id, ref.location)
    RT.FQTypeName.Package ref.id
```

### Step 4: Update Error Formatters
```fsharp
// Error formatters query debug DB
let formatError (fnHash: Hash) =
  match debugDB.getFnLocation(fnHash) with
  | Some loc -> $"In function {formatLocation loc}"
  | None -> $"In function {fnHash}"
```

### Step 5: Update Pretty Printers
```darklang
// Pretty printers query debug DB
let prettyPrintType (hash: String) : String =
  match Builtin.debugGetTypeLocation hash with
  | Some loc -> formatTypeName loc currentContext
  | None -> hash
```

---

## Open Questions

### 1. When to Build Debug Metadata?
- **Option A**: During parse (PT creation)
- **Option B**: During PT→RT conversion
- **Option C**: On-demand (when error occurs)

**Recommendation**: During PT→RT (centralized, happens once per toplevel)

### 2. One Database or Per-Branch?
- **Option A**: Single global debug DB (all branches)
- **Option B**: Per-branch debug DB

**Recommendation**: Per-branch (different branches might have different locations for same Hash)

### 3. How to Handle Missing Debug Info?
- **Option A**: Fail hard (crash if not found)
- **Option B**: Degrade gracefully (show Hash)
- **Option C**: Rebuild from source

**Recommendation**: Degrade gracefully, optionally warn

### 4. Should Debug Metadata be Versioned?
If source changes, debug metadata might become stale:
- **Option A**: Ignore (debug info is best-effort)
- **Option B**: Version it (invalidate on source change)
- **Option C**: Timestamp it (show "as of X")

**Recommendation**: Best-effort, rebuild from source if critical

### 5. Production Deployment?
- **Option A**: Include debug DB in production
- **Option B**: Strip debug DB in production
- **Option C**: Configurable (include for beta, strip for release)

**Recommendation**: Include in production (better error messages worth the space)

---

## Subsolutions and Variations

### 4a: Minimal Debug Metadata (Just Location)
Only store Hash→Location mapping, nothing else.

**Pros**: Simple, solves immediate problem
**Cons**: Misses opportunity for richer tooling

### 4b: Rich Debug Metadata (Everything)
Store location, source positions, source text, dependencies, type graphs, etc.

**Pros**: Enables powerful tooling
**Cons**: Complex, large storage

### 4c: Incremental Debug Metadata
Start with just location, add more over time as needed.

**Pros**: Gradual complexity growth
**Cons**: Multiple migration phases

**Recommendation**: Start with 4a (minimal), expand to 4b (rich) as tooling needs emerge.

---

## Future Extensions

### Source Maps
```fsharp
type SourceMap = {
  // Map runtime position → source position
  rtPositionToSourcePosition: Map<ExecutionPosition, SourcePosition>
}

// For stack traces
let formatStackTrace (frames: List<ExecutionFrame>) : string =
  frames
  |> List.map (fun frame ->
    match SourceMap.getSourcePosition(frame.position) with
    | Some pos -> $"{pos.file}:{pos.line} in {frame.fn}"
    | None -> $"{frame.fn}"
  )
  |> String.join "\n"
```

### Type Inference Metadata
```fsharp
type InferenceMetadata = {
  // What type was inferred where
  inferredTypes: Map<SourcePosition, TypeName>

  // Why was it inferred (for tooltips)
  inferenceReasons: Map<SourcePosition, string>
}

// LSP hover: show inferred type
let getInferredType (pos: SourcePosition) : Option<string> =
  match InferenceMetadata.getInferredType(pos) with
  | Some typ -> Some $"Inferred type: {typ}"
  | None -> None
```

### Performance Profiling Metadata
```fsharp
type ProfilingMetadata = {
  // How long each function took
  fnExecutionTimes: Map<Hash, Duration>

  // How many times called
  fnCallCounts: Map<Hash, int64>
}

// Show hot spots
let getSlowFunctions() : List<(PackageLocation, Duration)> =
  ProfilingMetadata.fnExecutionTimes
  |> Map.toList
  |> List.sortByDescending snd
  |> List.take 10
  |> List.map (fun (hash, dur) ->
    let loc = DebugDB.getFnLocation(hash) |> Option.defaultValue "unknown"
    (loc, dur)
  )
```

---

## Recommendation

**Start with Solution 4a (Minimal Debug Metadata)**:

1. Add required `location` to PT.FQTypeName.PackageRef
2. Keep RT.FQTypeName.Package as just Hash
3. Create simple DebugDatabase with Hash→Location mapping
4. Store in PackageManager (Option A) for simplicity
5. Update error formatters and pretty printers to query it

**Then expand to Solution 4b (Rich Debug Metadata)** as tooling needs emerge:
- Add source positions for LSP
- Add source text for tooltips
- Add dependency graph for refactoring
- Add profiling metadata for performance

This gives you:
- ✅ Lean runtime (just Hash)
- ✅ Rich debugging (full location)
- ✅ Foundation for future tooling
- ✅ Clear separation of concerns
