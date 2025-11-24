# PackageIDs Analysis: Can We Remove Guid Strings?

## The Chicken-and-Egg Problem

### Timeline of Events:

```
1. F# Compilation
   ↓
   PackageIDs.fs compiled with hardcoded Guid strings
   ↓
   Builtins reference these IDs (e.g., TypeReference.option)

2. Runtime Starts
   ↓
   No packages loaded yet!
   ↓
   Builtins already need to reference Option/Result types

3. Package Parsing
   ↓
   Darklang stdlib (.dark files) parsed
   ↓
   Hashes computed from content
   ↓
   Must match IDs that builtins are already using!
```

**The Problem**: F# builtins need type IDs **before** Darklang packages are parsed.

---

## Current Usage of PackageIDs

### 1. Type Construction in F# Code

```fsharp
// RuntimeTypes.fs
let option (t : TypeReference) : TypeReference =
  TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.Stdlib.option), [ t ])

let result (t1 : TypeReference) (t2 : TypeReference) : TypeReference =
  TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.Stdlib.result), [ t1; t2 ])
```

**Used**: To construct Option/Result types in builtin function signatures.

---

### 2. Runtime Type Comparison

```fsharp
// CommonToDarkTypes.fs
| DEnum(FQTypeName.Package id, _, _, "Some", [ value ]) when
  id = PackageIDs.Type.Stdlib.option
  ->
  Some(f value)

// Dval.fs
let optionType = FQTypeName.fqPackage PackageIDs.Type.Stdlib.option

// ProgramTypesToRuntimeTypes.fs
| RT.FQTypeName.Package id when id = PackageIDs.Type.Stdlib.option ->
  match caseName, fieldValues with
  | "Some", [ fieldValue ] -> [ RT.Dval.toValueType fieldValue ]
```

**Used**: To identify when a Dval is an Option/Result/etc at runtime.

---

### 3. Parse-Time ID Assignment

```fsharp
// WrittenTypesToProgramTypes.fs
{ id = PackageIDs.Type.idForName pt.name.owner pt.name.modules pt.name.name
  declaration = ... }
```

**Used**: To ensure stdlib types get the **same IDs** that builtins expect.

---

### 4. PackageManager Stabilization

```fsharp
// PackageManager.fs
let stableId =
  match stableIdOpt with
  | Some id -> id  // From reference PM
  | None ->
    // Check PackageIDs for known stable ID
    LibExecution.PackageIDs.Type.idForName loc.owner loc.modules loc.name
```

**Used**: When syncing, ensure known stdlib types get stable IDs.

---

## Why Guids Worked

**Coordination via hardcoded values:**

```
PackageIDs.fs:
  Guid "9ce3b596-..." → Hash "abc123..." (via ToByteArray)

F# Builtins:
  PackageIDs.Type.Stdlib.option → Hash "abc123..."
  ↓
  TCustomType(..., Hash "abc123", ...)

Darklang Parsing:
  type Option = Some | None
  ↓
  id = PackageIDs.Type.idForName(...) → Hash "abc123..."  // SAME!
```

**Both sides converge on same Hash** because they both go through PackageIDs.

---

## Why Content Hashing Breaks This

```
F# Builtins (at compile time):
  Need: Hash of Option type
  Problem: Option type hasn't been parsed yet!
  Can't compute: Hash.ofContent(Option declaration)

Darklang Parsing (at runtime):
  type Option = Some(value) | None
  ↓
  Hash = ContentHash.hash(declaration) = "xyz789"

F# Builtins still expect: Hash "abc123" (from old Guid)

xyz789 ≠ abc123  ❌ MISMATCH!
```

---

## Potential Solutions

### Solution A: Keep Guid Strings (Status Quo)

**Pros**:
- ✅ Works today
- ✅ Provides coordination

**Cons**:
- ❌ Misleading (these aren't "real" content hashes)
- ❌ Guid strings are arbitrary
- ❌ Must maintain lookup table

---

### Solution B: Pre-compute Content Hashes

**Idea**: Compute Hashes from stdlib declarations, hardcode in PackageIDs.fs

```fsharp
// PackageIDs.fs
module Type =
  module Stdlib =
    // Precomputed from actual Option type declaration
    let option = Hash.unsafeOfString "abc123..."
```

**How to generate**:
1. Parse stdlib once
2. Compute content hashes
3. Copy Hash strings into PackageIDs.fs
4. Rebuild F#

**Pros**:
- ✅ Uses real content hashes
- ✅ Still provides coordination

**Cons**:
- ❌ Manual process (parse → copy → rebuild)
- ❌ Must re-run whenever stdlib declarations change
- ❌ Still chicken-and-egg (need to parse to get hashes to parse)

---

### Solution C: Hash.zero + Startup Resolution

**Idea**: Use placeholder, resolve at startup

```fsharp
// PackageIDs.fs
module Type =
  module Stdlib =
    let mutable option = Hash.zero  // Placeholder

// Startup.fs
let initializePackageIDs (pm: PackageManager) =
  PackageIDs.Type.Stdlib.option <-
    pm.findType(None, {owner="Darklang"; modules=["Stdlib"; "Option"]; name="Option"})
    |> Option.get
```

**Execution order**:
1. F# compiles with Hash.zero placeholders
2. Runtime starts
3. Parse stdlib packages
4. **Before** running any builtins, resolve PackageIDs from PM
5. Now builtins can use correct Hashes

**Pros**:
- ✅ Uses real content hashes
- ✅ Automatic (no manual copying)
- ✅ Always up-to-date

**Cons**:
- ❌ Mutable state (PackageIDs become mutable)
- ❌ Must initialize before any builtin runs
- ❌ Can't use PackageIDs at F# compile time (they're zero)

---

