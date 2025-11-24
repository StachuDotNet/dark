# Solution 1: PackageRef with Optional Location

## Core Idea

Change package type/fn/value references from bare Hash to a struct containing both Hash and optional Location.

## Data Structure Changes

### Before (Current)
```fsharp
module FQTypeName =
  type Package = Hash
  type FQTypeName =
    | Builtin of Builtin
    | Package of Hash
```

### After (Proposed)
```fsharp
module FQTypeName =
  type PackageRef = {
    id: Hash                          // WHAT: content hash
    location: Option<PackageLocation>  // WHERE: location hint
  }

  type FQTypeName =
    | Builtin of Builtin
    | Package of PackageRef
```

---

## How It Solves Problems

### Problem 1: Pretty Printing Nested Modules ✅

**Before:**
```fsharp
// Type reference: Package(abc123)
// PM lookup: typeIdToLoc[abc123] → MyModule3 (collision!)
```

**After:**
```fsharp
// Type reference: Package { id = abc123; location = Some MyModule2 }
// Pretty printer: "I'm looking at MyModule2.ID specifically"
// Use location to disambiguate, fall back to Hash if needed
```

### Problem 2: Runtime Error Messages ✅

**Before:**
```
Error: Function abc123 failed
```

**After:**
```fsharp
// Error context: Package { id = abc123; location = Some "Stdlib.String.append" }
// Error message: "In function Stdlib.String.append (abc123)"
```

### Problem 3: Type References in Code ✅

**Before:**
```fsharp
TypeReference.Package(abc123)  // Where is this type?
```

**After:**
```fsharp
TypeReference.Package { id = abc123; location = Some MyModule.ID }
// Pretty printer knows: this is MyModule.ID
// If ID moves, location becomes stale but Hash still valid
```

### Problem 4: Detecting Duplicates ✅

**Before:**
Hard to detect duplicates (only have Hash)

**After:**
```fsharp
// Can query: "Show me all locations with Hash abc123"
// Answer: [MyModule.ID, OtherModule.ID]
// Warn: "Duplicate structure detected"
```

### Problem 5: Stale Location References ✅

**Before:**
Location changes → reference breaks

**After:**
```fsharp
// Reference: Package { id = abc123; location = Some OldLocation }
// Lookup: OldLocation not found OR hash mismatch
// Fallback: Find current location(s) for hash abc123
// Degrade gracefully: still works, maybe warn
```

---

## Implementation Details

### Phase 1: Data Structure Changes

**F# Side (RuntimeTypes.fs, ProgramTypes.fs):**
```fsharp
module FQTypeName =
  type PackageRef = {
    id: Hash
    location: Option<PackageLocation>
  }
  type FQTypeName = Package of PackageRef

module FQValueName =
  type PackageRef = {
    id: Hash
    location: Option<PackageLocation>
  }
  type FQValueName = Package of PackageRef

module FQFnName =
  type PackageRef = {
    id: Hash
    location: Option<PackageLocation>
  }
  type FQFnName = Package of PackageRef
```

**Darklang Side (runtimeTypes.dark, programTypes.dark):**
```darklang
module FQTypeName =
  type PackageRef = {
    id: String  // Hash serializes as String
    location: Option<PackageLocation>
  }
  type FQTypeName = Package of PackageRef
```

### Phase 2: Parser Changes

**When resolving a type reference:**
```darklang
// Input: "MyModule.ID"
let resolveTypeReference (name: String) : FQTypeName =
  // Parse name into location
  let location = parseLocation(name)

  // Look up Hash at that location
  match Builtin.pmFindType None location with
  | Some hash ->
    FQTypeName.Package(PackageRef {
      id = hash
      location = Some location
    })
  | None ->
    // Unresolved - store location for later
    FQTypeName.Package(PackageRef {
      id = ""  // Or placeholder
      location = Some location
    })
```

### Phase 3: PackageManager Changes

**Add multi-valued reverse lookup:**
```fsharp
type PackageManager = {
  // Existing (unchanged):
  findType: (BranchID * PackageLocation) -> Option<Hash>
  getType: Hash -> Option<PackageType>

  // New:
  getTypeLocations: (BranchID * Hash) -> List<PackageLocation>
  getTypeAtLocation: (BranchID * Hash * PackageLocation) -> Option<PackageType>
}
```

**InMemory.fs implementation:**
```fsharp
// Storage:
let typeIdToLocs = ConcurrentDictionary<Hash, List<PackageLocation>>()

// When applying SetTypeName:
| PT.PackageOp.SetTypeName(id, loc) ->
  typeLocMap.TryAdd(loc, id) |> ignore

  // Add to multi-valued reverse mapping
  let existing = typeIdToLocs.GetOrAdd(id, [])
  if not (List.contains loc existing) then
    typeIdToLocs.TryUpdate(id, loc :: existing, existing) |> ignore

// New lookup functions:
getTypeLocations =
  fun (_branchID, id) ->
    let (found, locs) = typeIdToLocs.TryGetValue id
    Ply(if found then locs else [])

getTypeAtLocation =
  fun (_branchID, id, loc) ->
    let (found, locs) = typeIdToLocs.TryGetValue id
    if found && List.contains loc locs then
      getType id
    else
      Ply(None)
```

