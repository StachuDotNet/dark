# Practical Implementation Plan: Ref-by-Hash (Single PR)

## Approach

Fix things incrementally, one compilation unit at a time, until everything builds and works.

Not "phases over weeks" - just **ordered steps** to keep the build working as we go.

---

## Step 1: Fix LibExecution to Build

### 1.1 ProgramTypes.fs - Add Location to References

```fsharp
// Current:
type TypeReference =
  | TCustomType of NameResolution<FQTypeName.FQTypeName> * typeArgs: List<TypeReference>

// Change to:
type TypeReference =
  | TCustomType of
      hash: NameResolution<FQTypeName.Package> *
      location: Option<PackageLocation> *
      typeArgs: List<TypeReference>
```

**Why**: Need location hint for pretty printing, but hash for identity.

**Impact**: All pattern matches on TCustomType need updating.

---

### 1.2 PackageType/Fn/Value - Reorder Fields

```fsharp
// Current scattered

// Change to declaration-first:
type PackageType =
  { declaration : TypeDeclaration.T
    id : FQTypeName.Package  // computed from declaration
    location : Option<PackageLocation>  // where it lives
    description : string
    deprecated : Deprecation<FQTypeName.FQTypeName> }
```

**Why**: Declaration is source of truth, ID is derived.

**Impact**: Construction sites need reordering.

---

### 1.3 ContentHash.fs - Add SCC Detection

```fsharp
// New function:
let detectSCCs (items: List<PackageType>) : List<List<PackageType>> =
  // Tarjan's algorithm
  // Return groups of mutually recursive types

let hashSCC (scc: List<PackageType>) : Hash =
  // Sort items deterministically (by name)
  // Hash group together
  // All members get same hash
```

**Why**: Mutual recursion needs SCC-based hashing.

**Impact**: New code, doesn't break existing.

---

### 1.4 Fix All LibExecution Compile Errors

Go through compiler errors one by one:
- Update TCustomType pattern matches
- Add `location` parameter (use `None` for now)
- Update PackageType construction

**Goal**: `LibExecution.fsproj` builds successfully.

---

## Step 2: Fix LibSerialization to Build

### 2.1 Binary Serializers - Add Location Field

```fsharp
// Serializers/PT/PackageType.fs
let write (w: BinaryWriter) (pt: PT.PackageType.PackageType) =
  // Existing fields...
  Option.write PackageLocation.write w pt.location  // NEW
```

**Impact**: Serialization format changes (expected).

---

### 2.2 Update All Serializer Tests

Fix tests that construct PackageTypes/Fns/Values.

**Goal**: `LibSerialization.fsproj` builds successfully.

---

## Step 3: Fix LibParser to Build

### 3.1 WrittenTypesToProgramTypes.fs - Multi-Phase Parsing

```fsharp
// Phase 1: Parse all declarations (hash = Hash.zero)
let parseDeclarations (wt: WT.PackageType) : PT.PackageType =
  { declaration = ...
    id = Hash.zero  // placeholder
    location = Some { owner = ...; modules = ...; name = ... }
    ... }

// Phase 2: Detect SCCs and compute hashes
let computeHashes (types: List<PT.PackageType>) : List<PT.PackageType> =
  let sccs = ContentHash.detectSCCs types
  sccs |> List.map (fun scc ->
    let hash = ContentHash.hashSCC scc
    scc |> List.map (fun t -> { t with id = hash })
  ) |> List.concat

// Phase 3: Rewrite references (replace Hash.zero with actual hashes)
let rewriteReferences (types: List<PT.PackageType>) : List<PT.PackageType> =
  let hashMap = types |> List.map (fun t -> (t.location.Value, t.id)) |> Map.ofList
  types |> List.map (fun t ->
    { t with declaration = rewriteRefsInDeclaration hashMap t.declaration }
  )
```

**Why**: Can't hash until parsed, can't resolve refs until hashed.

**Impact**: WT2PT becomes 3-phase instead of single pass.

---

### 3.2 Update NameResolver

```fsharp
// Current: Resolves to FQTypeName immediately

// Change: Return location, resolve to hash later
let resolveTypeName (name: string) : PackageLocation =
  parseLocation name
```

**Impact**: Resolution happens in phase 3, not during parse.

---

**Goal**: `LibParser.fsproj` builds successfully.

---

## Step 4: Fix LibPackageManager to Build

