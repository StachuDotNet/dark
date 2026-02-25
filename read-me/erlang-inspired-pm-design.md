# Toward an Erlang-Inspired Package Distribution Model for Dark

*Design Report -- February 2026 (v2)*

---

## 1. The Problem

Dark's PackageOp is currently a single flat enum mixing several fundamentally
different kinds of mutations: adding immutable content, binding mutable names,
cascading dependency updates, and branch lifecycle. These have different conflict
models, ordering constraints, and distribution strategies -- but they're all
treated the same way.

Erlang/OTP, by contrast, has a strict layered hierarchy of operation categories
(code loading, process state, supervisor topology, application lifecycle, release
orchestration), where each layer's ops depend on and orchestrate the layers below.

Dark should adopt this layered approach.

---

## 2. Five Categories of Ops

### Category 1: Infrastructure Ops

*Create the containers that scope everything else.*

```
InfraOp =
  | CreateBranch of name * parentBranchId
  | RenameBranch of branchId * newName
  | DeleteBranch of branchId
```

- **Conflict model**: Name uniqueness only. Simple reject on duplicate.
- **Ordering**: Must be applied before any ops that reference the branch.
- **Distribution**: Replicate eagerly. Lightweight metadata.
- **Erlang analog**: `net_kernel:connect_node` + `mnesia:create_schema` --
  establishing the infrastructure that everything else runs on.

Currently these are direct SQL mutations, not ops. Making them ops enables sync
and audit trail. Branch ops are the *foundation layer* -- binding ops are scoped
to branches, so branch ops must come first.

### Category 2: Content Ops

*Add immutable, content-addressed package items.*

```
ContentOp =
  | AddType of PackageType
  | AddValue of PackageValue
  | AddFn of PackageFn
```

- **Conflict model**: None. Content-addressed (SHA256 -> UUID). Two nodes
  independently adding identical content produce the same UUID. This is a
  **grow-only set (G-Set CRDT)** -- merge is set union.
- **Ordering**: Unordered. Commutative, associative, idempotent.
- **Distribution**: Sync eagerly, freely, everywhere. Always safe.
- **Erlang analog**: `code:load_object_code` -- staging bytecode in memory
  without activating it. The bytes exist but no name points to them yet.

This is the easy world. Write-once, conflict-free, global. The current system
already handles this well.

### Category 3: Binding Ops

*Mutable name-to-content pointers, scoped to branches.*

```
BindingOp =
  | Bind of location * itemId * previousItemId option
  | Unbind of location * reason
  | Rename of fromLocation * toLocation * itemId
```

- **Conflict model**: Write-write conflicts on same name. Requires resolution.
  Options (from simple to sophisticated):
  1. Branch isolation (current model -- conflicts surface at merge)
  2. Last-writer-wins (Lamport timestamp, lossy)
  3. Multi-value register (Riak-style siblings, human resolves)
  4. Intent-based OT (carry enough info to auto-rebase)
- **Ordering**: Ordered within a name. Causal -- depends on Infrastructure
  (branch exists) + Content (item exists).
- **Distribution**: Replicate with review. Binding changes may need human
  approval before activation.
- **Erlang analog**: `global:register_name` with conflict resolution functions,
  or the code server's current/old module table.

This is the hard world. Mutable, branch-scoped, conflict-prone. The location
table is essentially a distributed mutable registry, and registries need explicit
conflict semantics.

Key design point: bindings should carry `previousItemId` (what they replaced) to
enable both revert and intent-based conflict resolution later.

### Category 4: Derivation Ops

*Computed cascades of binding changes triggered by upstream changes.*

```
DerivationOp =
  | PropagateBindings of
      propagationId * triggerOp * List<DerivedBinding>
  | RevertPropagation of
      revertId * propagationIds * List<RestoredBinding>

DerivedBinding = {
  location: PackageLocation
  fromItemId: UUID       // old dependent version
  toItemId: UUID         // new dependent version (rewritten AST)
  dependsOnOp: OpId      // the binding op that triggered this
}
```

