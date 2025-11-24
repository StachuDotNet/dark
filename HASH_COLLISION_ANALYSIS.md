# Hash Collision Analysis: Content-Based Package IDs

## The Problem

After migrating package IDs from UUID to content-based Hash, we discovered a fundamental issue: **types with identical content in different module locations hash to the same value**.

### Example That Fails

```darklang
module MyModule1 =
  type ID = Int64           // Hash("ID", Int64) → abc123

  module MyModule2 =
    type ID = Int64         // Hash("ID", Int64) → abc123 (COLLISION!)

    module MyModule3 =
      type ID = Int64       // Hash("ID", Int64) → abc123 (COLLISION!)
```

All three type definitions produce the same Hash because the Hash is computed from:
- Type name: "ID"
- Type definition: Int64
- NOT including: module path

### What Goes Wrong

1. **During Parsing**: Parser creates PackageOps:
   ```fsharp
   AddType { id = abc123; ... }
   SetTypeName(abc123, {owner=""; modules=["MyModule1"]; name="ID"})
   SetTypeName(abc123, {owner=""; modules=["MyModule1";"MyModule2"]; name="ID"})
   SetTypeName(abc123, {owner=""; modules=["MyModule1";"MyModule2";"MyModule3"]; name="ID"})
   ```

2. **In PackageManager**: The `SetTypeName` ops update `typeIdToLoc` dictionary:
   ```fsharp
   typeIdToLoc[abc123] = MyModule1.ID           // First
   typeIdToLoc[abc123] = MyModule1.MyModule2.ID // Overwrites!
   typeIdToLoc[abc123] = MyModule1.MyModule2.MyModule3.ID // Overwrites again!
   ```

3. **During Pretty-Printing**: When looking up where each type belongs:
   ```fsharp
   pmGetLocationByType(branchID, abc123)
   // Returns: MyModule1.MyModule2.MyModule3 for ALL three types
   ```

4. **Result**: All three types appear in MyModule1 instead of their proper nested locations.

---

## The Core Question

**Should two types with identical definitions in different modules have the same ID or different IDs?**

This is fundamentally about identity vs. structural equality.

---

## Option 1: Location-Aware Hashing (Unique IDs per Location)

**Include module path in the Hash computation.**

### Implementation
```fsharp
// Current (wrong):
Hash(typeName, typeDefinition)

// Proposed:
Hash(owner, modules, typeName, typeDefinition)
```

### Pros
- ✅ Each type definition gets a unique ID
- ✅ No collisions in typeIdToLoc mappings
- ✅ Pretty-printing works correctly
- ✅ Clear 1:1 correspondence between definitions and IDs
- ✅ Aligns with how types are actually referenced (by full path)
- ✅ Simple to implement - just change the Hash computation

### Cons
- ❌ Moving a type to a different module changes its ID
- ❌ Breaks content addressability - same content → different hashes
- ❌ Two identical types in different locations can't share implementations
- ❌ Defeats the purpose of content-based addressing
- ❌ More like "location-based IDs with content validation" than true content addressing

### Analysis
This essentially gives up on content-based addressing. The ID becomes tied to location, not content. While this fixes the immediate problem, it abandons the benefits of content addressing:
- Can't detect duplicate definitions across modules
- Can't share implementations of identical types
- Refactoring (moving types) changes IDs unnecessarily

**Verdict**: This works but defeats the purpose of the Hash migration.

---

## Option 2: Composite Keys (ID + Location)

**Keep content-based IDs but use (ID, Location) tuples as keys everywhere.**

### Implementation
```fsharp
// Current:
typeIdToLoc: Map<Hash, Location>
typeLocToId: Map<Location, Hash>

// Proposed: Remove typeIdToLoc entirely, or make it multi-valued
getTypeLocation: Hash -> List<Location>  // Returns ALL locations with this type
```

### Pros
- ✅ Preserves content-based addressing
- ✅ Can detect identical types across modules (same Hash)
- ✅ Multiple definitions can coexist
- ✅ No information loss

### Cons
- ❌ Major API changes throughout codebase
- ❌ `getTypeLocation(id)` becomes ambiguous - which location?
- ❌ Pretty-printer needs context to know which instance to use
- ❌ Every lookup requires location disambiguation
- ❌ Breaks assumption that IDs are globally unique identifiers
- ❌ Complex implementation with widespread impact

### Analysis
This acknowledges that the same type can exist in multiple places, but then forces every consumer to deal with that ambiguity. The pretty-printer would need to know "which ID in which location" to work correctly, which defeats the purpose of having IDs in the first place.

**Verdict**: Technically sound but impractical. Too much complexity propagated throughout the system.

---

## Option 3: Hierarchical Naming (Location IS the ID)

