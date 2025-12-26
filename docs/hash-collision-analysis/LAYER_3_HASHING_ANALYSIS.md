# Layer 3: Hashing Analysis

## Current State

### Core Infrastructure
`LibSerialization.Hashing.ContentHash` provides:

```fsharp
/// Compute SHA256 hash of bytes
let hashBytes (bytes : byte[]) : Hash

/// Serialize a value using a binary writer function, then hash it
let hashWithWriter<'T> (writer : BinaryWriter -> 'T -> unit) (value : 'T) : Hash
```

**Pattern**: Serialize to binary → hash bytes → return Hash

### Existing Hashing Functions

**PackageLocation** (lines 33-42):
```fsharp
let hash (loc : LibExecution.ProgramTypes.PackageLocation) : Hash =
  hashWithWriter Serializers.PT.Common.PackageLocation.write loc
```
Note: "Location-based hashing means IDs change when items move"

**PackageOp** (lines 46-54):
```fsharp
let hash (op : LibExecution.ProgramTypes.PackageOp) : Hash =
  hashWithWriter Serializers.PT.PackageOp.write op
```
Used for deduplication

**PackageType** (lines 58-67):
```fsharp
let hash (typ : LibExecution.ProgramTypes.PackageType.PackageType) : Hash =
  let normalized = { typ with id = Hash.empty }
  hashWithWriter Serializers.PT.PackageType.write normalized
```

**PackageFn** (lines 71-80):
```fsharp
let hash (fn : LibExecution.ProgramTypes.PackageFn.PackageFn) : Hash =
  let normalized = { fn with id = Hash.empty }
  hashWithWriter Serializers.PT.PackageFn.write normalized
```

**PackageValue** (lines 84-93):
```fsharp
let hash (value : LibExecution.ProgramTypes.PackageValue.PackageValue) : Hash =
  let normalized = { value with id = Hash.empty }
  hashWithWriter Serializers.PT.PackageValue.write normalized
```

### Normalization Strategy
All package item hashing:
1. Sets `id` field to `Hash.empty` before hashing
2. This prevents circular dependency (hash depends on content, not ID)
3. Serializes entire structure
4. Hashes serialized bytes

### What Gets Hashed
Currently includes:
- Type declarations (fields, cases, type params)
- Function declarations (params, return type, type params)
- Function/value bodies (full PT.Expr tree)
- Names (module names, field names, parameter names)
- Metadata (descriptions, deprecation info)

### What Does NOT Get Hashed
- The `id` field itself (set to Hash.empty)
- Nothing is explicitly excluded except the ID

### Binary Serialization Dependency
Hashing relies on:
- `LibSerialization.Binary.Serializers.PT.*` modules
- Format is stable (changes would invalidate all hashes)
- Format is deterministic (same input → same bytes)

## Problems

### 1. Mutual Recursion Impossible
**Problem**: Cannot hash mutually recursive types:

```dark
type A = | Tag of B
type B = | Other of A
```

**Why**:
- To hash A, need to serialize TypeReference to B
- To get hash of B, need to serialize TypeReference to A
- Circular dependency - cannot compute either hash

**Current approach**: Not handled - likely causes infinite loop or uses whatever temporary ID is available

### 2. Name Sensitivity
**Problem**: Names are included in serialization

**Impact**:
```dark
type Foo = Int64
type Bar = Int64
```
These have DIFFERENT hashes even though semantically identical.

**Also affects**:
- Renaming a field changes the hash
- Renaming a parameter changes the hash
- But the semantic behavior is identical

**Question**: Is this desired or not?

### 3. Metadata Affects Hash
**Problem**: `description` and `deprecated` are serialized and hashed

**Impact**:
- Fixing a typo in description changes the hash
- Marking something deprecated changes the hash
- But the actual functionality is unchanged

**Should**: Metadata be excluded from content hash?

### 4. No SCC Detection
**Problem**: No strongly connected component analysis

**Needed for**:
- Mutually recursive types
- Mutually recursive functions
- Computing a single stable hash for the group

**Current**: Each item hashed individually, no group hashing

### 5. Determinism Assumptions
**Problem**: Assumes binary serialization is deterministic

