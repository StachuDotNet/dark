# Solution 2: Include Location in Hash Computation

## Core Idea

Make Hash computation location-aware: Hash = SHA256(owner, modules, name, content).

This ensures each type at each location gets a unique Hash.

---

## How It Works

### Before (Current - Collision)
```fsharp
// MyModule1.ID
Hash("ID", Int64) → abc123

// MyModule2.ID
Hash("ID", Int64) → abc123  // COLLISION!
```

### After (Location-Aware - Unique)
```fsharp
// MyModule1.ID
Hash("", ["MyModule1"], "ID", Int64) → abc123

// MyModule2.ID
Hash("", ["MyModule1", "MyModule2"], "ID", Int64) → def456  // Different!
```

---

## Changes Required

### Hash Computation

**writtenTypesToProgramTypes.dark:**
```darklang
// Before:
let packageType =
  ProgramTypes.PackageType.PackageType
    { id = ""  // Computed from content only
      description = ""
      declaration = declaration
      deprecated = ProgramTypes.Deprecation.NotDeprecated }

let hash = Builtin.languageToolsHashPackageType packageType

// After:
let packageType =
  ProgramTypes.PackageType.PackageType
    { id = ""
      description = ""
      declaration = declaration
      deprecated = ProgramTypes.Deprecation.NotDeprecated }

// Include location in hash
let hash =
  Builtin.languageToolsHashPackageTypeAtLocation packageType currentLocation
```

**ContentHash.fs:**
```fsharp
// New function
let hashPackageTypeAtLocation
  (loc: PT.PackageLocation)
  (typ: PT.PackageType.PackageType)
  : Hash =
  use ms = new MemoryStream()
  use bw = new BinaryWriter(ms)

  // Write location
  Serializers.PT.Common.PackageLocation.write bw loc

  // Write type content
  Serializers.PT.PackageType.write bw { typ with id = Hash.zero }

  let bytes = ms.ToArray()
  hashBytes bytes
```

---

## How It Solves Problems

### Problem 1: Pretty Printing ✅
- Each type has unique Hash
- No collisions in typeIdToLoc mapping
- `typeIdToLoc[abc123] = MyModule1.ID` (no overwrite)
- `typeIdToLoc[def456] = MyModule2.ID` (separate entry)

### Problem 2: Runtime Errors ⚠️ Partial
- Hash is still opaque (def456 doesn't tell you it's MyModule2.ID)
- Still need reverse lookup: typeIdToLoc[def456] → MyModule2.ID
- Better than collision, but not as good as having location in reference

### Problem 3: Type References ⚠️ Partial
- Hash is unique per location
- But to pretty print, still need reverse lookup
- Can't tell from Hash alone where it came from

### Problem 4: Detecting Duplicates ❌ Fails
- Same structure, different locations → Different Hashes
- Can't easily detect: "These two types are structurally identical"
- Lose ability to find duplicates

### Problem 5: Stale References ❌ Fails
- If type moves, Hash changes!
- Old references with old Hash become invalid
- Breaking change (unlike Solution 1 which degrades gracefully)

### Problem 6: Multiple Identical Types ⚠️ Changed
- Forces them to be different (different Hashes)
- Can't share implementations even if structure identical
- Might be desired? Depends on language semantics

---

## Pros and Cons

### Pros ✅
- Simple fix (just change Hash computation)
- No data structure changes needed
- Fixes pretty printing collision immediately
- Each type guaranteed unique ID
- No need for multi-valued mappings

### Cons ❌
- Moving a type changes its Hash (breaks references)
- Can't detect structural duplicates
- Can't share implementations of identical types
- Defeats purpose of content-based addressing
- Still need reverse lookup for error messages
- Hash becomes location-based, not content-based

---

## Comparison to Solution 1

| Aspect | Solution 1 (PackageRef) | Solution 2 (Location in Hash) |
|--------|------------------------|-------------------------------|
| Fixes collision? | ✅ Yes | ✅ Yes |
| Better errors? | ✅ Yes (location in ref) | ⚠️ Partial (still need lookup) |
| Detect duplicates? | ✅ Yes | ❌ No |
| Handle moves? | ✅ Graceful | ❌ Breaks |
| Content-based? | ✅ Yes (Hash unchanged) | ❌ No (Hash tied to location) |
| Complexity? | ⚠️ Higher (data structure change) | ✅ Lower (just Hash computation) |
| Migration? | ⚠️ Gradual (add PackageRef) | ❌ Breaking (all Hashes change) |

---

## When This Makes Sense

Use this approach if:
1. ✅ Content-based addressing isn't important
2. ✅ Types rarely move between modules
3. ✅ Don't need to detect structural duplicates
4. ✅ Want simplest possible fix
5. ✅ Willing to accept breaking change

Don't use if:
1. ❌ Want graceful handling of moves
2. ❌ Need to detect duplicate definitions
3. ❌ Want meaningful error messages (location in reference)
4. ❌ Value content verification benefits

---

## Implementation

### Step 1: Add location-aware Hash functions

```fsharp
// In ContentHash.fs
module PackageType =
  let hashAtLocation (loc: PT.PackageLocation) (typ: PT.PackageType.PackageType) : Hash =
    use ms = new MemoryStream()
    use bw = new BinaryWriter(ms)
    Serializers.PT.Common.PackageLocation.write bw loc
    Serializers.PT.PackageType.write bw { typ with id = Hash.zero }
    hashBytes (ms.ToArray())
```

### Step 2: Update parser to use location-aware hashing

```darklang
// In writtenTypesToProgramTypes.dark
let typeDefToPackageType
  (location: PackageLocation)
  (declaration: TypeDeclaration)
  : PackageType =
  let typ = PackageType { id = ""; declaration = declaration; ... }
  let hash = Builtin.languageToolsHashPackageTypeAtLocation location typ
  PackageType { typ with id = hash }
```

### Step 3: Update all type creation sites

Everywhere that creates a PackageType must now pass location.

### Step 4: Migration

⚠️ **This is a breaking change**
- All existing Hashes become invalid
- Need to recompute all package Hashes
- Need to update all references in serialized data
- Consider: migration script to update old Hashes

---

## Verdict

**This is the "simple but limited" solution.**

It fixes the immediate collision problem but:
- Gives up on content-based addressing
- Makes refactoring (moving types) a breaking change
- Loses ability to detect duplicates
- Doesn't improve error messages much

**Recommendation:** Use Solution 1 (PackageRef) instead, unless:
- You need the absolute simplest fix
- Content addressing isn't important for your use case
- You're willing to accept the limitations
