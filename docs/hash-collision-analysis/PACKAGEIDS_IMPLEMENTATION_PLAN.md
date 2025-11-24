# PackageIDs Implementation Plan: Mutable Resolution

## Overview

Replace hardcoded Guid strings with mutable `Option<Hash>` that gets populated during package parsing.

---

## Core Approach

```fsharp
// PackageIDs.fs - BEFORE
let option = p ["Option"] "Option" "9ce3b596-968f-44c9-bcd4-511007cd9225"
// Returns: Hash (from Guid bytes)

// PackageIDs.fs - AFTER
let mutable private _option : Option<Hash> = None
let option : Hash =
  match _option with
  | Some hash -> hash
  | None -> failwith "PackageIDs.Type.Stdlib.option not yet initialized"
```

**Key idea**: Start with `None`, populate during parsing, error if uninitialized.

---

## Data Structure Changes

### Current Structure

```fsharp
// PackageIDs.fs
module Type =
  let mutable private _lookup = Map<(List<string> * string), Guid>()

  let private p modules name (id : string) : Hash =
    let guid = System.Guid.Parse id
    _lookup <- _lookup |> Map.add (modules, name) guid
    let bytes = guid.ToByteArray()
    Hash.ofBytes bytes

  module Stdlib =
    let option = p [ "Option" ] "Option" "9ce3b596-..."
```

**Issues:**
- Returns Hash immediately (needs Guid string)
- Lookup map stores Guid, not Hash
- No mechanism to populate later

---

### Proposed Structure

```fsharp
// PackageIDs.fs
module Type =
  // Store locations and their resolved Hashes
  let mutable private _registry : Map<PackageLocation, Option<Hash>> = Map.empty

  // Helper to register a location (returns accessor, not Hash!)
  let private register (owner: string) (modules: List<string>) (name: string) : unit -> Hash =
    let loc = { owner = owner; modules = modules; name = name }
    _registry <- _registry |> Map.add loc None

    // Return a function that looks up the Hash
    fun () ->
      match Map.tryFind loc _registry with
      | Some (Some hash) -> hash
      | Some None ->
        failwith $"PackageID not initialized: {owner}.{String.concat \".\" modules}.{name}"
      | None ->
        failwith $"PackageID not registered: {owner}.{String.concat \".\" modules}.{name}"

  module Stdlib =
    let private optionFn = register "Darklang" [ "Stdlib"; "Option" ] "Option"
    let option : Hash = optionFn()

    let private resultFn = register "Darklang" [ "Stdlib"; "Result" ] "Result"
    let result : Hash = resultFn()
```

**Benefits:**
- No Guid strings needed
- Registry starts with `None` values
- Accessor functions fail if uninitialized
- Clear error messages

---

## Alternative: Lazy Initialization

```fsharp
// PackageIDs.fs
module Type =
  let mutable private _registry : Map<PackageLocation, Option<Hash>> = Map.empty

  let private register (owner: string) (modules: List<string>) (name: string) : Lazy<Hash> =
    let loc = { owner = owner; modules = modules; name = name }
    _registry <- _registry |> Map.add loc None

    lazy (
      match Map.tryFind loc _registry with
      | Some (Some hash) -> hash
      | Some None ->
        failwith $"PackageID not initialized: {owner}.{String.concat \".\" modules}.{name}"
      | None ->
        failwith $"PackageID not registered: {owner}.{String.concat \".\" modules}.{name}"
    )

  module Stdlib =
    let private optionLazy = register "Darklang" [ "Stdlib"; "Option" ] "Option"
    let option : Hash = optionLazy.Value
```

**Benefits:**
- Lazy evaluation (only fails when accessed)
- Cleaner syntax
- Standard .NET pattern

---

## Population During Parsing

### Current Flow

```fsharp
// WrittenTypesToProgramTypes.fs
let toPT (pt : WT.PackageType) : Ply<PT.PackageType.PackageType> =
  uply {
    let! declaration = TypeDeclaration.toPT pm onMissing currentModule pt.declaration
    return
      { id = PackageIDs.Type.idForName pt.name.owner pt.name.modules pt.name.name
        description = pt.description
        declaration = declaration
        deprecated = PT.NotDeprecated }
  }
```

**Issue**: `idForName` returns a Hash (from Guid), but we want to compute it from declaration.

