# Solution Comparison

Quick reference comparing all three solutions.

---

## One-Line Summary

| Solution | Essence |
|----------|---------|
| **1. PackageRef** | Add optional location to type references |
| **2. Location in Hash** | Include module path when computing Hash |
| **3. Two Hashes** | Separate identity hash (location) from content hash (structure) |

---

## Problem-by-Problem Comparison

### Problem 1: Pretty Printing Nested Modules

| Solution | Works? | How |
|----------|--------|-----|
| **1. PackageRef** | ✅ Yes | Location hint disambiguates which instance |
| **2. Location in Hash** | ✅ Yes | Each location gets unique Hash, no collision |
| **3. Two Hashes** | ✅ Yes | Identity hash unique per location |

**Winner:** All solve it, but differently

---

### Problem 2: Runtime Error Messages

| Solution | Quality | Details |
|----------|---------|---------|
| **1. PackageRef** | ✅ Best | Location embedded in reference → "In function Stdlib.String.append" |
| **2. Location in Hash** | ⚠️ Okay | Must reverse lookup Hash → location |
| **3. Two Hashes** | ⚠️ Okay | Must reverse lookup identity hash → location |

**Winner:** Solution 1 (best error messages)

---

### Problem 3: Detecting Duplicate Definitions

| Solution | Can Detect? | How |
|----------|-------------|-----|
| **1. PackageRef** | ✅ Yes | Same Hash = same structure |
| **2. Location in Hash** | ❌ No | Different locations → different Hashes |
| **3. Two Hashes** | ✅ Yes | Same contentHash = same structure |

**Winner:** Solutions 1 & 3

---

### Problem 4: Handling Stale References (Type Moved)

| Solution | Graceful? | Behavior |
|----------|-----------|----------|
| **1. PackageRef** | ✅ Yes | Hash unchanged, location stale → re-lookup by Hash |
| **2. Location in Hash** | ❌ No | Hash changes → reference breaks |
| **3. Two Hashes** | ❌ No | Identity hash changes → reference breaks |

**Winner:** Solution 1 (only one that handles moves)

---

### Problem 5: Content Verification

| Solution | Can Verify? | How |
|----------|-------------|-----|
| **1. PackageRef** | ✅ Yes | Hash verifies content |
| **2. Location in Hash** | ⚠️ Partial | Hash includes location, not pure content |
| **3. Two Hashes** | ✅ Yes | contentHash verifies content |

**Winner:** Solutions 1 & 3

---

## Implementation Complexity

### Data Structure Changes

| Solution | F# Changes | Darklang Changes | Breaking? |
|----------|------------|------------------|-----------|
| **1. PackageRef** | Medium (add PackageRef type) | Medium (update type refs) | ⚠️ Gradual |
| **2. Location in Hash** | None | Small (hash computation) | ❌ Yes (all Hashes change) |
| **3. Two Hashes** | Medium (add contentHash field) | Medium (track both hashes) | ❌ Yes (schema change) |

---

### PackageManager Changes

| Solution | New Functions? | Storage Changes? |
|----------|----------------|------------------|
| **1. PackageRef** | Yes (multi-valued reverse lookup) | Yes (Hash → List<Location>) |
| **2. Location in Hash** | No | No |
| **3. Two Hashes** | Yes (contentHash lookups) | Yes (track both hashes) |

---

### Parser Changes

| Solution | Complexity | Details |
|----------|------------|---------|
| **1. PackageRef** | High | Must populate location hints everywhere |
| **2. Location in Hash** | Medium | Pass location to hash computation |
| **3. Two Hashes** | Medium | Compute both hashes |

---

## Storage Overhead

| Solution | Size per Reference | Notes |
|----------|-------------------|-------|
| **1. PackageRef** | 32 bytes (Hash) + ~50 bytes (Location) = ~82 bytes | Location is optional, often present |
| **2. Location in Hash** | 32 bytes (Hash) | Compact |
| **3. Two Hashes** | 64 bytes (2 Hashes) | Double the hash storage |

---

## Migration Path

### Solution 1: PackageRef
```
1. Add PackageRef type (Option<Location>)
2. Start with Location = None everywhere
3. Gradually populate locations in parser
4. Update pretty printer to use location hints
5. All old code works (with None)
```
**Difficulty:** ⚠️ Medium (gradual, non-breaking)

### Solution 2: Location in Hash
```
1. Change hash computation
2. Recompute all package hashes
3. Update all references in DB/serialized data
4. Everything breaks, must migrate all at once
```
**Difficulty:** ❌ High (breaking, big-bang)

### Solution 3: Two Hashes
```
1. Add contentHash field
2. Compute both hashes for all types
3. Update all code to use identity hash
4. Add duplicate detection using contentHash
5. Schema change required
```
**Difficulty:** ❌ High (breaking, schema change)

---

## Decision Matrix

### Choose Solution 1 (PackageRef) if:
- ✅ You want best error messages
- ✅ Types will move between modules
- ✅ You want graceful degradation
- ✅ You can do gradual migration
- ✅ You need duplicate detection

### Choose Solution 2 (Location in Hash) if:
- ✅ You want simplest fix
- ✅ Types rarely move
- ✅ Don't need duplicate detection
- ✅ Don't care about content addressing
- ⚠️ Can handle breaking change

### Choose Solution 3 (Two Hashes) if:
- ✅ You need duplicate detection
- ✅ You want content verification
- ✅ You might share implementations
- ⚠️ Can handle breaking change
- ⚠️ Don't mind storage overhead
- ❌ Don't need graceful moves

---

## Recommended Approach

### Phase 1: Implement Solution 1 (PackageRef)
- Best for error messages
- Handles moves gracefully
- Can migrate gradually
- Fixes immediate collision problem

### Phase 2 (Optional): Add contentHash
- If duplicate detection becomes important
- Add as additional field (like Solution 3)
- Don't change existing `id` field
- Structure: `{ id: Hash; location: Option<Location>; contentHash: Hash }`

This gives you:
- ✅ Best error messages (location in ref)
- ✅ Graceful moves (location can be stale)
- ✅ Content verification (Hash unchanged)
- ✅ Duplicate detection (add contentHash later if needed)
- ✅ Gradual migration (start with location = None)

---

## Key Insights

1. **Hash collisions are features, not bugs**
   - Same Hash = structurally identical
   - This is useful information to preserve

2. **Location is contextual metadata**
   - Helps with error messages
   - Can become stale
   - Should be optional

3. **Don't confuse identity with integrity**
   - Hash (content) verifies WHAT it is
   - Location tells WHERE it is
   - These are separate concerns

4. **Optimize for common case**
   - Most errors need good messages → Solution 1
   - Most types don't move → but when they do, grace matters
   - Most lookups by location → location hints help

---

## Summary Table

| Criteria | Sol 1 | Sol 2 | Sol 3 |
|----------|-------|-------|-------|
| Fixes collision | ✅ | ✅ | ✅ |
| Error messages | ✅✅✅ | ⚠️ | ⚠️ |
| Handles moves | ✅✅✅ | ❌ | ❌ |
| Detects duplicates | ✅ | ❌ | ✅ |
| Content verification | ✅ | ⚠️ | ✅ |
| Implementation complexity | ⚠️ | ✅ | ⚠️ |
| Migration path | ✅ | ❌ | ❌ |
| Storage overhead | ⚠️ | ✅ | ❌ |

**Winner:** Solution 1 (best overall, especially for UX)