### 4.1 PackageManager.fs - Add Multi-Location Support

```fsharp
type PackageManager =
  { // Forward lookup (unchanged)
    findType : (BranchIDOpt * PackageLocation) -> Ply<Option<Hash>>

    // Reverse lookup (NEW - returns multiple)
    getTypeLocations : (BranchIDOpt * Hash) -> Ply<List<PackageLocation>>

    // Get by hash (unchanged)
    getType : Hash -> Ply<Option<PackageType>> }
```

**Why**: One hash can exist at multiple locations.

---

### 4.2 InMemory.fs - Multi-Valued Map

```fsharp
// Current:
let typeIdToLoc = ConcurrentDictionary<Hash, PackageLocation>()

// Change to:
let typeIdToLocs = ConcurrentDictionary<Hash, List<PackageLocation>>()

// Update SetTypeName:
| PT.PackageOp.SetTypeName(id, loc) ->
  typeLocMap.TryAdd(loc, id) |> ignore

  // Add to multi-valued reverse map
  let existing = typeIdToLocs.GetOrAdd(id, [])
  if not (List.contains loc existing) then
    typeIdToLocs.TryUpdate(id, loc :: existing, existing) |> ignore
```

**Why**: Fix the TryAdd bug that only keeps first location.

---

### 4.3 SQL - Change Deprecation Logic

```fsharp
// Current: Deprecates old location on SetTypeName

// Change: Only deprecate if explicitly moving (detect via ops sequence)
let applySetName (itemId: Hash) (location: PackageLocation) =
  // Check if this is a move (location used to point to different hash)
  let! oldHashOpt = findType(None, location)

  match oldHashOpt with
  | Some oldHash when oldHash <> itemId ->
    // This is a move - deprecate old
    deprecateLocation(oldHash, location)
  | _ ->
    // New location or same hash - don't deprecate
    ()

  // Insert new location (doesn't conflict with above)
  insertLocation(itemId, location)
```

**Why**: Support coexistence (nested modules) AND moves (refactoring).

---

**Goal**: `LibPackageManager.fsproj` builds successfully.

---

## Step 5: Fix Builtin Libraries to Build

### 5.1 PackageIDs.fs - Mutable Registry (from earlier plan)

```fsharp
module Type =
  let mutable private _registry : Map<PackageLocation, Option<Hash>> = Map.empty

  let private register (owner: string) (modules: List<string>) (name: string) : Lazy<Hash> =
    let loc = { owner = owner; modules = modules; name = name }
    _registry <- _registry |> Map.add loc None
    lazy (Map.find loc _registry |> Option.get)

  let registerIfKnown (owner: string) (modules: List<string>) (name: string) (hash: Hash) : unit =
    let loc = { owner = owner; modules = modules; name = name }
    match Map.tryFind loc _registry with
    | Some None -> _registry <- _registry |> Map.add loc (Some hash)
    | Some (Some existing) when existing <> hash ->
      failwith $"PackageID conflict: {loc}"
    | _ -> ()

  module Stdlib =
    let private optionLazy = register "Darklang" [ "Stdlib"; "Option" ] "Option"
    let option : Hash = optionLazy.Value
```

**Why**: F# needs stable references, parser populates them.

---

### 5.2 Update WT2PT to Register PackageIDs

```fsharp
// In parseDeclarations/computeHashes:
let hash = ContentHash.hashSCC scc
PackageIDs.Type.registerIfKnown loc.owner loc.modules loc.name hash
```

**Why**: Populate registry during parsing.

---

**Goal**: All `Builtin*.fsproj` projects build successfully.

---

## Step 6: Get F# Tests Passing

### 6.1 Fix Test Helpers

```fsharp
// TestUtils.fs
let makePackageType name declaration =
  { declaration = declaration
    id = ContentHash.PackageType.hash { id = Hash.zero; declaration = declaration; ... }
    location = Some { owner = "Test"; modules = []; name = name }
    description = ""
    deprecated = PT.NotDeprecated }
```

---

### 6.2 Run LibExecution.Tests

```bash
./scripts/run-backend-tests --filter-test-list execution
```

Fix errors:
- TCustomType pattern matches
- PackageType construction
- Missing location parameters

---

### 6.3 Run PackageManager.Tests

```bash
./scripts/run-backend-tests --filter-test-list packagemanager
```