---

### Proposed Flow

```fsharp
// WrittenTypesToProgramTypes.fs
let toPT (pt : WT.PackageType) : Ply<PT.PackageType.PackageType> =
  uply {
    let! declaration = TypeDeclaration.toPT pm onMissing currentModule pt.declaration

    // Compute content hash from declaration
    let tempType =
      { id = Hash.zero
        description = pt.description
        declaration = declaration
        deprecated = PT.NotDeprecated }
    let contentHash = ContentHash.PackageType.hash tempType

    // Register this Hash in PackageIDs if it's a known stdlib type
    PackageIDs.Type.registerIfKnown pt.name.owner pt.name.modules pt.name.name contentHash

    return { tempType with id = contentHash }
  }
```

**New function in PackageIDs:**

```fsharp
// PackageIDs.fs
module Type =
  let registerIfKnown (owner: string) (modules: List<string>) (name: string) (hash: Hash) : unit =
    let loc = { owner = owner; modules = modules; name = name }
    match Map.tryFind loc _registry with
    | Some None ->
      // This is a registered PackageID location, populate it
      _registry <- _registry |> Map.add loc (Some hash)
    | Some (Some existingHash) ->
      // Already populated - check for consistency
      if existingHash <> hash then
        failwith $"PackageID {owner}.{String.concat \".\" modules}.{name} already set to {existingHash}, cannot reset to {hash}"
    | None ->
      // Not a registered location, ignore
      ()
```

---

## Validation at Startup

After parsing all packages, verify all PackageIDs are populated:

```fsharp
// Startup.fs or PackageIDs.fs
module PackageIDs =
  let validateAllInitialized () : Result<unit, List<string>> =
    let uninitialized =
      _registry
      |> Map.toList
      |> List.choose (fun (loc, hashOpt) ->
        match hashOpt with
        | None -> Some $"{loc.owner}.{String.concat \".\" loc.modules}.{loc.name}"
        | Some _ -> None
      )

    if List.isEmpty uninitialized then
      Ok ()
    else
      Error uninitialized

// After parsing packages
match PackageIDs.Type.validateAllInitialized() with
| Ok () -> printfn "All PackageIDs initialized"
| Error missing ->
  failwith $"PackageIDs not initialized: {String.concat \", \" missing}"
```

---

## Migration Steps

### Step 1: Add Registry Infrastructure

```fsharp
// PackageIDs.fs
module Type =
  // New registry
  let mutable private _registry : Map<PackageLocation, Option<Hash>> = Map.empty

  // Keep old lookup for now (for idForName)
  let mutable private _lookup = Map []

  // New: register a location
  let private register (owner: string) (modules: List<string>) (name: string) : Lazy<Hash> =
    let loc = { owner = owner; modules = modules; name = name }
    _registry <- _registry |> Map.add loc None
    lazy (Map.find loc _registry |> Option.get)

  // New: populate a registered location
  let registerIfKnown (owner: string) (modules: List<string>) (name: string) (hash: Hash) : unit =
    let loc = { owner = owner; modules = modules; name = name }
    match Map.tryFind loc _registry with
    | Some None -> _registry <- _registry |> Map.add loc (Some hash)
    | Some (Some existing) when existing <> hash ->
      failwith $"PackageID conflict: {loc} already set"
    | _ -> ()
```

### Step 2: Convert One Entry

```fsharp
// PackageIDs.fs
module Type =
  module Stdlib =
    // OLD (keep for now):
    // let option = p [ "Option" ] "Option" "9ce3b596-..."

    // NEW:
    let private optionLazy = register "Darklang" [ "Stdlib"; "Option" ] "Option"
    let option : Hash = optionLazy.Value
```

### Step 3: Update Parser

```fsharp
// WrittenTypesToProgramTypes.fs
let toPT (pt : WT.PackageType) : Ply<PT.PackageType.PackageType> =
  uply {
    let! declaration = TypeDeclaration.toPT pm onMissing currentModule pt.declaration

    // Compute content hash
    let tempType = { id = Hash.zero; description = pt.description; declaration = declaration; deprecated = PT.NotDeprecated }
    let hash = ContentHash.PackageType.hash tempType

    // Register if known stdlib type
    PackageIDs.Type.registerIfKnown pt.name.owner pt.name.modules pt.name.name hash

    return { tempType with id = hash }
  }
```

