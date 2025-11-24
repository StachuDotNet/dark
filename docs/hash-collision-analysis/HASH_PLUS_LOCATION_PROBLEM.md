# Hash + Location: The Problem Space

## Core Insight

**Hash collisions are not bugs - they're features.**

When two types have the same Hash, it means they're structurally identical. This is useful information! The problem is that we're using Hash as the ONLY identifier, when we actually need multiple pieces of information for different purposes.

---

## Problems We're Trying to Solve

### Problem 1: Pretty Printing Nested Modules (Current Failure)

**Scenario:**
```darklang
module MyModule1 =
  type ID = Int64

  module MyModule2 =
    type ID = Int64

    module MyModule3 =
      type ID = Int64
```

**What Happens:**
- All three types hash to same value: `abc123`
- Parser creates: `SetTypeName(abc123, MyModule1)`, `SetTypeName(abc123, MyModule2)`, `SetTypeName(abc123, MyModule3)`
- **InMemory PM**: First one wins! `typeIdToLoc.TryAdd(abc123, MyModule1)` succeeds, subsequent `TryAdd` calls fail silently
- **SQL PM**: Each SetTypeName **deprecates** the previous location (designed for moves, not coexistence)
- Pretty printer asks: "Where is type abc123?" → Gets wrong/stale location

**Current Implementation Issues:**
- `InMemory.fs` line 36: `typeIdToLoc.TryAdd(id, loc) |> ignore<bool>` - only first location stored
- `OpPlayback.fs` line 143-158: `UPDATE locations SET deprecated_at = datetime('now')` - deprecates old location on each SetTypeName
- Neither implementation supports **multiple active locations** for same Hash

**What We Need:**
- Parser: Track BOTH Hash and Location when creating types
- PM: Store multiple (Hash, Location) pairs without overwriting
- Pretty Printer: Given (Hash, Location context), determine the right name

---

### Problem 2: Runtime Error Messages

**Scenario:**
```darklang
// Runtime error in some function
Stdlib.String.append "hello" 123  // Wrong type!
```

**What Happens:**
- RTE says: "Function `e7b0b593...` parameter 'suffix' expects String but got Int64"
- User sees: gibberish hash, no idea which function failed

**What We Need:**
- Function reference carries Hash (structure) + Location hint (name)
- Error formatter can say: "In function `Stdlib.String.append`" (user-friendly!)
- If location is stale/missing, fall back to Hash or reverse-lookup

---

### Problem 3: Type References in Code

**Scenario:**
```darklang
type Person = { id: <some-type-ref> }
```

**What Happens:**
- Type reference stores just a Hash
- When pretty-printing: need to figure out what name to use
  - `ID` if it's in current module
  - `MyModule.ID` if it's in sibling
  - `Owner.Package.Module.ID` if external

**What We Need:**
- Type reference carries Hash + Location (where it was resolved)
- Pretty printer uses Location as hint for name resolution
- Can re-resolve if Location is stale

---

### Problem 4: Detecting Duplicate Definitions

**Scenario:**
```darklang
module A =
  type UserID = Int64

module B =
  type UserID = Int64  // Duplicate! Same structure, different location
```

