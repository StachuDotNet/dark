# Propagation Ops: Purity & Sync Analysis

## Background

Propagation was recently added to handle a real problem: when a low-level package item changes, all transitive dependents need new versions pointing to the updated item. Without propagation, a change to e.g. `Stdlib.List.map` would require the user to manually create hundreds of individual ops for everything that calls it.

The system works by:
1. Discovering all transitive dependents (recursive graph walk via `getDependentsBatch`)
2. Generating new UUIDs for ALL dependents (`System.Guid.NewGuid()`)
3. Transforming each dependent item via `AstTransformer` (rewriting UUID references), creating `Add` + `SetName` ops for each
4. Appending a `PropagateUpdate` metadata op at the end

**Key files:** `Propagation.fs` (algorithm), `PackageOpPlayback.fs` (how ops are applied), `ProgramTypes.fs` (op type definitions)

## How Propagation Ops Differ from Core Ops

The 6 "core" ops (`AddType`, `AddValue`, `AddFn`, `SetTypeName`, `SetValueName`, `SetFnName`) are pure in a specific sense:
- Deterministic: same input → same op content → same content-addressed hash
- Self-contained: each op carries everything needed to apply it
- Symmetric: no special undo mechanism needed (you just create a new version)
- Independent: each op can be applied in isolation

The 2 propagation ops break several of these properties:

### 1. `PropagateUpdate` is a no-op during playback

`PackageOpPlayback.fs` lines 206-210:
```fsharp
| PT.PackageOp.PropagateUpdate _ ->
  // Location changes are already handled by the individual SetFnName/SetTypeName/
  // SetValueName ops that accompany this op in the propagation batch.
  ()
```

The real work is done by the preceding batch of `Add` + `SetName` ops. `PropagateUpdate` is metadata — it records WHAT was propagated but doesn't DO anything. It's an annotation on a group of ops, not an action.

### 2. `RevertPropagation` uses a different mechanism

Instead of generating `Add` + `SetName` ops (like forward propagation does), it directly manipulates `deprecated_at` flags on locations via SQL (`PackageOpPlayback.fs` lines 211-295). It un-deprecates old locations and deprecates new ones.

This is asymmetric: forward propagation goes through the normal op machinery, but reverting bypasses it. The revert also depends on the exact location state in the DB at revert time — it queries for "most recently deprecated location for this item" to un-deprecate.

### 3. Non-deterministic UUID generation

`Propagation.fs` line 191: `System.Guid.NewGuid()` for every dependent. If two machines independently propagate the same change, they get different UUIDs for every dependent → different `Add`/`SetName` op content → different content-addressed op hashes → both sets inserted on sync → duplicated items with different UUIDs at the same locations.

### 4. Implicit batch semantics

A propagation produces `N×2` ops (`Add` + `SetName` per dependent) followed by 1 `PropagateUpdate`. These MUST be applied together as a batch. But there's no explicit transaction boundary in the op stream — it relies on them being contiguous. If sync somehow interleaves ops from two sources, the intermediate state could be inconsistent.

## Sync Safety Assessment

**Safe scenario (the normal case):** Propagation happens on ONE machine, then syncs to others. The receiving machine replays the `Add` + `SetName` ops (which are fully deterministic once created) and skips the `PropagateUpdate` no-op. Content-addressed op IDs prevent duplicates. This works.

**Unsafe scenario:** The same logical change triggers propagation on two machines independently (both offline, both edit the same function, both propagate). Result: divergent dependency trees with different UUIDs for the same conceptual items, duplicated content, location conflicts.

**Mitigation:** Push-before-propagate discipline. As long as propagation only runs locally and the generated ops are synced (never re-generating propagation on incoming ops), sync is safe. `insertAndApplyOps` replays pre-generated ops; it never calls `Propagation.propagate()` on incoming ops.

**`RevertPropagation` is more fragile for sync.** It does direct location manipulation based on UUIDs stored in the op. If the local DB's location state doesn't exactly match what the originating machine had when it generated the revert, the un-deprecation SQL queries might hit wrong rows or miss entirely.

## Possible Improvements

### A. Content-addressed / hash-based dependent UUIDs (HIGH VALUE)

Instead of `Guid.NewGuid()`, derive new UUIDs deterministically:
```
newUUID = hash(sourceUUID + dependentItemId + changeSequenceNumber)
```

This makes propagation **idempotent across machines**. Two machines propagating the same change get the same new UUIDs → same ops → deduplicated on sync via content-addressed op IDs.

**Note:** Darklang has tried content-addressed/hash-based IDs before and moved away from them — the reasons should be investigated before committing to this approach. The current system uses content-addressed IDs for ops (SHA256 of binary blob → GUID) but location-addressed IDs for package items (name-based lookup). There may have been good reasons for the divergence.

**Even if full hash-based item IDs aren't right**, deterministic UUID derivation specifically for propagation-generated items could work as a scoped change. The key insight: propagation UUIDs don't need to be content-addressed in general — they just need to be deterministic given the same inputs (source item + dependent item + change identity).

### B. Make `PropagateUpdate` a real op (MEDIUM VALUE)

Instead of being a no-op with separate `Add` + `SetName` ops, make it a single op that CONTAINS all the repoint data AND the new item definitions. Playback would apply the whole batch atomically.

Benefits:
- Eliminates implicit batch boundary problem
- Makes op stream more self-describing
- Cleaner transaction semantics

Downside: Larger single op (but max op blob is already 31KB, not a real constraint).

### C. Make `RevertPropagation` symmetric with forward propagation (MEDIUM VALUE)

Instead of direct location manipulation, have revert generate `Add` + `SetName` ops that restore the old versions. This would:
- Make revert go through the same code path as forward propagation
- Remove dependency on exact location state at revert time
- Make revert safe for sync (same determinism guarantees as forward)

### D. Eliminate propagation ops entirely (RADICAL, probably not now)

Store "virtual pointers" — name resolution would dynamically resolve references through the latest version. A change to `Stdlib.List.map` wouldn't spawn N new versions; dependents would reference the NAME (stable) rather than a specific UUID.

This is a much bigger architectural change. Propagation is essentially a denormalization optimization — it pre-computes what could theoretically be resolved at lookup time. But the current "immutable items, UUID references" model has real benefits for caching, reproducibility, and debugging.

## Recommendation for Sync

For the immediate sync implementation:
1. **Don't re-propagate on the receiving side.** Sync the generated ops as-is. This is already the plan.
2. **Document the push-before-propagate discipline.** Make it clear that if you propagate locally, you must push before anyone else changes the same dependency graph.
3. **Investigate hash-based propagation UUIDs** as a follow-up. This is the single highest-value change for making propagation sync-safe in the face of concurrent offline work.
4. **Consider making RevertPropagation generate real ops** instead of doing direct location manipulation. This is lower priority but would make sync more robust.

None of these block the initial sync implementation — they're hardening improvements for after basic push/pull is working.