### Step 4: Test with One Type

Run tests. Verify:
- ✅ Option type gets content hash
- ✅ PackageIDs.Type.Stdlib.option returns that hash
- ✅ Builtins can use it
- ✅ Errors if accessed before parsing

### Step 5: Convert All Entries

Once working, convert all ~350 PackageID entries:
- Types (Option, Result, all ParseErrors, etc.)
- Functions (Stdlib.List.map, etc.)
- Values (if any)

### Step 6: Remove Old Guid Infrastructure

```fsharp
// PackageIDs.fs - DELETE:
// let mutable private _lookup = Map []
// let private p modules name (id : string) : Hash = ...

// KEEP:
let mutable private _registry : Map<PackageLocation, Option<Hash>> = Map.empty
let private register ...
let registerIfKnown ...
```

---

## Error Handling

### Uninitialized Access

```fsharp
// If builtin tries to use PackageID before parsing:
let optionType = PackageIDs.Type.Stdlib.option
// ❌ Error: "PackageID not initialized: Darklang.Stdlib.Option.Option"
```

**Solution**: Ensure parsing happens before any builtin execution.

---

### Double Registration

```fsharp
// If parser tries to set same PackageID twice with different hashes:
PackageIDs.Type.registerIfKnown "Darklang" ["Stdlib"; "Option"] "Option" hash1
PackageIDs.Type.registerIfKnown "Darklang" ["Stdlib"; "Option"] "Option" hash2
// ❌ Error: "PackageID conflict: Darklang.Stdlib.Option.Option already set to hash1, cannot reset to hash2"
```

**This catches**:
- Parsing same file twice
- Definition changed between parses
- Hash computation bug

---

### Missing Registration

```fsharp
// After parsing, some PackageID never populated:
PackageIDs.Type.validateAllInitialized()
// ❌ Error: "PackageIDs not initialized: Darklang.Stdlib.Result.Result, ..."
```

**This catches**:
- Stdlib file not parsed
- Type renamed/moved
- Registry entry stale

---

## PackageManager Changes

### Current: idForName Fallback

```fsharp
// PackageManager.fs
let stableId =
  match stableIdOpt with
  | Some id -> id
  | None ->
    // Check PackageIDs for known stable ID
    LibExecution.PackageIDs.Type.idForName loc.owner loc.modules loc.name
```

### Proposed: Content Hash Everywhere

```fsharp
// PackageManager.fs
let stableId =
  match stableIdOpt with
  | Some id -> id
  | None ->
    // No stable ID - use whatever content hash was computed
    // PackageIDs will be populated during parsing, no special handling needed
    typ.id  // Content hash already in the type
```

**Simplification**: Don't need `idForName` anymore! Content hashing handles it.

---

## Initialization Order

### Critical Sequence:

```
1. F# Application Starts
   ↓
   PackageIDs registry initialized (all None)

2. Parse Stdlib Packages
   ↓
   For each type/fn/value:
   - Compute content hash
   - Call PackageIDs.registerIfKnown
   - Populate registry entries

3. Validate All Populated
   ↓
   PackageIDs.validateAllInitialized()
   - Error if any None remaining

4. Run Builtins
   ↓
   Builtins access PackageIDs
   - All resolved to content hashes
   - Everything works!
```

**Key requirement**: Step 2-3 MUST complete before Step 4.

---

## Example: Full Flow for Option Type

### 1. F# Compile Time

```fsharp
// PackageIDs.fs
module Type =
  module Stdlib =
    let private optionLazy = register "Darklang" [ "Stdlib"; "Option" ] "Option"
    let option : Hash = optionLazy.Value

// RuntimeTypes.fs
let option (t : TypeReference) : TypeReference =
  TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.Stdlib.option), [ t ])
```

Registry state: `{Darklang.Stdlib.Option.Option: None}`

---

### 2. Parse Stdlib

```darklang
// packages/darklang/stdlib/option.dark
module Stdlib =
  module Option =
    type Option<'a> =
      | Some of value: 'a
      | None
```