### Solution D: Location-Based References (New!)

**Idea**: Builtins reference by **location**, not Hash

```fsharp
// RuntimeTypes.fs - BEFORE
let option (t : TypeReference) : TypeReference =
  TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.Stdlib.option), [ t ])

// RuntimeTypes.fs - AFTER
let option (t : TypeReference) : TypeReference =
  let loc = PackageLocation.stdlib ["Option"] "Option"
  TCustomType(Unresolved loc, [ t ])
  // Resolved later via PM lookup
```

**At type-checking time**:
```fsharp
match typeRef with
| TCustomType(Unresolved loc, typeArgs) ->
  let! hash = pm.findType(None, loc)
  TCustomType(Resolved hash, typeArgs)
```

**Pros**:
- ✅ No hardcoded IDs needed!
- ✅ Uses content hashing naturally
- ✅ Locations are stable (names don't change often)

**Cons**:
- ❌ Need two-phase type references (Unresolved → Resolved)
- ❌ More complex type system
- ❌ Can't pattern match on Hash directly

---

### Solution E: Lazy Lookup Functions

**Idea**: PackageIDs provides **lookup functions**, not Hashes

```fsharp
// PackageIDs.fs
module Type =
  module Stdlib =
    let private _option = lazy (
      pm.findType(None, {owner="Darklang"; modules=["Stdlib"; "Option"]; name="Option"})
      |> Option.get
    )
    let option : Hash = _option.Value

// RuntimeTypes.fs (unchanged)
let optionType = FQTypeName.fqPackage PackageIDs.Type.Stdlib.option
```

**Pros**:
- ✅ Automatic resolution
- ✅ Lazy evaluation (only when needed)
- ✅ Same API as current

**Cons**:
- ❌ Requires global PM reference
- ❌ Lazy initialization can be tricky
- ❌ What if PM not initialized yet?

---

### Solution F: Hybrid (Guid → Hash at Startup)

**Current state**: Guid strings converted to Hash at F# compile time

**Proposed**: Keep Guid strings, but replace with content Hashes at startup

```fsharp
// PackageIDs.fs
module Type =
  module Stdlib =
    // Still use Guid initially (for coordination)
    let mutable option = Hash.ofGuid (Guid.Parse "9ce3b596-...")

// Startup.fs
let stabilizePackageIDs (pm: PackageManager) =
  // Replace Guid-based hashes with content hashes
  match pm.findType(None, {owner="Darklang"; modules=["Stdlib"; "Option"]; name="Option"}) with
  | Some contentHash ->
    PackageIDs.Type.Stdlib.option <- contentHash
  | None ->
    failwith "Option type not found in stdlib!"
```

**WrittenTypesToProgramTypes.fs changes**:
```fsharp
// BEFORE: Use PackageIDs to assign ID
{ id = PackageIDs.Type.idForName pt.name.owner pt.name.modules pt.name.name
  ... }

// AFTER: Use content hash directly
let hash = ContentHash.PackageType.hash { id = Hash.zero; declaration = declaration; ... }
{ id = hash
  ... }
```

**Result**:
- Stdlib types get content hashes
- At startup, PackageIDs is updated to point to those hashes
- F# code continues to work (references get resolved to content hashes)

**Pros**:
- ✅ Minimal changes to F# code
- ✅ Eventually uses content hashing
- ✅ Coordination still works (via startup replacement)

**Cons**:
- ❌ Still has Guid strings (but they're temporary)
- ❌ Mutable PackageIDs
- ❌ Must stabilize before running builtins

---

## Recommended Approach

**Solution F (Hybrid) seems most pragmatic:**

### Phase 1: Keep Guids, Add Stabilization
1. Keep PackageIDs.fs as-is (with Guid strings)
2. Add startup stabilization that replaces Guid-hashes with content-hashes
3. Change WrittenTypesToProgramTypes to use content hashing directly
4. At startup, update PackageIDs to match actual content hashes

### Phase 2 (Optional): Remove Guids
1. After stabilization works, capture all content hashes
2. Replace Guid strings with Hash strings in PackageIDs.fs
3. Remove stabilization (no longer needed)

---

## Alternative: Solution D (Location-Based)

**If willing to refactor more deeply:**

This is the "correct" long-term solution:
- Builtins reference by **location** (what they mean)
- Hashes computed from **content** (what they are)
- Resolution happens at type-checking time

But requires:
- Two-phase type references (Unresolved → Resolved)
- Changes throughout type system
- More invasive refactoring

---

## Open Questions

1. **When does stabilization happen?**
   - After parsing all stdlib?
   - Before first builtin runs?
   - As a separate initialization step?

2. **What if stdlib type definition changes?**
   - Content hash changes
   - Guid-based hash stays same
   - Stabilization updates PackageIDs
   - Old serialized data might have old hash?

3. **Do we need PackageIDs for user packages?**
   - Currently only for stdlib (Darklang-owned)
   - User packages don't have coordination problem
   - Could use location-based refs for user code

4. **Performance of mutable PackageIDs?**
   - How often are these accessed?
   - Is mutation a problem?
   - Could use lazy/once-cell pattern?

---

## Summary

**The Core Issue**: F# code needs to reference Darklang types before they're parsed.

**Why Guids Work**: Provide coordination point - both F# and Darklang code go through same PackageIDs lookup.

**Why Content Hashing Alone Fails**: Can't compute Hash(Option) until Option is parsed, but F# needs it at compile time.

**Best Path Forward**:
- **Short term**: Solution F (Hybrid) - keep Guids, stabilize at startup
- **Long term**: Solution D (Location-based) - refactor to two-phase resolution

**Key Insight**: PackageIDs isn't about the Guid strings themselves - it's about providing **coordination** between F# compile-time references and Darklang parse-time definitions.
