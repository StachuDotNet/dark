# Layer 7: Builtins Analysis

## Current State

### PackageIDs.fs Structure
Hard-coded mapping of (owner, modules, name) → Hash:

```fsharp
module Type =
  let mutable private _lookup = Map []

  let private p modules name (id : string) : Hash =
    let guid = System.Guid.Parse id
    _lookup <- _lookup |> Map.add (modules, name) guid
    let bytes = guid.ToByteArray()
    Hash.ofBytes bytes

  module Stdlib =
    let result = p [ "Result" ] "Result" "c1cb018c-8264-4080-be7f-b06b8a0e1729"
    let option = p [ "Option" ] "Option" "9ce3b596-968f-44c9-bcd4-511007cd9225"
    // ... many more

  let idForName (owner : string) (modules : List<string>) (name : string) : Hash =
    match owner with
    | "Darklang" ->
      match Map.get (modules, name) _lookup with
      | Some id -> id  // Use pre-assigned hash
      | None -> System.Guid.NewGuid()  // Generate new
    | _ -> System.Guid.NewGuid()
```

**Pattern**: Pre-allocate Guids (treated as hashes) for known types/functions

### Where Builtins Reference Darklang Types

**RT.BuiltInFn**: References package types in signatures
```fsharp
// backend/src/BuiltinExecution/Libs/Option.fs (example)
let fns : List<RT.BuiltInFn> = [
  { name = RT.FQFnName.builtin "Option" "map" 0
    parameters = [
      RT.BuiltInParam.make "option"
        (RT.TCustomType(
          Ok(RT.FQTypeName.fqPackage PackageIDs.Type.Stdlib.option),
          [RT.TVariable "a"]))
        "The option to map"
      // ...
    ]
    returnType =
      RT.TCustomType(
        Ok(RT.FQTypeName.fqPackage PackageIDs.Type.Stdlib.option),
        [RT.TVariable "b"])
    // ...
  }
]
```

**Key insight**: Builtins use `PackageIDs.Type.Stdlib.option` to reference the Option type

### Chicken-and-Egg Problem

**The Issue**:
1. F# builtins need to reference Option type
2. Option type is defined in Darklang (packages/darklang/stdlib/option.dark)
3. Option type's hash depends on its content
4. But F# code is compiled before Darklang code is parsed
5. Cannot compute Option's hash at F# compile time

**Current solution**: Hard-code UUIDs in PackageIDs.fs

**Problem with current solution**:
- If Option implementation changes, hash should change
- But F# builtins still reference old UUID
- Mismatch between actual hash and referenced hash

### Parse-Time Integration

From WrittenTypesToProgramTypes.fs:

```fsharp
// Lines 754, 780, 832
{ id = PackageIDs.Type.idForName pt.name.owner pt.name.modules pt.name.name
  description = pt.description
  declaration = declaration
  deprecated = PT.NotDeprecated }
```

**When parsing Darklang.Stdlib.Option**:
1. Parser calls `PackageIDs.Type.idForName "Darklang" ["Stdlib"] "Option"`
2. Finds hardcoded UUID in _lookup
3. Uses that as the hash
4. Even though content hash might be different!

**Effect**: Canonical Darklang types get stable IDs that F# code can reference

### Testing Integration

From PackageIDs.fs (lines 5-10):
```fsharp
/// All Darklang code exists in package space, referenced by ID. In many places
/// throughout our F# codebase, we reference these IDs. (i.e. in order to return an
/// `Option` from a function, we need to know the ID of the `Option` package type).
///
/// So, we define their IDs here, and reference via those IDs. When parsing, we have
/// a lookup of name -> ID handy; if a parsed definition matches one of those names,
/// we ensure that we use the corresponding ID when saving it to the DB.
```

**Implications**:
- IDs are treated as "blessed" by convention
- Parser enforces these IDs
- F# code assumes these IDs

## Problems

### 1. UUID ≠ Content Hash
**Problem**: PackageIDs uses arbitrary UUIDs, not content hashes

**Consequence**:
- Option type v1 gets UUID abc
- Option type v2 (changed impl) should get hash def
- But F# code still references UUID abc
- System broken

**Fundamental issue**: Cannot have stable IDs AND content-based IDs

### 2. Parse-Time Override
**Problem**: Parser overrides computed hash with PackageIDs hash

```fsharp
// What should happen:
let contentHash = computeHash optionType
let id = contentHash

// What actually happens:
let id = PackageIDs.Type.Stdlib.option  // Ignore content!
```

**Consequence**: Content-based hashing is defeated for builtins

### 3. F# Compile-Time Constraint
**Problem**: F# code needs IDs at compile time, but hashes computed at runtime

**Timeline**:
1. F# compiled → needs Option ID → uses PackageIDs.UUID
2. Darklang parsed → computes Option hash → gets different value
3. Mismatch