Parser runs:
```fsharp
// WrittenTypesToProgramTypes.fs
let declaration = ... // Parsed from above
let tempType = { id = Hash.zero; declaration = declaration; ... }
let hash = ContentHash.PackageType.hash tempType  // = "abc123..."

// Register!
PackageIDs.Type.registerIfKnown "Darklang" ["Stdlib"; "Option"] "Option" hash
```

Registry state: `{Darklang.Stdlib.Option.Option: Some("abc123...")}`

---

### 3. Validate

```fsharp
PackageIDs.Type.validateAllInitialized()
// ✅ Success - all entries populated
```

---

### 4. Builtin Execution

```fsharp
// RuntimeTypes.fs
let optionTypeRef = TypeReference.option TInt64
// Accesses: PackageIDs.Type.Stdlib.option
// Returns: "abc123..."
// Result: TCustomType(Ok(FQTypeName.Package "abc123..."), [TInt64])
```

---

## Benefits

1. **No Guid strings** - content hashing throughout
2. **Automatic coordination** - parser populates what builtins need
3. **Fail fast** - errors if uninitialized or inconsistent
4. **Type safety** - still returns Hash type
5. **Simple mental model** - "parse populates, runtime uses"

---

## Potential Issues

### Issue 1: Lazy vs Function

**Option A: Function**
```fsharp
let option : Hash = optionFn()  // Call function each time
```
- More flexible
- Can log/trace access
- Slightly slower

**Option B: Lazy**
```fsharp
let option : Hash = optionLazy.Value  // Lazy evaluation
```
- Standard pattern
- Only evaluated once
- Fails on first access if uninitialized

**Recommendation**: Use Lazy (Option B) for performance and clarity.

---

### Issue 2: Thread Safety

If parsing happens on multiple threads:

```fsharp
// Need thread-safe mutation
let private _registryLock = obj()
let private _registry = ref Map.empty

let registerIfKnown (owner: string) (modules: List<string>) (name: string) (hash: Hash) : unit =
  lock _registryLock (fun () ->
    let loc = { owner = owner; modules = modules; name = name }
    match Map.tryFind loc !_registry with
    | Some None ->
      _registry := Map.add loc (Some hash) !_registry
    | ...
  )
```

**Recommendation**: Use lock if multi-threaded parsing, otherwise not needed.

---

### Issue 3: Circular Dependencies

What if Option type references another PackageID that isn't initialized yet?

```darklang
type Result<'ok, 'err> =
  | Ok of 'ok
  | Error of 'err

// Later...
type Outcome = Result<String, Error>  // References Result type
```

**Solution**: Parse order matters. Ensure types are parsed in dependency order, or use two-phase:
1. Parse all declarations (compute all Hashes)
2. Resolve all references

---

## Testing Strategy

### Unit Tests

```fsharp
// Test: Uninitialized access
[<Fact>]
let ``PackageIDs fails if accessed before initialization`` () =
  let ex = Assert.Throws<Exception>(fun () ->
    let _ = PackageIDs.Type.Stdlib.option
    ()
  )
  Assert.Contains("not initialized", ex.Message)

// Test: Successful registration
[<Fact>]
let ``PackageIDs can be registered and retrieved`` () =
  let hash = Hash.ofString "test123"
  PackageIDs.Type.registerIfKnown "Darklang" ["Stdlib"; "Option"] "Option" hash
  Assert.Equal(hash, PackageIDs.Type.Stdlib.option)

// Test: Double registration detection
[<Fact>]
let ``PackageIDs detects conflicting registration`` () =
  let hash1 = Hash.ofString "test123"
  let hash2 = Hash.ofString "test456"
  PackageIDs.Type.registerIfKnown "Darklang" ["Stdlib"; "Option"] "Option" hash1
  let ex = Assert.Throws<Exception>(fun () ->
    PackageIDs.Type.registerIfKnown "Darklang" ["Stdlib"; "Option"] "Option" hash2
  )
  Assert.Contains("conflict", ex.Message)
```

---

## Summary

**Approach**: Mutable `Option<Hash>` registry, populated during parsing, validated at startup.

**Key Changes**:
1. PackageIDs: Register locations with None, provide lazy accessors
2. Parser: Compute content hashes, call `registerIfKnown`
3. Startup: Validate all populated before running builtins
4. Remove: All Guid strings and Guid→Hash conversion

**Result**: Content-based hashing throughout, with coordination mechanism for F#↔Darklang.
