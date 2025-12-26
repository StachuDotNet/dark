# Layer 1: ProgramTypes Analysis

## Current State

### Identity Model
ProgramTypes currently uses **Hash-based identity** for package items:
- `FQTypeName.Package = Hash`
- `FQValueName.Package = Hash`
- `FQFnName.Package = Hash`

### Type References
The `TCustomType` variant stores:
```fsharp
TCustomType of
  NameResolution<FQTypeName.FQTypeName> *
  typeArgs : List<TypeReference>
```

**Critical TODOs in the code:**
- Line 257: "TODO: this reference should be by-hash"
- Line 258: "but, the Location might be nice to have as well, _optionally_"
- Line 259: "so we know which instance of some type/fn we were referring to"

### Expression References
Multiple expression types reference items:
- `EFnName` (line 355): By hash ✅
- `ERecord` (line 370): Has TODO comment - should be by-hash
- `EEnum` (line 395): Has TODO comment - should be by-hash
- `EValue` (line 403): Has TODO comment - should be by-hash
- `ESelf` (line 410): Self-reference mechanism

### Self-Recursion
`ESelf` exists for self-recursive functions but:
- Only handles direct recursion
- Cannot handle mutual recursion
- No cycle index system
- Comments suggest need for MutualType mechanism (lines 567-571)

### Package Items
All three types store hash-based IDs:
```fsharp
PackageType.PackageType = { id : FQTypeName.Package; ... }
PackageFn.PackageFn = { id : FQFnName.Package; ... }
PackageValue.PackageValue = { id : FQValueName.Package; ... }
```

### Metadata Storage
Currently in PT:
- `description : string` (in all package types)
- `deprecated : Deprecation<...>` (in all package types)
- Comments suggest this might belong elsewhere (lines 549-556, 595-606)

### PackageManager Type
```fsharp
type PackageManager = {
  findType : (BranchIDOpt * PackageLocation) -> Ply<Option<FQTypeName.Package>>
  findValue : (BranchIDOpt * PackageLocation) -> Ply<Option<FQValueName.Package>>
  findFn : (BranchIDOpt * PackageLocation) -> Ply<Option<FQFnName.Package>>

  getType : FQTypeName.Package -> Ply<Option<PackageType.PackageType>>
  getValue : FQValueName.Package -> Ply<Option<PackageValue.PackageValue>>
  getFn : FQFnName.Package -> Ply<Option<PackageFn.PackageFn>>

  // Missing: getTypeLocation, getValueLocation, getFnLocation

  search : BranchIDOpt * Search.SearchQuery -> Ply<Search.SearchResults>
  applyOps : (BranchIDOpt * List<PackageOp>) -> Ply<unit>
  init : Ply<unit>
}
```

Note: The actual SQL implementation has `getTypeLocation`, `getValueLocation`, `getFnLocation` functions but they're not in the PT.PackageManager type signature.

## Problems

### 1. Identity vs Location Confusion
**Problem**: Hash identifies content, but we also need location info for:
- User-facing display (which module is this from?)
- Disambiguation when multiple types have same shape
- Error messages and debugging

**Current approach**: Hash alone, with optional location via separate queries

**Issue**: This creates a two-step lookup pattern everywhere:
1. Get hash by location
2. Get content by hash
3. (Sometimes) Get location by hash again for display

### 2. Mutual Recursion Unsolved
**Problem**: Cannot properly hash mutually recursive types:
```dark
type A = | Tag1 of B
type B = | Tag2 of A
```

**Current approach**: Comments mention "MutualType#1" and cycle indices but not implemented

**Issue**: Without SCC-based hashing, mutual recursion creates:
- Circular dependency in hash computation
- Unstable hashes depending on parse order
- Cannot reference other type during hashing

### 3. Name Sensitivity
**Problem**: If names are included in hashes, then:
- Renaming a private helper changes the hash
- But the semantic content is identical
- Breaks semantic addressing

**Current approach**: Names are serialized and thus hashed

**Issue**: `type Foo = Int64` vs `type Bar = Int64` have different hashes even though they're semantically identical aliases

### 4. ESelf Limitations
**Problem**: `ESelf` only handles direct self-recursion

**Cannot represent**:
- Mutual recursion between functions
- Indirect recursion (A calls B calls A)
- Type-level mutual recursion

**Should we have**: Reference-by-index within SCC?

### 5. Metadata Placement
**Problem**: Description and deprecation live in PT but aren't needed for:
- Hash computation
- Runtime execution
- Type checking

**Questions**:
- Should metadata move to a separate layer?
- How to associate metadata with hashes?
- Multiple locations might want different descriptions