- **Conflict model**: Distinct from binding conflicts. A derivation conflict
  means "the trigger changed since I computed this cascade." Resolution is
  recomputation, not human choice.
- **Ordering**: Depends on the triggering binding op + all content ops for the
  new dependent versions.
- **Distribution**: Can be recomputed on the receiving side (if UUIDs are
  deterministic) OR shipped as pre-computed ops (current approach). The
  `propagationId` groups them for atomic revert.
- **Erlang analog**: The relup's `code_change` cascade -- when you upgrade a
  module, the release handler walks the supervision tree, suspends each affected
  process, calls `code_change/3`, and resumes. Each step depends on the previous.

Currently Dark has monolithic `PropagateUpdate` ops. The proposal is to decompose
them into chains of individual derived bindings, each with explicit dependency on
its trigger. This enables:
- Partial propagation (apply some, review others)
- Granular conflicts (one dependent conflicts, others don't)
- Visible causal structure

Open question: should derivation ops be a sub-category of binding ops (same
table, extra metadata) or a fully separate category? The conflict model differs
enough to warrant separation, but they produce the same effect (name -> UUID
changes).

### Category 5: Lifecycle Ops

*Change the visibility and status of other ops without changing their content.*

```
LifecycleOp =
  | Commit of branchId * message * List<OpId>
  | Discard of branchId * List<OpId>
  | Merge of childBranchId * parentBranchId
  | Rebase of branchId * newBaseCommitId
```

- **Conflict model**: Precondition-based. Merge requires "is rebased, no WIP,
  no children." Rebase can detect location conflicts (same path modified on
  both sides). These aren't value conflicts -- they're state-machine violations.
- **Ordering**: Depends on everything below. Commit depends on ops existing.
  Merge depends on commit (no WIP). Rebase depends on branch structure.
- **Distribution**: Must be coordinated. A merge on one node must be visible
  to all nodes atomically (or the op stream diverges).
- **Erlang analog**: `release_handler:make_permanent` -- doesn't change what
  the code IS, changes its STATUS from "installed" to "permanent." Also
  `release_handler:unpack_release` (staging) and `install_release` (activating).

Currently commits and merges are direct SQL mutations. Making them ops enables:
- Syncing branch lifecycle between nodes
- Audit trail of all status transitions
- Potential for "pending merge" as a reviewable state

---

## 3. The Dependency DAG

These five categories form a layered dependency graph:

```
  Infrastructure Ops        (branches exist)
        |
        +-----> Content Ops  (items exist, global, conflict-free)
        |           |
        +-----------+-----> Binding Ops    (names point to items)
                                |
                    +-----------+-----------+
                    |                       |
              Derivation Ops          Lifecycle Ops
              (cascaded bindings)     (commit/merge/rebase)
```

Each op declares its dependencies explicitly:

```
Op {
  id: content-hash-of-payload
  category: Infrastructure | Content | Binding | Derivation | Lifecycle
  depends_on: List<OpId>
  payload: ...
}
```

**Why explicit dependencies matter for distribution:**

- Sync sends the DAG, not a flat list. Receiving node applies in any valid
  topological order.
- Partial sync is valid if dependency-closed. You can sync all content ops
  without any binding ops -- the content just exists unbound.
- Content ops can stream eagerly (always safe). Binding ops can buffer for
  review. Lifecycle ops need coordination.
- The dependency structure makes relationships visible even when computed
  values (like propagation UUIDs) differ between machines.

---

## 4. Conflict Resolution Per Category

| Category       | Can Conflict? | Detection              | Resolution Strategy         |
|----------------|---------------|------------------------|-----------------------------|
| Infrastructure | Name only     | UNIQUE constraint      | Reject duplicate            |
| Content        | Never         | Content-addressing     | Set union (CRDT)            |
| Binding        | Yes (w-w)     | Same name, diff UUID   | Multi-value -> human review |
| Derivation     | Stale trigger | Base UUID changed      | Recompute from new base     |
| Lifecycle      | Preconditions | State-machine check    | Fix precondition, retry     |

