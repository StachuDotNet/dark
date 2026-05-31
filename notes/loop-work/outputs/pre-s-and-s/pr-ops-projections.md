# PR: Separate ops from projections

The spine's floor effort 3, built from
[distributed-event-sourcing.md](distributed-event-sourcing.md) (Storage). Also answers the
"minimum F# / PT for the core ops layer" question. (The spine references *down* to this spec.)

**Goal.** Make the **op log canonical** and every package table a **regenerable projection**,
and split them **physically**: ops in `core.db`, projections in per-branch caches that can be
dropped and re-folded. After this, a projection can be deleted with zero information loss.

> **Validated in prework** (`loop-fun:prework/ops-projections`). Implemented
> `LibDB.Seed.rebuildProjections` — clears the regenerable projection tables, marks all
> `package_ops` unapplied, and re-folds them through the existing `PackageOpPlayback.applyOps`.
> **Compiles + the drop-rebuild=identity TEST PASSES**: on a seeded DB, drop the projections,
> re-fold the whole op log, and `package_functions` + `locations` come back **identical**. So the
> design's central claim — *projections are regenerable from the canonical op log* — is **proven
> by running code**, and the "minimum F#" really is mostly reorganizing what exists.
> - **Which tables are projections — corrected by the code.** The authoritative list is the 5
>   tables `Seed.export` strips as derived data: `package_functions`/`types`/`values`,
>   `locations`, `package_dependencies`. **`package_blobs` is *canonical content*, NOT a
>   projection** (blob bytes aren't re-derivable by folding ops) — clearing it loses data. (An
>   early version over-cleared it; the test now asserts `package_blobs` is preserved.) So in
>   `core.db`-vs-projections terms: ops + `package_blobs` (content) are canonical; the other 5
>   are the regenerable cache. The spec's "every package table is a projection" needs this caveat.
> - Fix: `Sql.executeStatementAsync` is `Task<unit>` — use `do!`.
>
> **Later note (not now):** the prototype hardcodes the 7 projection tables in F#. Worth thinking
> through someday whether the op/projection *shape* could be Dark-declared (an App naming its
> projections, walked by a generic rebuild) rather than F#-hardcoded — left as a seam to revisit,
> not a near-term goal.

**The reassuring part:** `main` already has the bones — `package_ops`/`branch_ops` (the op
log), `package_functions`/`package_types`/`package_values`/`locations`/`deprecations`/
`package_dependencies` (projections), and **`LibDB.Seed.growIfNeeded`** already "applies
unapplied ops to rebuild projection tables." This PR *formalizes and physically splits* what's
already there — it is mostly reorganization, not new machinery.

**Prereqs.** None (leaf). Unblocks the sync PR (ships ops into `core.db`) and the durable
EventBus buses (also live in `core.db`).

## .fs changes

| File (on `main`) | Change |
|---|---|
| `LibExecution/ProgramTypes.fs` | **No change.** `PackageOp` (`AddType`/`AddValue`/`AddFn`/`SetName`/`Deprecate`) already exists — the op *is* the canonical unit. Stated explicitly: no PT change, no hash churn, no two-build dance. |
| `LibDB/Seed.fs` | Promote `growIfNeeded`'s fold into a first-class **`rebuildProjections : branchId -> Ply<unit>`** that folds `package_ops`/`branch_ops` into a *fresh* projection DB (today it grows the same file). |
| `LibDB/Db.fs` (connection mgmt) | Open **two** connections: `core.db` (ops + commits + branches + accounts) and `branches/<branch>.db` (projections). Route reads of derived tables to the branch DB, op appends to `core.db`. |
| `LibDB/PackageManager.fs` | Resolve `name → hash` and load bodies from the branch projection DB; on a miss / stale flag, `rebuildProjections` then retry. |
| `LocalExec/Migrations.fs` | Two schemas instead of one (below). The kill-and-fill stays, but projection DBs are *always* safe to kill (they re-fold). |

The **minimum F#** (emailed #3): the op log table + the fold (`rebuildProjections`) already
exist; the genuinely new code is the **two-connection split** + a small **projection registry**
(what each projection folds, its scope, its invalidation trigger). That's it — a few hundred
lines of reorganization, not a new subsystem.

> **The two-DB split's *engine* is already built (prework) — by the sync PR's `connStore`.** The
> LibPM seam's `connStore connStr` (a `PackageStore` whose handlers write to a *given* connection)
> + `dispatchVia` *is* the per-branch-DB refold: folding `core.db`'s op stream into a separate
> store's projection tables. **Tested (LibPmSeam 7/7):** a 4-op stream (2 `AddFn` + 2 `SetName`)
> folds from `package_ops` into a standalone "branch.db" holding **only** `package_functions` +
> `locations` (no op log), which then resolves **both** names independently. That's exactly this
> PR's claim — *ops in `core.db`, projections in a droppable per-branch DB refolded from the log*.
> So **one mechanism serves two PRs**: sync's cross-store fold and this physical split. What's left
> for this PR is the *routing* (open the two connections, send reads to the branch DB / appends to
> `core.db`) — the fold-into-a-separate-store half is done and tested.
>
> **And the projection REGISTRY (below) is built too** (`Seed.projectionRegistry`, OpsProjections
> 3/3): a `Projection { table; foldsOpKinds; dirtiedBy }` over the 5 regenerable projections —
> `rebuildProjections` now derives its clear-list from it (single source of truth, can't drift from
> the descriptors), and `projectionsDirtiedBy opKind` gives the incremental-refold targets (`AddFn`
> → functions+deps, `SetName`/`RevertPropagation` → locations, the no-op `PropagateUpdate` → none).
> So **both** of this PR's "genuinely new" pieces exist: the fold engine *and* the registry; the
> remaining work is the two-connection routing.