### 6. Missing Reverse Lookup
**Problem**: PM can go Location → Hash → Content, but not Hash → Locations (plural)

**Needed for**:
- Finding all places where a hash appears
- Understanding hash collisions
- Migration/refactoring tools

## Requirements for Ref-by-Hash

### R1: Hash + Optional Location
TypeReferences should store:
```fsharp
TCustomType of
  hash : Hash *                      // Content identity (required)
  location : Option<PackageLocation> * // Display hint (optional)
  typeArgs : List<TypeReference>
```

**Why**:
- Hash for semantic identity and lookup
- Location for display and disambiguation
- Location can be recovered via reverse lookup if missing

### R2: Mutual Recursion via SCC
Need strongly connected component analysis:

```fsharp
type SCCIndex = int
type SCCHash = Hash  // Hash of entire SCC

type TypeReference =
  | TCustomType of hash : Hash * ...
  | TMutualRef of
      sccHash : SCCHash *     // Hash of the SCC
      index : SCCIndex *       // Which type within SCC
      typeArgs : List<TypeReference>
```

**Or** simpler: Just hash the entire SCC together and use qualified names within:
```fsharp
// When hashing type A in SCC {A, B}:
// - Reference to B becomes "SCC#0:B" instead of hash
// - After SCC is hashed, all refs use the same SCC hash
```

### R3: Name-Insensitive Hashing (Maybe)
**Option A**: Exclude names from hashing
- Pro: Semantic equivalence preserved
- Con: `type A = Int64` and `type B = Int64` collide

**Option B**: Include names but normalize
- Pro: Avoids unwanted collisions
- Con: Renaming changes hash

**Option C**: Hash content + maintain alias table
- Hash the structure: `Alias TInt64`
- Separately track: `Location:Foo → Hash:abc`
- Pro: Best of both worlds
- Con: More complex

**Recommendation**: Option C - hash structure, separate name→hash mapping

### R4: Enhanced ESelf or Cycle Indices
**For expressions**:
```fsharp
type Expr =
  | ... existing ...
  | ESelfRef of cycleIndex : int  // Ref to enclosing def at index
```

**For types**:
```fsharp
type TypeReference =
  | ... existing ...
  | TSelfRef of cycleIndex : int
```

Where cycleIndex = 0 means "immediately enclosing", 1 means "one level out", etc.

### R5: Metadata Separation
**Option A**: Keep in PT but mark as "not hashed"
```fsharp
type PackageType = {
  id : Hash           // Content hash
  declaration : ...   // What gets hashed

  // Metadata (not hashed)
  description : string
  deprecated : Deprecation<...>
}
```

**Option B**: Move to separate MetadataManager
```fsharp
type PackageManager = {
  getType : Hash -> Option<TypeDeclaration>
  getMetadata : Hash -> Option<Metadata>
}
```

**Recommendation**: Option A for now (simpler), with clear separation during hashing

### R6: Bidirectional Location Mapping
```fsharp
type PackageManager = {
  // Current
  findType : Location -> Option<Hash>
  getType : Hash -> Option<PackageType>

  // New
  getLocations : Hash -> List<PackageLocation>  // All locations for this hash
  getPreferredLocation : Hash -> Option<PackageLocation>  // "Canonical" one
}
```

## Proposed Changes

### 1. TypeReference Enhancement
```fsharp
type TypeReference =
  | TUnit | TBool | ... // primitives unchanged

  | TCustomType of
      hash : Hash *
      sourceLocation : Option<PackageLocation> * // Where parsed from
      typeArgs : List<TypeReference>

  | TSelfRef of distance : int  // 0 = immediate parent

  | TFn of arguments : NEList<TypeReference> * ret : TypeReference
  | TVariable of string
  | TDB of TypeReference
```

**Migration**: Parse with location, compute hash during WT→PT

### 2. Expression Enhancement
```fsharp
type Expr =
  | ... existing ...

  | EFnName of id * hash : Hash * location : Option<PackageLocation>
  | EValue of id * hash : Hash * location : Option<PackageLocation>
  | ESelfRef of id * distance : int
```

**For records/enums**: Similarly store hash + optional location

### 3. PackageType Structure
```fsharp
type PackageType = {
  id : Hash  // Content hash of declaration
  declaration : TypeDeclaration.T

  // Metadata (excluded from hash computation)
  metadata : Metadata
}

type Metadata = {
  description : string
  deprecated : Deprecation<FQTypeName>
  author : Option<string>
  since : Option<Version>
}
```