**Eliminate Hash IDs entirely. Use full qualified names as identifiers.**

### Implementation
```fsharp
// Instead of:
type PackageType = { id: Hash; ... }

// Use:
type PackageType = { location: PackageLocation; ... }
// Where PackageLocation = { owner: string; modules: List<string>; name: string }
```

### Pros
- ✅ No collision possible - names are inherently unique by location
- ✅ Self-documenting - ID tells you where the type lives
- ✅ No Hash computation needed
- ✅ Natural alignment with how packages are referenced
- ✅ Simpler mental model - name = identity

### Cons
- ❌ Massive refactoring - IDs are everywhere in the codebase
- ❌ Names are mutable (can be renamed) - need migration story
- ❌ Can't detect structural duplicates (same type, different names)
- ❌ Database tables need redesign (multi-column keys)
- ❌ No content verification - can't detect if type definition changed
- ❌ Larger IDs (strings vs 32-byte hashes)

### Analysis
This is the most radical option. It treats package items like a hierarchical filesystem where the path IS the identity. This works well for many systems (URLs, file paths, DNS), but we lose:
1. Content verification (Hash proves the definition matches)
2. Ability to detect duplicate definitions
3. Compact representation

**Verdict**: Clean conceptually but requires major architectural changes. Too disruptive for the benefits.

---

## Option 4: Scoped Content Addressing (Hash + Scope)

**Hash content but scope the ID to a specific context (module/package).**

### Implementation
```fsharp
// Hash includes enough context to be unique within its scope
Hash(modulePath, typeName, typeDefinition)

// But also track:
type PackageType = {
  id: Hash                    // Unique within this module tree
  scope: List<string>         // Module path this type is defined in
  ...
}
```

### Pros
- ✅ IDs are unique within their scope
- ✅ Content-based within scope (moving within module keeps ID)
- ✅ Can still detect duplicates at same scope level
- ✅ Less radical than full location-aware hashing
- ✅ Supports "local content addressing"

### Cons
- ❌ Still breaks pure content addressing
- ❌ Requires scope-aware lookup logic
- ❌ Moving between modules still changes ID
- ❌ Ambiguous what "scope" means for cross-module references
- ❌ More complex than either pure content or pure location addressing

### Analysis
This is a middle ground that tries to get benefits of both approaches but inherits downsides of both. It's "content addressed within a namespace" which is conceptually muddier than either extreme.

**Verdict**: Compromise solution that doesn't fully satisfy either goal.

---

## Option 5: Single Definition Rule (Enforce Uniqueness)

**Disallow identically-named types with identical definitions in nested modules.**

### Implementation
Add validation during parsing:
```fsharp
// When parsing a type definition:
if typeWithSameNameExists(modulePath) &&
   typeWithSameDefinitionExists(parentModulePath) then
  error "Type 'ID' with identical definition already exists in parent module"
```

### Pros
- ✅ No code changes to Hash computation needed
- ✅ Forces explicit design decisions
- ✅ Encourages better naming (no shadow types)
- ✅ Aligns with many languages (no shadowing same-structure types)
- ✅ Makes content addressing work correctly

### Cons
- ❌ Breaking change for existing code
- ❌ Restricts developer freedom
- ❌ May force awkward workarounds (ID1, ID2, ID3)
- ❌ Not clear if this is semantically wrong in Darklang
- ❌ Doesn't match intuition (different scopes should allow same names)

### Analysis
This is a language design constraint that makes the Hash collision impossible by construction. Many languages don't allow type shadowing with identical structure. But this feels like working around the problem rather than solving it.

**Verdict**: Could work but feels like an artificial restriction driven by implementation concerns.

---

## Option 6: Bidirectional Mapping with Disambiguation

**Keep content-based Hashes but track all locations, require disambiguation context.**

### Implementation
```fsharp
type PackageManager = {
  // One-to-many: Hash can map to multiple locations
  getTypeLocations: Hash -> List<Location>

  // One-to-one: Location maps to exactly one Hash
  getTypeId: Location -> Option<Hash>

  // Disambiguated access requires both
  getTypeAtLocation: (Hash * Location) -> Option<PackageType>
}
```

### Pros
- ✅ Preserves content addressing
- ✅ Acknowledges multiple definitions can exist
- ✅ Lookup by location is unambiguous
- ✅ Can detect identical types across locations

### Cons
- ❌ Pretty-printer needs location context to disambiguate
- ❌ Complex API - sometimes use Hash, sometimes (Hash, Location)
- ❌ Parser must ensure every type reference includes location
- ❌ Error-prone - easy to forget to pass location
- ❌ Unclear which API to use in which situation

### Analysis
This makes the ambiguity explicit in the API, forcing callers to handle it. But it's not clear HOW callers should handle it. When pretty-printing a type reference, we have the Hash - but which location instance do we mean? We'd need additional context tracking throughout the system.

