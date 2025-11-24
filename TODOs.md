# PackageManager - Hash-Based IDs

## Goal: Content-Addressed IDs

**Key Insight**: Hash the _content_ (body/implementation), NOT the location (owner/modules/name).

This means:
- If a function moves from `Darklang.Stdlib.List.map` to `Darklang.List.map`, the ID stays the same
- References don't break when items are renamed or reorganized
- True content-addressed storage

## Current State

✅ Infrastructure exists:
- `LibSerialization.Hashing.ContentHash` has the hashing utilities
- Binary serializers can serialize package items
- `PackageOp.hash` already hashes full ops (including content)

❌ Not yet done:
- Switch from location-based IDs to content-based IDs
- Update ID generation to hash the full item (type def, fn body, value)
- Handle the "chicken-and-egg" problem (need ID before we have the item to hash)

## Implementation Plan

### Phase 1: Understand the ID Assignment Flow

Current flow:
1. Parser encounters a definition (e.g., `let foo = 42`)
2. Parser calls `PackageIDs.Value.idForName owner modules "foo"` → gets a Guid
3. Parser creates `PackageValue` with that ID
4. Later, `PackageOp.AddValue` is created with the PackageValue

Problem: We need the content to hash, but we're assigning IDs before we have the full content parsed.

### Phase 2: Two-Pass ID Assignment

**Phase 1: Parse with temporary IDs**
- Parse with `OnMissing.Allow` (current behavior)
- Use random Guids or location-based temporary IDs
- Collect all PackageOps

**Phase 2: Compute content hashes and reassign IDs**
- For each op, hash the full content (type def, fn body, value)
- Generate stable ID from content hash
- Update all ops to use content-based IDs
- Update all references within the ops to use new IDs

**Phase 3: Re-parse with stable IDs** (optional, or merge with phase 2)
- If we need to re-parse for other reasons, use content-based IDs
- Otherwise, just use the ops with updated IDs

### Phase 3: Add Content Hashing Functions

In `LibSerialization.Hashing.ContentHash`, add:

```fsharp
module PackageType =
  /// Hash a complete PackageType definition (including all fields)
  /// The ID is ignored during hashing (set to Guid.Empty before hashing)
  let hash (typ : LibExecution.ProgramTypes.PackageType.PackageType) : Guid =
    let normalized = { typ with id = System.Guid.Empty }
    hashWithWriter Serializers.PT.PackageType.write normalized

module PackageFn =
  /// Hash a complete PackageFn definition (including body)
  /// The ID is ignored during hashing
  let hash (fn : LibExecution.ProgramTypes.PackageFn.PackageFn) : Guid =
    let normalized = { fn with id = System.Guid.Empty }
    hashWithWriter Serializers.PT.PackageFn.write normalized

module PackageValue =
  /// Hash a complete PackageValue definition
  /// The ID is ignored during hashing
  let hash (value : LibExecution.ProgramTypes.PackageValue.PackageValue) : Guid =
    let normalized = { value with id = System.Guid.Empty }
    hashWithWriter Serializers.PT.PackageValue.write normalized
```

### Phase 4: Add ID Rewriting Pass

In `LibPackageManager.PackageManager`, add:

```fsharp
/// Rewrite all IDs in ops to be content-based
/// Returns new ops with stable content-based IDs
let assignContentBasedIDs (ops : List<PT.PackageOp>) : List<PT.PackageOp> =
  // Build maps: old temporary ID → new content-based ID
  let typeIdMap = Dictionary<Guid, Guid>()
  let valueIdMap = Dictionary<Guid, Guid>()
  let fnIdMap = Dictionary<Guid, Guid>()

  // First pass: compute content hashes
  for op in ops do
    match op with
    | PT.PackageOp.AddType t ->
        let contentId = LibSerialization.Hashing.ContentHash.PackageType.hash t
        typeIdMap.Add(t.id, contentId)
    | PT.PackageOp.AddValue v ->
        let contentId = LibSerialization.Hashing.ContentHash.PackageValue.hash v
        valueIdMap.Add(v.id, contentId)
    | PT.PackageOp.AddFn f ->
        let contentId = LibSerialization.Hashing.ContentHash.PackageFn.hash f
        fnIdMap.Add(f.id, contentId)
    | _ -> ()

  // Second pass: rewrite all IDs (both definitions and references)
  ops |> List.map (rewriteOpIDs typeIdMap valueIdMap fnIdMap)

/// Helper to rewrite IDs within a single op
let rewriteOpIDs
  (typeIdMap : Dictionary<Guid, Guid>)
  (valueIdMap : Dictionary<Guid, Guid>)
  (fnIdMap : Dictionary<Guid, Guid>)
  (op : PT.PackageOp)
  : PT.PackageOp =
  // TODO: Need to recursively rewrite IDs in:
  // - Type definitions (field types may reference other types)
  // - Function bodies (expressions may reference types/values/fns)
  // - Value bodies (expressions may reference types/values/fns)
  // - SetTypeName/SetValueName/SetFnName (rewrite the ID part)
  ...
```

### Phase 5: Integration Points

Update parsing flows to use `assignContentBasedIDs`:

**LoadPackagesFromDisk.fs:**
```fsharp
let! reParsedOps = ... // second pass parsing

// NEW: Assign content-based IDs
let contentBasedOps = LibPackageManager.PackageManager.assignContentBasedIDs reParsedOps

// No more stabilization needed!
return contentBasedOps
```

**TestModule.fs:**
```fsharp
let! reParsedModules = ... // second pass

// NEW: Assign content-based IDs to each module's ops
let! adjustedModules =
  reParsedModules
  |> Ply.List.mapSequentially (fun m ->
    uply {
      let contentBasedOps = LibPackageManager.PackageManager.assignContentBasedIDs m.ops
      return { m with ops = contentBasedOps }
    })

return adjustedModules
```

**Canvas.fs:**
Similar pattern.

### Phase 6: Remove Stabilization

Once content-based IDs work:
- [ ] Remove `stabilizeOpsAgainstPM` from PackageManager.fs
- [ ] Remove all calls to it
- [ ] Tests should still pass (IDs are now stable by content)

## Challenges & Solutions

### Challenge 1: Recursive ID Rewriting

Type/fn/value bodies contain references to other items. When we change IDs, we need to update all references.

**Solution**: Write a deep recursive rewriter that traverses:
- Type definitions (field types)
- Expressions (EFnName, EVariable, type references)
- Type references in signatures

### Challenge 2: Circular References

What if type A references type B, and type B references type A?

**Solution**: This is fine! We hash with temporary IDs first, then rewrite. The content hash doesn't care about the IDs of referenced items, only the structure.

### Challenge 3: Non-Determinism

If there's any randomness in parsing (GUIDs for internal nodes), hashes won't be stable.

**Solution**: Ensure all internal IDs are deterministic or normalized before hashing.

### Challenge 4: Location Still Matters for Name Resolution

We still need `SetTypeName(id, location)` to map names to IDs during parsing.

**Solution**: Keep the location → ID mappings, but the IDs are now content-based instead of location-based.

## Testing Strategy

- [ ] Test: Parse same file twice → identical IDs (no stabilization)
- [ ] Test: Rename a function → ID changes (new content)
- [ ] Test: Move a function to different module → ID stays same (same content)
- [ ] Test: Change function body → ID changes (new content)
- [ ] Test: Circular type references hash correctly

## Open Questions

- Should we cache content hashes to avoid recomputing?
- What happens when binary serialization format changes? (All hashes change)
- How to handle migration from old location-based IDs?
- Should we store both location and content hash for debugging?