### 4. PackageManager API
```fsharp
type PackageManager = {
  // Location → Hash
  findType : BranchIDOpt * PackageLocation -> Ply<Option<Hash>>
  findValue : BranchIDOpt * PackageLocation -> Ply<Option<Hash>>
  findFn : BranchIDOpt * PackageLocation -> Ply<Option<Hash>>

  // Hash → Content
  getType : Hash -> Ply<Option<PackageType>>
  getValue : Hash -> Ply<Option<PackageValue>>
  getFn : Hash -> Ply<Option<PackageFn>>

  // Hash → Locations (NEW)
  getTypeLocations : Hash -> Ply<List<PackageLocation>>
  getValueLocations : Hash -> Ply<List<PackageLocation>>
  getFnLocations : Hash -> Ply<List<PackageFn>>

  // Hash → Preferred Location (NEW)
  getTypeLocation : BranchIDOpt * Hash -> Ply<Option<PackageLocation>>
  getValueLocation : BranchIDOpt * Hash -> Ply<Option<PackageLocation>>
  getFnLocation : BranchIDOpt * Hash -> Ply<Option<PackageFn>>

  // Search
  search : BranchIDOpt * Search.SearchQuery -> Ply<Search.SearchResults>

  // Ops
  applyOps : BranchIDOpt * List<PackageOp> -> Ply<unit>

  init : Ply<unit>
}
```

### 5. Hashing Protocol
```fsharp
module ContentHash =
  module PackageType =
    /// Hash a type declaration to content hash
    /// Normalizes:
    /// - Sets id field to Hash.empty
    /// - Excludes metadata
    /// - Handles mutual recursion via SCC
    let hash (typ : PackageType) : Hash

    /// Hash a group of mutually recursive types
    let hashSCC (types : NEList<PackageType>) : Hash
```

## Code Impacts

### Files to Change

**Immediate**:
- `LibExecution/ProgramTypes.fs` - Add location fields to TypeReference/Expr
- `LibSerialization/Hashing/ContentHash.fs` - Implement SCC hashing
- `LibParser/WrittenTypesToProgramTypes.fs` - Populate location during parse
- `LibPackageManager/PackageManager.fs` - Add location query methods

**Second Order**:
- All code that pattern matches on TypeReference (add location param)
- All code that creates TCustomType (pass location)
- Display/pretty-printing code (use location when available)

**Testing**:
- Add tests for mutual recursion hashing
- Add tests for location preservation
- Add tests for hash stability

### Compatibility Considerations

**Breaking change**: TypeReference shape changes
- Need to update all pattern matches
- Need to update all constructors
- Can maintain None for location during migration

**Non-breaking**: Adding getLocations methods
- Pure addition to PM interface
- Existing code doesn't need them

## Open Questions

### Q1: Location Storage Strategy
**Option A**: Store location in TypeReference at parse time
- Pro: Always available for display
- Con: Bulkier AST

**Option B**: Store only hash, look up location on demand
- Pro: Smaller AST
- Con: Extra DB queries for display

**Recommendation**: Store location at parse time (Option A), it's just a few strings

### Q2: SCC Detection Timing
**When to detect mutual recursion?**

**Option A**: During parsing (WT→PT)
- Pro: Can hash correctly immediately
- Con: Parser becomes more complex

**Option B**: During hash computation
- Pro: Parser stays simple
- Con: Need to track definitions during hashing

**Option C**: Separate analysis pass
- Pro: Clean separation of concerns
- Con: Extra pass over data

**Recommendation**: Option B - detect cycles during hash computation

### Q3: Hash Collision Handling
**What if two different types hash to same value?**

Very unlikely with SHA256, but:
- Store both in DB under same hash?
- Detect and error?
- Include location in hash as tiebreaker?

**Recommendation**: Detect and error loudly - hash collision means bug in content normalization or cosmic bad luck

### Q4: Builtin Integration
**How do F# builtins reference Darklang types?**

Current: PackageIDs.fs has hardcoded Guids
Future: Need stable hashes

**Options**:
- Keep Guid→Hash conversion in PackageIDs
- Generate hashes at build time from canonical definitions
- Accept that builtins use special "blessed" hashes

**Recommendation**: Keep PackageIDs.fs for now, but compute hashes from canonical .dark files eventually

## Summary

**Key Insights**:
1. Hash is for identity, location is for display
2. Mutual recursion needs SCC-based hashing
3. Metadata should be separated from hashed content
4. Need bidirectional location↔hash mapping
5. ESelf works for simple recursion but need cycle indices for complex cases

**Biggest Risks**:
1. Breaking changes to TypeReference/Expr patterns everywhere
2. Getting mutual recursion hashing wrong
3. Performance of location lookups
4. Migration path from current system

**Next Steps**:
1. Prototype SCC hashing algorithm
2. Add location field to TypeReference (start with Option)
3. Implement getLocations in PM
4. Update one subsystem end-to-end as proof of concept