**Verdict**: Technically complete but pushes complexity onto every caller.

---

## Option 7: Content Hash + Stable Path IDs (Hybrid)

**Use content Hashes for verification, but stable path-based IDs for identity.**

### Implementation
```fsharp
type PackageType = {
  id: StableId           // Deterministic from full path: Hash(owner, modules, name)
  contentHash: Hash      // Hash of actual definition
  ...
}

// On lookup:
let verifyType (pm: PackageManager) (id: StableId) (expectedContent: TypeDef) =
  match pm.getType id with
  | Some typ when typ.contentHash = Hash(expectedContent) -> Ok typ
  | Some typ -> Error "Content mismatch - type definition changed"
  | None -> Error "Type not found"
```

### Pros
- ✅ IDs are stable and unique (based on path)
- ✅ Content verification still possible (via contentHash)
- ✅ Can detect when definitions change
- ✅ Best of both worlds: stable identity + content integrity
- ✅ Pretty-printing works (1:1 ID to location mapping)
- ✅ Can migrate types by changing location (new ID, same content)

### Cons
- ❌ Two IDs per type (identity + content) - more complex
- ❌ Need to decide when to check contentHash vs just use ID
- ❌ Requires refactoring to track both IDs
- ❌ Moving a type changes its ID (but preserves contentHash)
- ❌ Larger storage (two hashes per type)

### Analysis
This separates two concerns:
1. **Identity** (where is this type?) → stable path-based ID
2. **Integrity** (is this the right definition?) → content Hash

It acknowledges that location IS part of identity in a package system, while still getting content verification benefits. This is how many systems work (git commits have IDs based on content, but refs point to specific commits).

**Verdict**: Most pragmatic. Adds some complexity but solves both problems cleanly.

---

## Recommendation

After analyzing all options, I recommend **Option 7: Content Hash + Stable Path IDs (Hybrid)**.

### Why This is Best

1. **Fixes the immediate bug**: Unique IDs per location eliminates collisions
2. **Preserves content verification**: contentHash still validates definitions match
3. **Clear semantics**: Location determines identity, content determines validity
4. **Practical tradeoffs**: Slight complexity increase for major functionality gain
5. **Migration path**: Can be implemented incrementally

### Implementation Plan

#### Phase 1: Add contentHash field
```fsharp
type PackageType = {
  id: Hash                  // Still Hash, but will compute differently
  contentHash: Hash         // NEW: Hash of just the content
  declaration: TypeDeclaration
  ...
}
```

#### Phase 2: Change ID computation
```fsharp
// In writtenTypesToProgramTypes.dark:
let computeStableId (location: PackageLocation) : String =
  Builtin.languageToolsHashPackageLocation location

let computeContentHash (typ: PackageType) : String =
  Builtin.languageToolsHashPackageType { typ with id = "" }
```

#### Phase 3: Update validation
```fsharp
// When loading/verifying types:
match pm.getType id with
| Some typ ->
  let expectedContentHash = computeContentHash expectedType
  if typ.contentHash <> expectedContentHash then
    error "Type definition mismatch"
```

### Alternative: Simpler Option 1

If the complexity of tracking two hashes is too much, **Option 1 (Location-Aware Hashing)** is the simplest fix:
- Change Hash computation to include module path
- Gives up on pure content addressing, but we weren't getting much value from it anyway
- Current code mostly already works this way (types are referenced by location)

The key insight: **In a hierarchical package system, location IS part of identity**. Two types with the same name and definition in different modules are conceptually different types (they can evolve independently, be documented differently, have different semantics).

### What We Learned

The Hash collision revealed a deeper question: What makes a type "the same" vs "different"?
- Same content → Can share implementation
- Same location → Can substitute in references
- Same content AND location → Actually the same type

We tried to use Hash for both identity and integrity, but they're separate concerns that need separate mechanisms.

---

## Open Questions

1. **Should identical types in different modules share implementations?**
   - If yes: Keep pure content addressing, fix pretty-printer differently
   - If no: Location matters for identity, use location-aware IDs

2. **Do we care about detecting duplicate type definitions?**
   - If yes: Keep contentHash separate from ID
   - If no: Simple location-based IDs are sufficient

3. **How often do types move between modules?**
   - If often: Stable IDs across moves are valuable
   - If rarely: ID change on move is acceptable

4. **Is content verification actually used?**
   - Check if any code validates that Hash matches content
   - If not used: contentHash field may be unnecessary overhead

---

## Test Status

Current: 9,178 / 9,179 tests passing (99.99%)

The failing test (`nested module declaration`) has been annotated with an explanation of the Hash collision issue and references this analysis document.