```fsharp
// the projection registry — the only genuinely new abstraction
type Projection =
  { name        : string
    scope       : Global | PerBranch | PerSession
    foldsOpKinds : Set<OpKind>                 // which ops feed it
    dirtiedBy    : Set<OpKind> }               // which incoming ops invalidate it

let rebuildProjections (branchId: BranchId) : Ply<unit> = uply {
  // 1. open a fresh branches/<branch>.db   2. fold package_ops|branch_ops in order
  // 3. each Projection materializes its tables from the fold   (this is Seed.grow, generalized)
  () }
```

## SQL/schema — this is a PR where SQL matters

Split today's single `schema.sql` into two:

```
core.db        (canonical, synced)        branches/<branch>.db   (projection cache, DROP-able)
  package_ops      ← the op log              package_functions   ┐
  branch_ops       ← the op log              package_types       │ all regenerable
  commits, branches                          package_values      │ by folding core.db
  accounts_v0                                locations           │
  (durable EventBus buses land here later)   deprecations        │
                                             package_dependencies┘
```

Migration: move the projection tables out of the main schema into the branch schema; `core.db`
keeps only ops + commit/branch/account state. A branch DB carries a `folded_through_seq`
marker so a rebuild knows where it left off (incremental re-fold).

## Test plan

| Step | Test | Done-signal |
|---|---|---|
| fold is total | `.fs` (`OpsProjectionsTests`, new): seed ops, `rebuildProjections`, assert projection tables match a known-good snapshot | tables equal |
| drop + rebuild = identity | `.fs`: resolve `Stdlib.List.map`'s hash; `DROP` the branch DB; `rebuildProjections`; resolve again | same hash, byte-identical |
| `.dark` round-trip | `.dark` test (add/adjust `packages/.../tests`): define a fn, force a projection rebuild, call it | same result before/after |
| incremental | append one op; assert only the dirtied projection entries refold (not a full rebuild) | `folded_through_seq` advances by 1 — **DONE end to end (OpsProjections 5/5):** `projectionsDirtiedByBatch` picks the dirtied tables, and **`rebuildDirtied opKinds`** clears only those + re-folds only the ops of those kinds (filter by `opKindName` → `applyOps`). Tested: `rebuildDirtied {AddFn}` refolds `package_functions`(+deps) while **`locations` is unchanged** — selectivity proven. (Faithful for content-addressed `Add*`; a `SetName`-only refold needs its batch's added hashes for rename detection — documented.) |

## CLI impact

- New: **`dark branch rebuild`** — drop + re-fold this branch's projections (recovery / after a
  schema bump). Mostly a safety valve.
- `dark status` gains a line: ops count in `core.db` vs projection `folded_through_seq` (shows
  staleness). Otherwise no command changes. **Pure surface built (prework, `status-cli.dark` 4/4):**
  `statusLine opsCount foldedThrough` → `✓ up to date (N ops)` when caught up, else `core.db: N ops
  · projections folded through M (K behind)` — over the op-log size + the projection cursor (both
  F#-built). The observable "is my local cache current?" line.

## UX change

**Nothing user-visible in normal use** — resolution/search/run behave identically; the split is
under the hood. The only new surface is `dark branch rebuild` for recovery. Before/after a
rebuild, a user sees no difference — which is the point (projections are non-authoritative).

## Risks / problems not yet raised

- **Two-DB write ordering.** An op append (`core.db`) must commit *before* its projection update
  (`branch.db`); if the projection write fails, the next read re-folds and recovers. Never the
  reverse — a projection must never be ahead of the op log.
- **Branch-DB proliferation.** Many branches → many files. GC closed branches' projection DBs
  (they re-fold on demand); only `core.db` is durable.
- **Concurrent sessions** on one branch share its projection DB — needs the same lock-claim
  discipline as the durable buses, or per-session projection scoping.
- **Migration of an existing `data.db`** in the field: first run splits it (ops stay, projections
  move to a branch DB); a one-time `growIfNeeded` rebuild covers any gap.

## Above / below

- **Below:** nothing.
- **Above expects:** the sync PR appends remote ops into `core.db` and triggers
  `rebuildProjections`; the durable EventBus buses persist into `core.db`. Both depend only on
  "`core.db` is the one synced, canonical store" — frozen here.