**Cannot be solved** with pure content hashing

### 4. No Verification
**Problem**: No check that PackageIDs hash matches actual content hash

**Risk**:
- Option implementation changes
- PackageIDs.fs not updated
- F# code references wrong hash
- Silent breakage

**Needed**: Validation at startup or test time

### 5. Manual Maintenance
**Problem**: PackageIDs.fs is manually curated

**Every new standard library type/function needs**:
1. Define in Darklang
2. Generate UUID
3. Add to PackageIDs.fs
4. Update all F# code that references it

**Error-prone and tedious**

### 6. Versioning Nightmare
**Problem**: What if Option type needs to change?

**Scenario**: Add a field to Result type
```dark
// V1
type Result<a, b> = Ok of a | Error of b

// V2
type Result<a, b> =
  | Ok of a
  | Error of b
  | Pending  // New case
```

**Questions**:
- Does hash change? (Yes, content changed)
- Do F# builtins still work? (No, they expect v1)
- How to migrate? (Update PackageIDs? Keep both versions?)

### 7. Cross-Language Dependency
**Problem**: F# and Darklang are coupled via PackageIDs

**Consequence**:
- Cannot change stdlib without updating F#
- Cannot update F# without considering stdlib
- Tight coupling

## Requirements for Ref-by-Hash

### R1: Stable References for Builtins
F# code needs to reference Darklang types, somehow.

**Options**:

**Option A**: Keep PackageIDs forever (status quo+)
- Pro: Works now
- Con: Not content-based
- Con: Maintenance burden

**Option B**: Generate PackageIDs from canonical sources
- Parse canonical .dark files at build time
- Compute hashes
- Generate PackageIDs.fs automatically
- Pro: Automated, correct
- Con: Build complexity

**Option C**: Use concept IDs
- Assign each canonical type a stable concept ID
- F# references concept IDs
- Concept IDs map to current hash at runtime
- Pro: Decouples F# from content hashes
- Con: Adds layer of indirection

**Option D**: F# runtime lookup
- F# code doesn't hardcode IDs
- Runtime looks up by location: `pm.findType("Darklang", ["Stdlib"], "Option")`
- Pro: No hardcoding
- Con: Runtime overhead, async

### R2: Hash Verification
Validate PackageIDs hashes match content hashes:

```fsharp
module PackageIDsValidation =
  /// At startup or in tests, verify PackageIDs match actual content
  let validateAll (pm : PT.PackageManager) : Ply<Result<unit, List<ValidationError>>> =
    uply {
      let errors = []

      // For each entry in PackageIDs
      for (modules, name), expectedHash in PackageIDs.Type._lookup do
        // Look up actual type
        let! actualTypeOpt = pm.findType(None, { owner = "Darklang"; modules = modules; name = name })

        match actualTypeOpt with
        | None ->
          errors <- TypeNotFound(modules, name) :: errors
        | Some actualHash ->
          if actualHash <> expectedHash then
            errors <- HashMismatch(modules, name, expectedHash, actualHash) :: errors

      if List.isEmpty errors then
        return Ok ()
      else
        return Error errors
    }
```

### R3: Concept ID Layer (Recommended)
Introduce stable concept IDs above content hashes:

```fsharp
module PackageIDs =
  module Type =
    module Stdlib =
      // Stable concept IDs (never change)
      let optionConcept : ConceptID = "darklang-stdlib-option-v1"
      let resultConcept : ConceptID = "darklang-stdlib-result-v1"

  // At runtime, resolve concept → current hash
  let resolveType (concept : ConceptID) : Hash =
    ConceptRegistry.getCurrentHash concept
```

**Benefits**:
- F# references stable concept IDs
- Concept ID can point to different hashes over time
- Allows versioning and evolution
- Decouples F# from content

### R4: Automated Generation
Generate PackageIDs from canonical sources:

**Build process**:
1. Parse `packages/darklang/stdlib/*.dark`
2. Compute content hashes for all types/functions
3. Generate PackageIDs.fs with correct hashes
4. F# compilation uses generated file

**Implementation**:
```fsharp
// In build script
let generatePackageIDs () =
  let! packages = parseStdlib ()
  let types = collectTypes packages

  let code = generateFSharpCode types
  File.WriteAllText("backend/src/LibExecution/PackageIDs.fs", code)

let generateFSharpCode (types : List<PackageType>) : string =
  // Generate module structure
  // Generate let bindings for each type
  // Generate lookup table
  ...
```

**Challenges**:
- Build order: need to build enough of compiler to parse .dark files
- Bootstrapping: initial PackageIDs needed to build parser?
- Cross-platform: build must work everywhere

### R5: F# Runtime Lookup (Fallback)
If compile-time IDs impossible, use runtime lookup:

```fsharp
// Instead of:
let optionHash = PackageIDs.Type.Stdlib.option

// Use:
let! optionHash = pm.findType(None, { owner = "Darklang"; modules = ["Stdlib"]; name = "Option" })
```

**Cons**:
- Async everywhere
- Performance overhead
- Potential failures at runtime

**When needed**: During transition or if other approaches fail

### R6: Versioned Builtins
Support multiple versions of stdlib types:

```fsharp
module PackageIDs =
  module Type =
    module Stdlib =
      module Option =
        let v1 = "hash-of-option-v1"
        let v2 = "hash-of-option-v2"
        let current = v2  // Pointer to current version

// F# code can reference specific version or current
let! opt = pm.getType PackageIDs.Type.Stdlib.Option.current
```

### R7: Documentation and Testing
Comprehensive docs and tests:

```fsharp
module PackageIDsTests =
  /// Verify every PackageID has corresponding definition
  let testAllIDsExist () =
    for id in allPackageIDs do
      let! typeOpt = pm.getType id
      assert (Option.isSome typeOpt)

  /// Verify hashes match content
  let testHashesMatch () =
    for id in allPackageIDs do
      let! typ = pm.getType id
      let expectedHash = computeContentHash typ
      assert (id = expectedHash)

  /// Verify no missing stdlib types
  let testCompleteness () =
    let! stdlibTypes = pm.search(None, { currentModule = ["Darklang"; "Stdlib"]; ... })
    let knownTypes = Set.ofList allPackageIDs
    let foundTypes = Set.ofList (stdlibTypes.types |> List.map _.hash)
    assert (Set.isSubset foundTypes knownTypes)
```

## Proposed Changes

### Approach: Concept IDs (Recommended)

**Phase 1: Add Concept Layer**

```fsharp
// NEW: Concept IDs
type ConceptID = string  // e.g., "darklang-stdlib-option-v1"

module ConceptRegistry =
  let mutable private _concepts : Map<ConceptID, Hash> = Map.empty

  let register (conceptId : ConceptID) (hash : Hash) : unit =
    _concepts <- Map.add conceptId hash _concepts

  let getCurrentHash (conceptId : ConceptID) : Hash =
    Map.find conceptId _concepts

  let initialize (pm : PT.PackageManager) : Ply<unit> =
    uply {
      // Register all known concepts
      let! optionHash = pm.findType(None, { owner = "Darklang"; modules = ["Stdlib"]; name = "Option" })
      match optionHash with
      | Some hash -> register "darklang-stdlib-option-v1" hash
      | None -> failwith "Option type not found"

      // ... repeat for all stdlib types
    }
```

**Phase 2: Update PackageIDs**

```fsharp
module PackageIDs =
  module Concepts =
    module Type =
      module Stdlib =
        let option = "darklang-stdlib-option-v1"
        let result = "darklang-stdlib-result-v1"
        // ...

  module Type =
    // Legacy: direct hashes (deprecated)
    module Stdlib =
      [<Obsolete("Use Concepts.Type.Stdlib.option instead")>]
      let option = ConceptRegistry.getCurrentHash Concepts.Type.Stdlib.option

  // Runtime initialization
  let initialize (pm : PT.PackageManager) : Ply<unit> =
    ConceptRegistry.initialize pm
```

**Phase 3: Update Builtin Functions**

```fsharp
// OLD
let optionType = RT.TCustomType(
  Ok(RT.FQTypeName.fqPackage PackageIDs.Type.Stdlib.option),
  [RT.TVariable "a"])

// NEW
let optionType (pm : PT.PackageManager) =
  let hash = ConceptRegistry.getCurrentHash PackageIDs.Concepts.Type.Stdlib.option
  RT.TCustomType(
    Ok(RT.FQTypeName.fqPackage hash),
    [RT.TVariable "a"])

// Or with caching
let private _optionTypeCache = ref None
let optionType (pm : PT.PackageManager) =
  match !_optionTypeCache with
  | Some t -> t
  | None ->
    let hash = ConceptRegistry.getCurrentHash PackageIDs.Concepts.Type.Stdlib.option
    let t = RT.TCustomType(Ok(RT.FQTypeName.fqPackage hash), [RT.TVariable "a"])
    _optionTypeCache := Some t
    t
```

**Phase 4: Parser Integration**

```fsharp
// WrittenTypesToProgramTypes.fs
let toPT (typ : WT.PackageType) : PT.PackageType =
  // Compute content hash
  let contentHash = ContentHash.PackageType.hash typ

  // Check if this is a known concept
  let finalHash =
    match ConceptRegistry.tryFindConcept contentHash with
    | Some conceptId -> ConceptRegistry.getCurrentHash conceptId
    | None -> contentHash

  { id = finalHash; ... }
```

### Alternative: Automated Generation

If concept IDs too complex, generate from source:

