# Feedback Responses & Design Refinements

## 1. NameResolution + Location Redundancy

**Problem**: If we add `location: Option<PackageLocation>` to TCustomType alongside `NameResolution<FQTypeName>`, the unresolved case is redundant.

```fsharp
// Proposed (has redundancy):
| TCustomType of
    NameResolution<FQTypeName.Package> *  // Resolved(hash) or Unresolved(location)
    location: Option<PackageLocation> *    // Duplicates Unresolved case!
    typeArgs: List<TypeReference>
```

**Solution Options**:

**A) Just use location, resolve later:**
```fsharp
| TCustomType of
    hashOrLocation: Result<Hash, PackageLocation> *
    typeArgs: List<TypeReference>
```
- Left(hash) = resolved
- Right(location) = needs resolution

**B) Always store both when resolved:**
```fsharp
| TCustomType of
    hash: Hash *
    originalLocation: Option<PackageLocation> *  // For pretty printing
    typeArgs: List<TypeReference>
```
- Always resolved by the time it's in PT
- Location is just a hint for display

**C) Use NameResolution but populate location on resolve:**
```fsharp
type TypeRef =
  { resolution: NameResolution<Hash>
    location: Option<PackageLocation> }  // Populated even when resolved

| TCustomType of TypeRef * typeArgs: List<TypeReference>
```

**Recommendation**: **Option B** - By the time we're in PT (after multi-phase parsing), everything should be resolved to hashes. Location is just metadata for pretty printing.

---

## 2. Rename 'id' to 'hash'

**Agreed!** Much clearer:

```fsharp
type PackageType =
  { declaration : TypeDeclaration.T
    hash : Hash  // NOT 'id' - this is the content hash
    location : Option<PackageLocation>  // Where it lives
    // Metadata (NOT hashed):
    description : string
    deprecated : Deprecation }

type PackageFn =
  { declaration : FnDeclaration.T
    hash : Hash
    location : Option<PackageLocation>
    description : string
    deprecated : Deprecation }

type PackageValue =
  { body : Expr
    hash : Hash
    location : Option<PackageLocation>
    description : string
    deprecated : Deprecation }
```

This makes it obvious: `hash` is the content-addressed identity, `location` is where it lives.

---

## 3. Separate ExprHash, TypeHash, FnHash, ValueHash?

**Question**: Would typed hashes help?

```fsharp
type TypeHash = TypeHash of Hash
type FnHash = FnHash of Hash
type ValueHash = ValueHash of Hash
type ExprHash = ExprHash of Hash  // Maybe for expression memoization?
```