**What We Need:**
- Detect that both have same Hash (identical structure)
- Can warn user: "Type with identical structure exists in module A"
- Or: allow it (they're in different scopes, might be intentional)

---

### Problem 5: Stale Location References

**Scenario:**
```darklang
// File from yesterday:
let x: MyModule.ID = 123L

// Today, ID was moved:
module NewLocation =
  type ID = Int64  // Moved here
```

**What Happens:**
- Old code references `(hash=abc123, location=MyModule.ID)`
- Location is stale (type no longer there)
- Need to still resolve correctly

**What We Need:**
- Try Location first (fast path)
- If not found, fall back to Hash lookup
- Find current location(s) for that Hash
- Continue working (maybe warn about stale reference)

---

### Problem 6: Multiple Identical Types (Intentional)

**Scenario:**
```darklang
module UserService =
  type ID = Int64  // User IDs

module ProductService =
  type ID = Int64  // Product IDs
```

**Design Question:**
Are these the same type or different types?
- **Same Hash** = structurally identical
- **Different Locations** = semantically distinct?

**What We Need:**
- System that can handle both interpretations
- For pretty-printing: treat as separate (show location)
- For optimization: could share implementation (same structure)
- For type checking: depends on language semantics

---

## Information We Have vs. Information We Need

### What We Have
- **Hash**: Content-based identifier
  - Computed from: type name, type definition
  - Same structure → Same Hash
  - Lightweight (32 bytes)

### What We're Missing
- **Location Context**: Where this thing is/was
  - Tells us: owner, modules, name
  - Helps: pretty-printing, error messages, disambiguation
  - Can be: stale, optional, a hint

### What We Need for Different Operations

| Operation | Needs Hash? | Needs Location? | Notes |
|-----------|-------------|-----------------|-------|
| Content verification | ✅ Yes | ❌ No | Hash proves structure |
| Pretty printing | ✅ Yes | ✅ Yes | Need both to pick right name |
| Error messages | ✅ Yes | ✅ Yes (hint) | Location makes error readable |
| Type lookup by name | ❌ No | ✅ Yes | Name → Location → Hash |
| Type lookup by structure | ✅ Yes | ❌ No | Just need Hash |
| Detecting duplicates | ✅ Yes | ❌ No | Same Hash = duplicate |
| Storing definitions | ✅ Yes | ✅ Yes | Need to map both ways |

---

## Key Realization

**We're trying to use Hash for TWO different purposes:**

1. **Identity** - "Which type is this?"
2. **Integrity** - "Does the structure match?"

These are separate concerns that need separate mechanisms:
- **Hash** → Answers: "What structure?"
- **Location** → Answers: "Where is it?" / "What's it called?"

---

## The Fundamental Trade-off

### Pure Content Addressing (Hash only)
- ✅ Structural equality is explicit
- ✅ Duplicates are obvious (same Hash)
- ✅ Compact references
- ❌ Can't distinguish identical types in different places
- ❌ Poor error messages (just hashes)
- ❌ Pretty printing is ambiguous

### Pure Location Addressing (Name only)
- ✅ Clear identity (location = name)
- ✅ Good error messages
- ✅ Pretty printing is trivial
- ❌ Can't detect structural duplicates
- ❌ Can't verify content matches
- ❌ Refactoring changes identity

### Hybrid (Hash + Location)
- ✅ Can verify content (Hash)
- ✅ Can show names (Location)
- ✅ Can detect duplicates (same Hash, different Location)
- ✅ Graceful degradation (if Location stale, use Hash)
- ❌ More complex (need both pieces)
- ❌ Larger references
- ❌ More bookkeeping

---

## Current Architecture Constraints

### InMemory PackageManager (`InMemory.fs`)

**Single-valued reverse mapping:**
```fsharp
// Line 26-28: One location per Hash
let typeIdToLoc = ConcurrentDictionary<Hash, PT.PackageLocation>()
let valueIdToLoc = ConcurrentDictionary<Hash, PT.PackageLocation>()
let fnIdToLoc = ConcurrentDictionary<Hash, PT.PackageLocation>()

// Line 34-36: TryAdd only succeeds once per Hash
| PT.PackageOp.SetTypeName(id, loc) ->
  typeLocMap.TryAdd(loc, id) |> ignore<bool>
  typeIdToLoc.TryAdd(id, loc) |> ignore<bool>  // ❌ Fails silently for duplicate Hashes
```

**Problem**: When multiple types have same Hash, only the **first** `SetTypeName` creates a reverse mapping. Subsequent ones fail silently.

**Consequence**: In tests (which use InMemory PM), Hash collisions cause wrong locations to be returned.

---

### SQL PackageManager (`OpPlayback.fs`)

**Deprecation-based approach:**
```fsharp
// Line 143-158: Deprecates old location when setting new one
let private applySetName (itemId: Hash) (location: PackageLocation) =
  // First, deprecate any existing location for this item
  UPDATE locations
  SET deprecated_at = datetime('now')
  WHERE item_id = @item_id AND deprecated_at IS NULL

  // Then insert new location
  INSERT INTO locations (location_id, item_id, owner, modules, name, item_type)
  VALUES (...)
```

**Design assumption**: `SetTypeName` is for **moving** types, not for **coexisting** types with same Hash.

**Consequence**: Multiple types with same Hash will deprecate each other's locations. Only the last one is active.

**Schema**: The `locations` table uses `location_id` as PRIMARY KEY (not `item_id`), so it CAN store multiple rows per `item_id`. But the `deprecated_at` logic prevents multiple active locations.

---

### Why This Design?

The SQL implementation's deprecation logic makes sense for **type moves**:
```darklang
// Day 1: Type at original location
module Original =
  type ID = Int64  // Hash: abc123

// Day 2: Type moved to new location
module NewLocation =
  type ID = Int64  // Hash: abc123 (same!)

// SetTypeName(abc123, NewLocation) should:
// - Deprecate "Original.ID"
// - Activate "NewLocation.ID"
```

**But it breaks for coexisting types:**
```darklang
module A =
  type ID = Int64  // Hash: abc123

module B =
  type ID = Int64  // Hash: abc123 (same!)

// SetTypeName(abc123, A) then SetTypeName(abc123, B):
// - A.ID gets deprecated
// - Only B.ID is active
// ❌ Wrong! Both should coexist.
```

---

### Design Question: Moves vs Coexistence

**Are multiple types with same Hash:**
1. **The same type at different locations** (moves over time)?
2. **Different types that happen to share structure** (coexist simultaneously)?

**Current answer**: The code assumes (1) - moves. But the failing test demonstrates (2) - coexistence.

**Proposed answer**: Both are valid! We need to support:
- **Coexistence**: Multiple active locations per Hash (current module nesting scenario)
- **Moves**: Deprecate old location when type moves (refactoring scenario)

**How to distinguish?**
- Option A: Different ops (`MoveTypeName` vs `SetTypeName`)
- Option B: Always allow coexistence, use separate deprecation ops
- Option C: Track in type metadata (is this the "canonical" location?)

---

## Next Steps

See the following documents for specific solution proposals:
- `SOLUTION_1_PACKAGE_REF.md` - Add Location to type references
- `SOLUTION_2_LOCATION_IN_HASH.md` - Include location in Hash computation
- `SOLUTION_3_SEPARATE_CONTENT_HASH.md` - Two hashes (identity + content)
- `SOLUTION_4_DEBUG_METADATA.md` - Debug metadata system
- `SOLUTION_COMPARISON.md` - Side-by-side comparison of approaches