**Risks**:
- Map iteration order (if maps are serialized)
- Float representation
- GUID generation for internal IDs
- Set iteration order

**Current**: Serializers appear deterministic but not explicitly validated

### 6. Format Versioning
**Problem**: No version marker in hashes

**Impact**:
- If binary format changes, all hashes change
- No way to know which version generated a hash
- Cannot migrate or detect incompatibility

**Needed**: Version prefix on hashes? Or separate hash-version tracking?

## Requirements for Ref-by-Hash

### R1: SCC-Based Hashing
Need to hash groups of mutually recursive definitions:

**Algorithm**:
1. Detect cycles using DFS
2. Group cycles into SCCs
3. Hash each SCC as a unit:
   - Sort members by name (for determinism)
   - For refs within SCC: use placeholder (e.g., "SCC#0")
   - For refs outside SCC: use actual hash
   - Hash the concatenated serializations

**Example**:
```dark
type A = | Tag of B
type B = | Other of A
```

Hashing:
1. Detect SCC: {A, B}
2. For A: serialize "| Tag of SCC#B"
3. For B: serialize "| Other of SCC#A"
4. Hash: SHA256(serialize(A) + serialize(B))
5. Both A and B get the same SCC hash

### R2: Selective Normalization
Need to exclude certain fields from hashing:

```fsharp
type NormalizationConfig = {
  includeNames : bool
  includeMetadata : bool
  includeTypeParams : bool  // or rename them to 'a, 'b, ...
}

let hashWithNormalization
  (config : NormalizationConfig)
  (typ : PackageType)
  : Hash
```

**For semantic hashing**:
- Exclude metadata (description, deprecation)
- Normalize or exclude names (debatable)
- Normalize type parameter names ('a, 'b instead of original)

**For strict hashing**:
- Include everything (current behavior)
- Useful for exact-match deduplication

### R3: Multi-Level Hashing
**Content hash**: Hash of semantic structure
**Location hash**: Hash of content + location
**Full hash**: Hash of content + location + metadata

```fsharp
type HashSet = {
  contentHash : Hash      // Semantic structure only
  locationHash : Hash     // + location info
  fullHash : Hash         // + metadata
}
```

Use cases:
- `contentHash` for deduplication (same structure = same hash)
- `locationHash` for tracking moves
- `fullHash` for exact matching (including docs)

### R4: Determinism Validation
Add explicit checks:

```fsharp
/// Hash twice and verify same result
let validateDeterministic (value : 'T) : unit =
  let hash1 = hash value
  let hash2 = hash value
  if hash1 <> hash2 then
    failwith "Non-deterministic serialization detected"

/// Test suite: hash 1000x, verify all equal
```

### R5: Hash Format Versioning
Include version in hash:

**Option A**: Version prefix
```fsharp
type Hash = {
  version : byte  // 1 for current format
  hash : byte[31]  // Rest of SHA256
}
```

**Option B**: Separate tracking
```fsharp
type VersionedHash = {
  version : int
  hash : Hash  // Full 32 bytes
}
```

**Recommendation**: Option B - keep Hash pure, track version in DB

### R6: Incremental Hashing
For large definitions, hash parts separately:

```fsharp
type TypeHash = {
  declarationHash : Hash  // Just the structure
  fieldHashes : List<Hash>  // Each field independently
  combinedHash : Hash  // Final hash
}
```

Benefits:
- Can detect which part changed
- Can share common field hashes
- Better diff/debugging

## Proposed Changes

### 1. SCC Detection and Hashing
```fsharp
module ContentHash =
  module SCC =
    /// Detect strongly connected components in type definitions
    let detect (types : List<PT.PackageType>) : List<NEList<PT.PackageType>>

    /// Hash a group of mutually recursive types
    let hash (scc : NEList<PT.PackageType>) : Hash =
      // 1. Sort by name for determinism
      let sorted = scc |> NEList.sortBy (fun t -> ...)

      // 2. Create placeholder map for internal refs
      let placeholders = Map.ofList [ ... ]

      // 3. Serialize each with placeholders
      let serialized =
        sorted
        |> NEList.map (fun t -> serializeWithPlaceholders placeholders t)
        |> NEList.toList

      // 4. Concatenate and hash
      let combined = Array.concat serialized
      hashBytes combined

    /// Serialize a type, replacing intra-SCC refs with placeholders
    let private serializeWithPlaceholders
      (placeholders : Map<Hash, string>)
      (typ : PT.PackageType)
      : byte[]
```