**Pros**:
- Type safety (can't mix type hashes with fn hashes)
- Self-documenting code
- Compiler catches mistakes

**Cons**:
- More ceremony (wrapping/unwrapping)
- Hashes might need to be polymorphic in some places
- Not clear ExprHash is needed (exprs aren't top-level items)

**Recommendation**: **Do it for top-level items only**:
```fsharp
type TypeHash = TypeHash of Hash
type FnHash = FnHash of Hash
type ValueHash = ValueHash of Hash

// Not ExprHash - exprs are sub-structures
```

Update FQTypeName/FQFnName/FQValueName:
```fsharp
module FQTypeName =
  type Package = TypeHash  // Instead of Hash

module FQFnName =
  type Package = FnHash

module FQValueName =
  type Package = ValueHash
```

---

## 4. How Can SCC Members Have Same Hash?

**Your confusion is valid!** I was wrong about this.

**Problem**: If `type A = B` and `type B = A` both get hash `xyz`, how do we distinguish them?

**Reality**: They **shouldn't** have the same hash. The SCC is about **detecting** mutual recursion, not **collapsing** identity.

**Correct Approach**:

```fsharp
// Step 1: Detect SCC
let sccs = detectSCCs types  // [[A, B]]

// Step 2: Hash the SCC GROUP to get a "cycle ID"
let sccHash = hashSCC [A, B]  // Hash of the group

// Step 3: Hash each member RELATIVE to the cycle
let hashA = hashWithCycleContext(A, sccHash)  // Unique to A
let hashB = hashWithCycleContext(B, sccHash)  // Unique to B
```

Each member hashes:
- Its own structure (name, definition)
- The SCC group hash (to establish cycle membership)

**Result**: A and B have **different** hashes, but both reference the **cycle hash** in their structure.

**Alternative**: Use placeholder/self-reference:
```fsharp
// When hashing A which references B (in same SCC):
// Replace B's hash with a placeholder like "self.B"
let hashA = hash {
  name = "A"
  definition = Alias(TypeRef("self.B"))  // Placeholder for cycle member
}
```

This ensures deterministic hashing without infinite loops.

**Recommendation**: Use **cycle-relative hashing** or **placeholder approach**. Each type still gets unique hash.

---

## 5. Avoiding Hash.zero and 3-Phase Parsing

**Your point**: Why use `Hash.zero` at all? Why not focus on declarations and handle hashing directly?

**Current 3-phase approach**:
1. Parse → PT with Hash.zero
2. Compute hashes
3. Rewrite Hash.zero to actual hashes

**Your suggestion**: Parse → Declaration → Hash immediately, emit ops.

**Better Approach**:

```fsharp
// Input: List of WT items (types, fns, values)
// Output: List of PackageOps (AddType, AddFn, AddValue, SetTypeName, etc.)

let wtToOps (items: List<WT.Item>) : List<PT.PackageOp> =
  // Step 1: Build dependency graph
  let depGraph = buildDepGraph items

  // Step 2: Detect SCCs
  let sccs = detectSCCs depGraph

  // Step 3: Hash each SCC group
  let hashMap = sccs |> List.collect (fun scc ->
    scc |> List.map (fun item ->
      let hash = hashInContext item scc
      (item.location, hash)
    )
  ) |> Map.ofList

  // Step 4: Emit ops
  items |> List.collect (fun item ->
    let hash = hashMap[item.location]
    let declaration = convertDeclaration item hashMap  // Refs resolved to hashes
    [ PT.PackageOp.AddType { declaration = declaration; hash = hash; ... }
      PT.PackageOp.SetTypeName(hash, item.location) ]
  )
```

**Key insight**: Go straight from WT → ops. PT types are just what the ops contain, not an intermediate representation.

**Result**: No Hash.zero, no rewriting phase. Just: parse → analyze → hash → emit ops.

---

## 6. Using Location for Pretty Printing

**Question**: How does `location: Option<PackageLocation>` in PT help identify which name to use?

**Answer**: It doesn't directly - PM needs to do the work.

**Scenario**:
```darklang
module A =
  type ID = Int64  // Hash: abc123, Location: A.ID

module B =
  type ID = Int64  // Hash: abc123, Location: B.ID
```

**In code**:
```darklang
let x: ??? = 42L  // Type hash is abc123, but which name to print?
```

**Pretty printer logic**:
```fsharp
let prettyPrintType (hash: TypeHash) (currentModule: List<string>) : string =
  // Ask PM for all locations with this hash
  let locations = pm.getTypeLocations(None, hash)

  match locations with
  | [] -> hash.toString()  // Can't find it
  | [singleLoc] -> formatLocation singleLoc currentModule
  | multipleLocs ->
    // Pick "best" one based on context
    let best = chooseBestLocation multipleLocs currentModule
    formatLocation best currentModule

let chooseBestLocation (locs: List<PackageLocation>) (currentModule: List<string>) =
  locs
  |> List.sortBy (fun loc ->
    // Prefer same module, then parent module, then shortest path
    if loc.modules = currentModule then 0
    else if startsWithPath loc.modules currentModule then 1
    else 2
  )
  |> List.head
```

**If type reference has location hint**:
```fsharp
// In PT: TCustomType with both hash and location
let prettyPrintTypeRef (hash: TypeHash) (locationHint: Option<PackageLocation>) =
  match locationHint with
  | Some hint ->
    // Verify hint is still valid
    match pm.findType(None, hint) with
    | Some h when h = hash -> formatLocation hint currentModule  // Valid!
    | _ -> prettyPrintType hash currentModule  // Stale hint, fall back
  | None ->
    prettyPrintType hash currentModule
```

**Changes needed**:
- PM: Add `getTypeLocations(hash) -> List<PackageLocation>`
- PT: Store `location: Option<PackageLocation>` in types
- Pretty printer: Query PM, use location hint when available
- WT2PT: Populate location during parsing

---

## 7. Handling Name Overrides

**Point**: When you `SetTypeName(newHash, location)`, that location should stop referring to the old hash.

**Current SQL approach**:
```sql
-- Old: Deprecates previous location entry
UPDATE locations SET deprecated_at = NOW()
WHERE location = @location AND deprecated_at IS NULL
```

**Problem**: This deprecates ALL entries for that location, even if they're different hashes (coexistence).

**Correct approach**:
```sql
-- Deprecate only if location PREVIOUSLY pointed to different hash
UPDATE locations SET deprecated_at = NOW()
WHERE owner = @owner
  AND modules = @modules
  AND name = @name
  AND item_id != @new_hash  -- Only deprecate if pointing to DIFFERENT hash
  AND deprecated_at IS NULL
```

**This handles**:
- **Move**: `A.ID` pointed to hash `xyz`, now points to hash `abc` → deprecate `xyz` at `A.ID`
- **Coexistence**: `A.ID` points to hash `abc`, `B.ID` also points to hash `abc` → no deprecation

---

## 8. Rename PackageIDs → PackageRefs

**Agreed!** Better name.

```fsharp
// OLD:
module PackageIDs =
  module Type =
    let option : Hash = ...

// NEW:
module PackageRefs =
  module Type =
    let option : Lazy<TypeHash> = ...
```

---

## 9. Making PackageRefs Less Noisy

**Current usage**:
```fsharp
let optionType = FQTypeName.fqPackage PackageIDs.Type.Stdlib.option
let resultType = FQTypeName.fqPackage PackageIDs.Type.Stdlib.result

when id = PackageIDs.Type.Stdlib.option ->
```

**Problems**:
- Verbose
- `.Value` ceremony if using Lazy
- `FQTypeName.fqPackage` boilerplate

**Solution A: Helper Functions**
```fsharp
module PackageRefs =
  module Type =
    let option : Lazy<TypeHash> = ...

    // Helper
    let optionRef() : FQTypeName.FQTypeName =
      FQTypeName.Package option.Value

// Usage:
let optionType = PackageRefs.Type.optionRef()
```

**Solution B: Direct FQTypeName**
```fsharp
module PackageRefs =
  module Type =
    let option : Lazy<FQTypeName.FQTypeName> =
      lazy (FQTypeName.Package (resolveHash "Darklang" ["Stdlib"; "Option"] "Option"))

// Usage:
let optionType = PackageRefs.Type.option.Value
```

**Solution C: Active Patterns**
```fsharp
let (|OptionType|_|) hash =
  if hash = PackageRefs.Type.option.Value then Some() else None

match typeRef with
| OptionType -> // Handle Option
```

**Recommendation**: **Solution B** + explicit initialization:
```fsharp
module PackageRefs =
  module Type =
    let mutable private _option : Option<TypeHash> = None
    let option : Lazy<FQTypeName.FQTypeName> =
      lazy (FQTypeName.Package (_option |> Option.get))

    let register (owner: string) (modules: List<string>) (name: string) (hash: TypeHash) =
      _option <- Some hash  // Populated during parsing

// Usage is cleaner:
let optionType = PackageRefs.Type.option.Value
```

---

## 10. NameResolution in RT is Good Actually

**Your point**: We WANT to allow unresolved code to run as long as the happy path is resolved.

**Agreement**: Yes! NameResolution in RT is correct.

```fsharp
// RT.TypeReference
| TCustomType of NameResolution<FQTypeName.FQTypeName> * typeArgs: List<TypeReference>
```

If type is `Unresolved`, we raise RTE when we actually try to use it. But we can parse/store/display it fine.

**Changes needed**: Minimal. Maybe:
- Ensure error messages show location when unresolved
- Keep NameResolution in RT
- Remove metadata provider stuff (over-engineered)

---

## 11. Metadata Separation

**Your point**: Hash shouldn't change when description/deprecation changes.

**Agreement**: Absolutely right!

**Current (wrong)**:
```fsharp
type PackageType =
  { declaration : TypeDeclaration.T
    hash : Hash  // Computed from EVERYTHING below
    description : string
    deprecated : Deprecation }

// Hash includes description → description change = new hash = breaks references!
```

**Correct approach**:

```fsharp
// What gets hashed:
type TypeDeclaration.T =
  { typeParams : List<string>
    definition : Definition }

let hashType (decl: TypeDeclaration.T) : TypeHash =
  // Hash ONLY the declaration
  ContentHash.hash decl

// What PackageType contains:
type PackageType =
  { declaration : TypeDeclaration.T
    hash : TypeHash  // Computed from declaration only
    location : Option<PackageLocation> }

// Metadata stored separately (not in PackageType):
// In DB: package_type_metadata table
// - hash (foreign key)
// - description
// - deprecated
// - examples
```

**New ops**:
```fsharp
type PackageOp =
  | AddType of TypeDeclaration.T  // Just declaration, hash computed
  | SetTypeName of TypeHash * PackageLocation
  | SetTypeDescription of TypeHash * string  // NEW
  | SetTypeDeprecation of TypeHash * Deprecation  // NEW
```

**Benefits**:
- Description changes don't affect hash
- Smaller serialization (PT doesn't need metadata)
- Metadata can be per-location or per-hash (TBD)

**Question**: Should metadata be per-hash or per-location?
- Per-hash: "This implementation is deprecated"
- Per-location: "The name Foo.Bar is deprecated, use Foo.Baz instead"

**Answer**: Probably per-location? A type might be fine, but a particular name for it is deprecated.

---

## Summary of Refinements

1. **NameResolution + Location**: Use Option B - always resolve in PT, location is just hint
2. **'id' → 'hash'**: Rename everywhere ✓
3. **Typed hashes**: Add TypeHash/FnHash/ValueHash wrappers ✓
4. **SCC hashing**: Each member gets unique hash (using cycle-relative approach)
5. **No Hash.zero**: Go straight from WT → ops, hash in context
6. **Pretty printing**: PM provides `getTypeLocations(hash)`, pretty printer chooses best
7. **Name overrides**: Only deprecate if location points to DIFFERENT hash
8. **PackageIDs → PackageRefs**: Rename ✓
9. **Less noisy refs**: Use Lazy<FQTypeName> directly
10. **NameResolution in RT**: Keep it! Unresolved is fine until execution
11. **Metadata separation**: Hash only declaration, store description/deprecation separately

These refinements make the design cleaner and more practical.