Fix errors:
- Multi-location queries
- SCC hashing tests

---

**Goal**: Core F# unit tests pass.

---

## Step 7: Get Parser and Package Loading Working

### 7.1 Test Parsing Simple File

```bash
# Parse a simple .dark file
./scripts/run-cli parse packages/darklang/stdlib/option.dark
```

Fix errors:
- Multi-phase parsing
- Hash computation
- PackageID registration

---

### 7.2 Test Parsing with Mutual Recursion

```darklang
type A = B
type B = A
```

Verify:
- SCC detected
- Both get same hash
- No infinite loops

---

### 7.3 Test Package Loading

```bash
# Load all stdlib packages
./scripts/run-cli load-packages
```

Fix errors:
- PM ops application
- Location tracking
- Hash resolution

---

**Goal**: Can parse and load Darklang packages.

---

## Step 8: Get CLI Working

### 8.1 Fix CLI Commands

```bash
# Test basic CLI commands
./scripts/run-cli --help
./scripts/run-cli list-packages
./scripts/run-cli show-type Stdlib.Option.Option
```

Fix errors:
- Pretty printing (use location from hash)
- Type lookups
- Error messages

---

### 8.2 Test Execution

```bash
# Run a simple script
./scripts/run-cli run test.dark
```

Fix errors:
- Runtime type resolution
- Builtin references
- Value execution

---

**Goal**: CLI works for basic operations.

---

## Step 9: Fix Remaining Test Failures

### 9.1 Run All Tests

```bash
./scripts/run-backend-tests
```

---

### 9.2 Fix Failing Tests One by One

For each failure:
1. Understand what broke
2. Decide if test needs updating or code needs fixing
3. Fix it
4. Verify related tests still pass

---

### 9.3 Special: Nested Module Test

The original failing test should now pass:
```darklang
module MyModule1 =
  type ID = Int64
  module MyModule2 =
    type ID = Int64
    module MyModule3 =
      type ID = Int64
```

All three types:
- Have same hash (structurally identical)
- Have different locations
- Pretty print correctly

---

**Goal**: All tests pass (or failing tests documented as "deferred").

---

## Step 10: Cleanup

### 10.1 Remove Dead Code

- Old UUID-based code paths
- Temporary hacks
- Commented-out sections

---

### 10.2 Add Documentation

- Update README if needed
- Document new architecture
- Add comments to tricky code

---

### 10.3 Run Full Test Suite One More Time

```bash
./scripts/run-backend-tests
```

---

## Ordering Summary

**Build order** (what needs to compile first):
1. LibExecution (ProgramTypes, RuntimeTypes, ContentHash)
2. LibSerialization (depends on LibExecution types)
3. LibParser (depends on both)
4. LibPackageManager (depends on all above)
5. Builtins (depend on LibExecution)
6. Tests (depend on everything)

**Functional order** (what needs to work first):
1. Types build (LibExecution)
2. Serialization works (LibSerialization)
3. Parsing works (LibParser multi-phase)
4. PM stores/retrieves correctly (LibPackageManager)
5. Builtins resolve (PackageIDs registry)
6. Tests pass
7. CLI works

---

## Key Principles

1. **Keep it building** - Fix compile errors immediately
2. **Test incrementally** - Don't wait until the end
3. **One concept at a time** - Location first, then SCC, then builtins
4. **Use None/placeholder** - Add location field with None initially, populate later
5. **Multi-valued is key** - Hash → multiple locations is fundamental

---

## What This Actually Fixes

- ✅ Hash collision (multi-location support)
- ✅ Mutual recursion (SCC hashing)
- ✅ Builtin references (mutable registry)
- ✅ Pretty printing (location hints)
- ✅ Content-addressed identity (hash from declaration)

---

## Estimated Time

Not "22 weeks" - more like:
- Steps 1-5 (get building): 2-3 days
- Steps 6-7 (get tests passing): 2-3 days
- Steps 8-9 (CLI + remaining tests): 2-3 days
- Step 10 (cleanup): 1 day

**Total: ~1-2 weeks** of focused work.

With AI assistance, could be faster - maybe a week if we batch changes intelligently.

---

## Success Criteria

1. All F# projects build
2. All tests pass (or failures documented)
3. CLI works for basic operations
4. Can parse stdlib packages
5. Nested modules work correctly
6. No UUID-based code left
7. Architecture is clean and documented