### 2. Normalization Options
```fsharp
type NormalizationMode =
  | Semantic  // Exclude names, metadata
  | Structural  // Include names, exclude metadata
  | Exact  // Include everything

module PackageType =
  let hash (mode : NormalizationMode) (typ : PT.PackageType) : Hash =
    let normalized =
      match mode with
      | Semantic ->
        { typ with
            id = Hash.empty
            description = ""
            deprecated = PT.NotDeprecated
            // TODO: normalize type param names
        }
      | Structural ->
        { typ with
            id = Hash.empty
            description = ""
            deprecated = PT.NotDeprecated
        }
      | Exact ->
        { typ with
            id = Hash.empty
        }

    hashWithWriter Serializers.PT.PackageType.write normalized
```

### 3. Multi-Level Hash Computation
```fsharp
type HashLevels = {
  semantic : Hash    // Structure only
  structural : Hash  // + names
  full : Hash        // + metadata
}

module PackageType =
  let hashAll (typ : PT.PackageType) : HashLevels =
    { semantic = hash Semantic typ
      structural = hash Structural typ
      full = hash Exact typ }

  /// Default: use structural hash (includes names, excludes metadata)
  let hash (typ : PT.PackageType) : Hash =
    hash Structural typ
```

### 4. Determinism Tests
```fsharp
module Tests =
  let testDeterminism () =
    let typ = /* create test type */

    // Hash 1000 times
    let hashes =
      List.init 1000 (fun _ -> ContentHash.PackageType.hash typ)

    // All must be equal
    let unique = Set.ofList hashes
    assert (Set.count unique = 1)

  let testMapOrdering () =
    // Create type with fields in different orders
    let typ1 = /* fields: [a; b; c] */
    let typ2 = /* fields: [c; b; a] but same structure */

    // Hashes should be equal if serialization normalizes order
    assert (hash typ1 = hash typ2)
```

### 5. Hash Format Documentation
```fsharp
/// Content hashing for Darklang package items
///
/// VERSIONING:
/// - Version 1 (current): SHA256 of binary serialized PT
/// - Format changes increment version
/// - Old hashes remain valid but use old version
///
/// NORMALIZATION:
/// - ID field always set to Hash.empty before hashing
/// - Metadata (description, deprecation) excluded
/// - Names are included (structural identity)
/// - Type parameters kept as-is
///
/// MUTUAL RECURSION:
/// - SCCs detected and hashed as a group
/// - Internal refs use placeholders
/// - All members of SCC get same hash
```

### 6. Incremental Hashing (Future)
```fsharp
module Incremental =
  type FieldHash = {
    name : string
    typ : TypeReference
    hash : Hash
  }

  type TypeHashBreakdown = {
    structureHash : Hash  // Just the type name, params
    fieldHashes : List<FieldHash>
    combinedHash : Hash  // Final hash
  }

  let hashWithBreakdown (typ : PT.PackageType) : TypeHashBreakdown
    // Allows: "Hash changed because field #2 changed"
```

## Code Impacts

### Files to Change

**Immediate**:
- `LibSerialization/Hashing/ContentHash.fs`:
  - Add SCC detection
  - Add SCC.hash function
  - Add normalization modes
  - Add multi-level hashing

- `LibSerialization/Binary/Serializers/PT/*`:
  - Ensure deterministic serialization
  - Document ordering assumptions
  - Add tests for determinism

**Second Order**:
- All callers of `ContentHash.PackageType.hash` etc:
  - May need to specify normalization mode
  - Or use default (structural)

- `LibPackageManager/PackageManager.fs`:
  - Use SCC hashing for groups of types
  - Update stabilization to use content hashes

**Testing**:
- Add tests for SCC detection
- Add tests for SCC hashing
- Add tests for determinism
- Add tests for normalization modes

