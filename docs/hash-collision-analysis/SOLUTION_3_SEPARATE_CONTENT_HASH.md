# Solution 3: Separate Identity Hash and Content Hash

## Core Idea

Use TWO different hashes for TWO different purposes:
1. **Identity Hash** (from location) - Uniquely identifies WHERE something is
2. **Content Hash** (from structure) - Verifies WHAT something is

---

## Data Structure

```fsharp
type PackageType = {
  id: Hash              // Identity: Hash(owner, modules, name) - unique per location
  contentHash: Hash     // Integrity: Hash(declaration) - same for identical structures
  declaration: TypeDeclaration
  ...
}
```

---

## How It Works

### Example: Two Identical Types

```darklang
module MyModule1 =
  type ID = Int64
  // id = Hash("MyModule1", "ID") = abc123
  // contentHash = Hash(Int64) = xyz789

module MyModule2 =
  type ID = Int64
  // id = Hash("MyModule2", "ID") = def456
  // contentHash = Hash(Int64) = xyz789  // SAME!
```

**Key Insight:**
- Different `id` → Can store separately in PM
- Same `contentHash` → Can detect they're structurally identical

---

## How It Solves Problems

### Problem 1: Pretty Printing ✅
- Each location has unique `id`
- No collisions: `typeIdToLoc[abc123] = MyModule1.ID`, `typeIdToLoc[def456] = MyModule2.ID`
- Pretty printer works: lookup by `id` returns unambiguous location

### Problem 2: Runtime Errors ⚠️ Partial
- Reference stores `id` (which is location-based)
- Can reverse lookup: `typeIdToLoc[abc123] → MyModule1.ID`
- Better than current, but Hash still opaque
- Not as good as Solution 1 (which embeds location)

### Problem 3: Type References ⚠️ Partial
- Type reference contains `id`
- Must reverse lookup to get name
- Same issue as Solution 2

### Problem 4: Detecting Duplicates ✅
- Same `contentHash` = structurally identical!
- Can query: "Show all types with contentHash xyz789"
- Answer: [MyModule1.ID, MyModule2.ID]
- Can warn: "Duplicate structure detected"