**Build script** (`scripts/build/generate-package-ids.fsx`):
```fsharp
// 1. Parse stdlib
let! stdlibTypes = parseDirectory "packages/darklang/stdlib"

// 2. Compute hashes
let typeHashes =
  stdlibTypes
  |> List.map (fun t -> (t.name, ContentHash.PackageType.hash t))

// 3. Generate F# code
let code = generatePackageIDsModule typeHashes
File.WriteAllText("backend/src/LibExecution/PackageIDs.fs", code)
```

**Generated PackageIDs.fs**:
```fsharp
// AUTO-GENERATED - DO NOT EDIT
// Generated from: packages/darklang/stdlib
// Generated at: 2025-01-15T12:34:56Z

module LibExecution.PackageIDs

open Prelude

module Type =
  module Stdlib =
    let option = Hash.parse "abc123..."  // Actual content hash
    let result = Hash.parse "def456..."
    // ...
```

**CI check**: Verify generated file is up-to-date
```bash
#!/bin/bash
# In CI pipeline
./scripts/build/generate-package-ids.sh
git diff --exit-code backend/src/LibExecution/PackageIDs.fs || {
  echo "PackageIDs.fs is out of date. Run generate-package-ids.sh"
  exit 1
}
```

## Code Impacts

### Files to Change

**Immediate**:
- `LibExecution/PackageIDs.fs`:
  - Add ConceptID layer, or
  - Make auto-generated

- NEW: `ConceptRegistry.fs`:
  - Implement concept → hash mapping
  - Runtime initialization

- `BuiltinExecution/Libs/*.fs`:
  - Update to use concept IDs
  - Or accept PM parameter for lookup

**Second Order**:
- Build scripts:
  - Add generation step if automated
  - Add validation step

- Tests:
  - Validate PackageIDs correctness
  - Test concept registry

### Migration Strategy

**Phase 1: Audit**
- Identify all uses of PackageIDs
- Document dependencies

**Phase 2: Add concept layer**
- Implement ConceptRegistry
- Don't change existing code yet
- Test parallel

**Phase 3: Gradual migration**
- Migrate one builtin lib at a time
- Verify tests pass
- Monitor performance

**Phase 4: Cleanup**
- Remove direct hash references
- Keep concept IDs only

## Open Questions

### Q1: Concept ID Format
**Question**: How to structure concept IDs?

**Options**:
- String: "darklang-stdlib-option-v1"
- UUID: "9ce3b596-968f-44c9-bcd4-511007cd9225"
- Structured: `{ owner: "Darklang"; module: "Stdlib"; name: "Option"; version: 1 }`

**Recommendation**: String with convention, easy to read and maintain

### Q2: Concept Versioning
**Question**: How to version concepts when implementation changes?

**Options**:
- New concept ID: "darklang-stdlib-option-v2"
- Version suffix in current ID
- Separate version field

**Recommendation**: New concept ID for breaking changes, keep old for compat

### Q3: Performance Impact
**Question**: Will runtime lookup of concept → hash be too slow?

**Mitigation**:
- Cache at builtin initialization
- Pre-populate at startup
- Lazy initialization

**Recommendation**: Cache aggressively, measure if becomes bottleneck

### Q4: Bootstrapping
**Question**: How to initialize concept registry before parsing stdlib?

**Chicken-egg**: Need PM to look up types, but PM needs types to be parsed

**Solution**: Multi-phase:
1. Initialize empty PM
2. Parse stdlib with placeholder IDs
3. Compute content hashes
4. Register concepts
5. Re-initialize PM with correct hashes

### Q5: Testing Strategy
**Question**: How to test that builtins correctly reference stdlib?

**Tests needed**:
- Every builtin that references a type has valid concept ID
- Every concept ID resolves to existing type
- Concept hashes match content hashes (if using generation)

### Q6: Failure Handling
**Question**: What if concept lookup fails at runtime?

**Options**:
- Crash (fail fast)
- Return error (graceful)
- Use fallback (unsafe)

**Recommendation**: Crash during initialization, not during execution

### Q7: Documentation
**Question**: How to document which concepts exist and what they map to?

**Needed**:
- List of all concept IDs
- Mapping to current hashes
- History of changes
- Migration guide

## Summary

**Key Insights**:
1. F# cannot use pure content hashes (compile-time constraint)
2. Need stable references that outlive content changes
3. Concept IDs decouple F# from content hashes
4. Auto-generation possible but complex
5. Runtime lookup feasible with caching

**Biggest Risks**:
1. Bootstrapping complexity
2. Performance of runtime lookup
3. Build process complexity (if auto-generating)
4. Migration path for existing code
5. Maintaining concept → hash mappings

**Next Steps**:
1. Prototype concept registry
2. Test performance of runtime lookup
3. Migrate one builtin lib as proof-of-concept
4. Evaluate auto-generation feasibility
5. Choose approach and document thoroughly