### Migration Strategy

**Phase 1: Add SCC support (preserve existing behavior)**
- Add SCC detection
- Add SCC.hash that uses same normalization as current
- Don't change existing hash calls yet

**Phase 2: Test and validate**
- Run on all existing packages
- Verify hashes are deterministic
- Verify SCC detection works
- Verify no regressions

**Phase 3: Switch to content hashing**
- Update parser to use SCC hashing
- Update PackageIDs to use content hashing
- Migration plan for existing hashes

**Phase 4: Add normalization modes**
- Add semantic vs structural vs exact
- Let users choose (default: structural)
- Update docs

## Open Questions

### Q1: Name Sensitivity
**Should names affect the hash?**

**Option A**: Yes (current)
- Pro: `type Foo = Int64` and `type Bar = Int64` are different
- Con: Renaming changes hash

**Option B**: No (semantic hashing)
- Pro: Renaming preserves hash
- Con: `type Foo = Int64` and `type Bar = Int64` collide

**Option C**: Configurable
- Pro: Best of both worlds
- Con: More complexity

**Recommendation**: Option A (include names) with future support for Option C

### Q2: Metadata in Hash
**Should description/deprecation affect the hash?**

**Strong opinion**: No
- Fixing typos should not change the hash
- Adding docs should not change the hash
- Marking deprecated should not change the hash

**Recommendation**: Exclude metadata from content hash, include in separate metadata hash

### Q3: Type Parameter Names
**Should type parameter names affect hash?**

```dark
type Foo<t> = | Some of t
type Bar<u> = | Some of u
```

**Option A**: Different hashes (names differ)
**Option B**: Same hash (structure identical)

**Recommendation**: Option A (include names) but normalize to 'a, 'b, 'c in semantic mode

### Q4: Field Order
**Should field order affect hash?**

```dark
type Foo = { x: Int64; y: String }
type Bar = { y: String; x: Int64 }
```

**Option A**: Different hashes (strict structural)
**Option B**: Same hash (normalize by sorting fields)

**Current**: Different hashes (serialization order matters)

**Recommendation**: Keep Option A (order matters) - field order IS part of structure

### Q5: Hash Collision Strategy
**What to do if two different items hash to same value?**

**Option A**: Detect and error
- Pro: Forces fix
- Con: Might be false positive

**Option B**: Store both, distinguish somehow
- Pro: No blocking
- Con: Ambiguity in system

**Option C**: Include location as tiebreaker
- Pro: Avoids collision
- Con: No longer pure content hash

**Recommendation**: Option A - detect and error. SHA256 collisions are astronomically unlikely; if one occurs, it indicates a bug in normalization.

### Q6: Incremental Hashing Value
**Is incremental hashing worth the complexity?**

**Pros**:
- Better diagnostics ("field #3 changed")
- Potential for partial recomputation
- Better diffs

**Cons**:
- Much more complex
- Storage overhead
- Might not be used

**Recommendation**: Not for MVP. Add later if needed for diff tooling.

### Q7: Hash Format Evolution
**How to handle format changes over time?**

**Option A**: Include version in hash
- Pro: Self-describing
- Con: Reduces hash space

**Option B**: Track version separately
- Pro: Full hash space
- Con: Need external version tracking

**Option C**: New hash algorithm name
- Pro: Clear distinction
- Con: Multiple hash types in system

**Recommendation**: Option B - track version in DB, keep hashes pure

## Summary

**Key Insights**:
1. SCC detection is critical for mutual recursion
2. Normalization is key - what to include/exclude
3. Determinism must be validated, not assumed
4. Multiple hash levels might be useful (semantic vs structural vs full)
5. Hash format will evolve - need versioning strategy

**Biggest Risks**:
1. Non-deterministic serialization causing hash instability
2. Getting SCC detection wrong
3. Performance overhead of SCC analysis
4. Hash format changes invalidating all existing hashes
5. Choosing wrong normalization strategy

**Next Steps**:
1. Implement SCC detection algorithm
2. Implement SCC hashing with placeholders
3. Add extensive determinism tests
4. Test on existing packages
5. Validate hashes are stable
6. Document hash format and guarantees