### Problem 5: Stale References ❌ Fails
- If type moves, `id` changes (it's location-based)
- Old references with old `id` break
- But: `contentHash` remains same
- Could potentially find by contentHash? Complex

### Problem 6: Multiple Identical Types ✅
- Treated as separate (different `id`)
- But can detect identity (`contentHash` matches)
- Can choose to share implementations (same content)

---

## Pros and Cons

### Pros ✅
- Fixes collision (unique `id` per location)
- Preserves content verification (`contentHash`)
- Can detect duplicates (compare `contentHash`)
- Can potentially share implementations (same `contentHash`)
- Clear separation of concerns (identity vs integrity)

### Cons ❌
- Two hashes per type (more storage)
- Moving types breaks references (`id` changes)
- Still need reverse lookup for error messages
- More complex (two different hash mechanisms)
- `contentHash` might be underutilized
- Which hash to use when? Potential confusion

---

## When Each Hash Is Used

| Operation | Uses `id`? | Uses `contentHash`? |
|-----------|------------|---------------------|
| Store in PM | ✅ Yes | ❌ No |
| Lookup by location | ✅ Yes | ❌ No |
| Reverse lookup location | ✅ Yes | ❌ No |
| Detect duplicates | ❌ No | ✅ Yes |
| Verify content matches | ❌ No | ✅ Yes |
| Share implementations | ❌ No | ✅ Yes |
| Pretty printing | ✅ Yes | ❌ No |
| Error messages | ✅ Yes | ❌ No |

---

## Implementation

### Step 1: Add contentHash field

```fsharp
// In ProgramTypes.fs
module PackageType =
  type PackageType = {
    id: Hash              // NEW: Location-based
    contentHash: Hash     // NEW: Content-based
    declaration: TypeDeclaration
    description: string
    deprecated: Deprecation
  }
```

### Step 2: Compute both hashes

```fsharp
// In ContentHash.fs
module PackageType =
  // Identity hash (from location)
  let identityHash (loc: PT.PackageLocation) : Hash =
    hashWithWriter Serializers.PT.Common.PackageLocation.write loc

  // Content hash (from structure)
  let contentHash (typ: PT.PackageType.PackageType) : Hash =
    hashWithWriter Serializers.PT.PackageType.write { typ with id = Hash.zero; contentHash = Hash.zero }

  // Combined creation
  let create (loc: PT.PackageLocation) (decl: PT.TypeDeclaration) : PT.PackageType.PackageType =
    let typ = { id = Hash.zero; contentHash = Hash.zero; declaration = decl; ... }
    let cHash = contentHash typ
    let iHash = identityHash loc
    { typ with id = iHash; contentHash = cHash }
```

### Step 3: Use identity hash for lookups

```fsharp
// PackageManager uses `id` (identity hash) for all lookups
type PackageManager = {
  findType: PackageLocation -> Option<Hash>        // Returns identity hash
  getType: Hash -> Option<PackageType>             // Takes identity hash
  getTypeLocation: Hash -> Option<PackageLocation> // Takes identity hash
}
```

### Step 4: Add duplicate detection

```fsharp
// New PM function
type PackageManager = {
  ...
  findTypesByContent: Hash -> List<PackageLocation>
}

// Usage:
let detectDuplicates (pm: PackageManager) (typ: PackageType) =
  let locations = pm.findTypesByContent typ.contentHash
  if List.length locations > 1 then
    warn $"Type with content hash {typ.contentHash} exists in {locations}"
```

---

## Comparison to Other Solutions

| Aspect | Solution 1 (PackageRef) | Solution 2 (Location in Hash) | Solution 3 (Two Hashes) |
|--------|------------------------|-------------------------------|-------------------------|
| Fixes collision? | ✅ Yes | ✅ Yes | ✅ Yes |
| Better errors? | ✅ Yes (location in ref) | ⚠️ Needs lookup | ⚠️ Needs lookup |
| Detect duplicates? | ✅ Yes (same Hash) | ❌ No | ✅ Yes (contentHash) |
| Handle moves? | ✅ Graceful | ❌ Breaks | ❌ Breaks |
| Content-based? | ✅ Yes | ❌ No | ⚠️ Half (contentHash yes, id no) |
| Storage overhead? | ⚠️ Hash + Option<Location> | ✅ Just Hash | ❌ Two Hashes |
| Complexity? | ⚠️ Data structure change | ✅ Simple | ⚠️ Two hash systems |

---

## Real-World Usage

### When `contentHash` is useful:
```fsharp
// 1. Duplicate detection
let duplicates = pm.findTypesByContent contentHash
if List.length duplicates > 1 then
  warn "Duplicate structures found"

// 2. Content verification
match pm.getType id with
| Some typ when typ.contentHash = expectedContentHash -> Ok()
| Some typ -> Error "Type definition changed"
| None -> Error "Type not found"

// 3. Implementation sharing (hypothetical)
match implementationCache.get typ.contentHash with
| Some impl -> impl  // Reuse implementation
| None ->
  let impl = compile typ
  implementationCache.add typ.contentHash impl
  impl
```

### When `contentHash` is NOT useful:
```fsharp
// Pretty printing - uses `id`, not `contentHash`
let prettyPrint (ref: TypeReference) =
  match pm.getTypeLocation ref.id with  // Using `id`
  | Some loc -> formatLocation loc
  | None -> ref.id.toString()

// Error messages - uses `id`, not `contentHash`
let formatError (fnId: Hash) =
  match pm.getFnLocation fnId with  // Using `id`
  | Some loc -> $"In function {formatLocation loc}"
  | None -> $"In function {fnId}"
```

**Observation:** Most operations use `id`, not `contentHash`.
The `contentHash` is mainly useful for:
- Duplicate detection
- Content verification
- Potential implementation sharing

**Question:** Is this worth the complexity?

---

## Verdict

**This is the "best of both worlds but complex" solution.**

It provides:
- ✅ Unique IDs (via identity hash)
- ✅ Content verification (via content hash)
- ✅ Duplicate detection (compare content hashes)

But:
- ❌ Two hashes per type (storage overhead)
- ❌ Moving types still breaks (identity hash changes)
- ❌ Doesn't improve error messages vs Solution 2
- ❌ Complex: which hash for what?

**Recommendation:**
- If you value duplicate detection and content verification: Use this
- If you value error messages and graceful moves: Use Solution 1
- If you want simplicity: Use Solution 2

**Best approach:** Start with Solution 1 (PackageRef with location hints), which handles moves gracefully and improves errors. If duplicate detection becomes important later, can add `contentHash` as an additional field.