### Phase 4: Pretty Printer Changes

**With location hint:**
```darklang
let prettyPrintTypeRef (ref: FQTypeName.PackageRef) : String =
  match ref.location with
  | Some loc ->
    // Fast path: check if type is still at this location
    match Builtin.pmFindType None loc with
    | Some hash when hash == ref.id ->
      // Location is current, use it
      formatTypeName loc currentContext

    | _ ->
      // Location is stale, fall back to reverse lookup
      prettyPrintByHash ref.id

  | None ->
    // No location hint, must reverse lookup
    prettyPrintByHash ref.id

let prettyPrintByHash (hash: String) : String =
  match Builtin.pmGetTypeLocations None hash with
  | [] -> hash  // Can't find it, show Hash
  | [ singleLoc ] -> formatTypeName singleLoc currentContext
  | multipleLocs ->
    // Multiple locations - pick "best" one
    // Could be: shortest path, closest to current module, etc.
    formatTypeName (multipleLocs[0]) currentContext
```

**With current module context:**
```darklang
let formatTypeName (loc: PackageLocation) (ctx: ModuleContext) : String =
  if loc.owner == ctx.currentOwner &&
     loc.modules == ctx.currentModules then
    // Same module - just the name
    loc.name
  else if loc.owner == ctx.currentOwner &&
          startsWithPath loc.modules ctx.currentModules then
    // Parent or sibling module - relative path
    relativePath loc.modules ctx.currentModules ++ "." ++ loc.name
  else
    // External - full path
    loc.owner ++ "." ++ joinPath loc.modules ++ "." ++ loc.name
```

### Phase 5: Error Formatter Changes

**Runtime error reporting:**
```fsharp
match fnName with
| RT.FQFnName.Package { id = hash; location = Some loc } ->
  let locStr = formatLocation loc
  let msg = $"In function {locStr}"

  // Verify location is current
  match pm.getFnAtLocation(hash, loc) with
  | Some _ -> msg  // Location valid
  | None ->
    // Location stale, try to find current one
    match pm.getFnLocations(hash) with
    | [] -> $"In function {hash} (location unknown)"
    | locs -> $"In function {hash} (possibly {formatLocation locs[0]})"

| RT.FQFnName.Package { id = hash; location = None } ->
  // No location hint, reverse lookup
  match pm.getFnLocations(hash) with
  | [] -> $"In function {hash}"
  | [loc] -> $"In function {formatLocation loc}"
  | locs -> $"In function {hash} (one of: {formatLocations locs})"
```

---

## Migration Path

### Step 1: Add PackageRef type (non-breaking)
```fsharp
// Add new type alongside existing
type PackageRef = { id: Hash; location: Option<PackageLocation> }

// Keep old Package = Hash as alias temporarily
type Package = Hash

// Add helper to upgrade
let toPackageRef (hash: Hash) : PackageRef =
  { id = hash; location = None }
```

### Step 2: Update internal code to use PackageRef
```fsharp
// Change FQTypeName gradually
type FQTypeName =
  | Package of PackageRef  // New
  // | Package of Hash     // Old (remove eventually)
```

### Step 3: Update serialization
```fsharp
// Binary serialization: write both Hash and optional Location
// JSON: can be backwards compatible (old = just Hash)
```

### Step 4: Update parser to populate locations
```fsharp
// Start adding location hints when resolving references
```

### Step 5: Update pretty printer to use locations
```fsharp
// Prefer location hint, fall back to Hash lookup
```

### Step 6: Clean up old code
```fsharp
// Remove Hash-only paths once all migrated
```

---

## Pros and Cons

### Pros ✅
- Fixes pretty printing collision
- Enables better error messages
- Supports stale location references
- Can detect duplicate structures
- Backward compatible (location is optional)
- Degrades gracefully (if no location, still works)
- Preserves content verification (Hash still there)

### Cons ❌
- Larger type references (Hash + Option<Location>)
- More complex serialization
- Parser needs to track locations everywhere
- Need to update all code that creates package references
- PackageManager needs multi-valued mappings
- More bookkeeping throughout system

---

## Open Questions

1. **Should location be Option or required?**
   - Option: More flexible, gradual migration
   - Required: Forces correctness, better errors
   - Recommendation: Option (for flexibility)

2. **What if multiple locations have same Hash?**
   - Pick first? Pick shortest? Pick "closest"?
   - Recommendation: Return all, let caller choose

3. **How to handle stale locations in serialized data?**
   - Validate on load? Silently update? Keep stale?
   - Recommendation: Keep stale, revalidate on use

4. **Should we warn about duplicate structures?**
   - Yes: Helps catch accidental duplicates
   - No: Might be intentional (different semantics)
   - Recommendation: Configurable lint warning

---

## Alternative: Location Required (Stricter)

Instead of `Option<PackageLocation>`, make it required:

```fsharp
type PackageRef = {
  id: Hash
  location: PackageLocation  // Always present
}
```

**Benefits:**
- Simpler code (no Option handling)
- Always have context for errors
- Forces parser to be complete

**Drawbacks:**
- What location for runtime-generated values?
- What location for deserialize values?
- Less flexible during migration

**Verdict:** Option is better for flexibility and migration.