The key insight: **each category has a fundamentally different conflict model.**
Mixing them into a single op type (as today) means you can't apply the right
resolution strategy per conflict type.

---

## 5. Erlang Analogy Map

| Dark Category    | Erlang Layer                    | Key Erlang Ops                        |
|------------------|---------------------------------|---------------------------------------|
| Infrastructure   | Distribution + Schema           | connect_node, create_schema           |
| Content          | Code staging                    | load_object_code, prepare_loading     |
| Binding          | Code activation + Registry      | load/purge, register_name             |
| Derivation       | Process state migration         | suspend, code_change, resume          |
| Lifecycle        | Release orchestration           | install_release, make_permanent       |

Erlang's deepest lesson: these layers form a **strict dependency hierarchy**.
You cannot `code_change` a process until the new code is loaded. You cannot
`make_permanent` until all processes have been migrated. You cannot load code
until the node is connected. The sequencing is not optional -- it IS the
architecture.

The other crucial Erlang lesson: **every layer needs both "do" and "undo."**
Appup files have symmetric upgrade/downgrade instructions. `install_release`
has `reboot_old_release`. `add_table_copy` has `del_table_copy`. Dark's op
system should ensure every op category has clean revert semantics.

---

## 6. Two-Version Concurrency

Erlang maintains two versions of a module simultaneously: processes running old
code continue until they make a fully-qualified call, then switch to current.

Dark should adopt this for bindings. During transitions, a location holds both
versions:

```
LocationState {
  current: UUID
  previous: Option<UUID>
  transition: Immediate | GracefulDrain | RequiresReview
}
```

For shared packages, `RequiresReview` is the right default: propagation is
*computed* but not *activated* until the developer reviews each downstream
binding change.

---

## 7. Distribution Strategy Per Category

| Category       | Sync Strategy         | Eagerness   | Review Needed? |
|----------------|-----------------------|-------------|----------------|
| Infrastructure | Replicate immediately | Eager       | No             |
| Content        | Replicate freely      | Eager       | No             |
| Binding        | Replicate, buffer     | On-demand   | Yes (optional) |
| Derivation     | Recompute or ship     | Lazy        | Yes            |
| Lifecycle      | Coordinate            | Synchronous | Depends        |

Content ops form the "always safe" base layer. A node can accept content from
anywhere, anytime, without risk. Binding ops are where human judgment enters.
Derivation ops are expensive to compute but cheap to ship. Lifecycle ops need
coordination to prevent divergence.

---

## 8. What Changes in Dark

**Phase 1 -- Structural separation (no new features):**

1. Split `PackageOp` into 5 enums with separate tables and serialization.
2. Add `depends_on: List<OpId>` to each op.
3. Add `previousItemId` to binding ops for revert/intent support.
4. Make branch create/delete/rename into ops (currently direct SQL).
5. Make commit/discard into ops (currently direct SQL).

**Phase 2 -- New conflict semantics:**

6. Multi-value bindings: location can hold multiple UUIDs in conflicted state.
7. Decompose PropagateUpdate into chains of dependent DerivedBinding ops.
8. Deterministic propagation UUIDs: `hash(sourceId ++ dependentId ++ seq)`.
9. Conflict resolution commands in CLI: `dark resolve <location>`.

**Phase 3 -- Distribution:**

10. Streaming sync with per-category strategies.
11. Namespace-scoped subscriptions.
12. Two-version bindings with transition states.

---

## 9. Open Questions

1. Should Derivation Ops be a sub-category of Binding Ops or fully separate?
   They produce the same effect but have different conflict models.

2. Should canvas/handler/deployment ops be a 6th category? Handlers determine
   what code *runs* (not just what *exists*), which is a distinct concern.

3. How granular should op dependencies be? Per-op (fine but verbose) vs
   per-category (coarse but simple)?

4. Should lifecycle ops (commit, merge) be represented as ops-on-ops, or as
   a separate metadata layer? Commits "about" other ops feel different from
   ops that create content or bindings.

5. What's the right default conflict resolution for bindings during merge?
   Branch isolation (current) works for single-dev but doesn't scale.
