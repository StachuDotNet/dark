# SQLite Schema for the Substrate

> Loop T19 (2026-05-20). Inventory of the 18 tables on `main` +
> the new tables proposed across the substrate sketches.

Source of truth: `backend/migrations/schema.sql` on `main`
(~346 LoC, **18 tables**, kill-and-fill on schema-hash change).

## Existing tables on `main`

Confirmed via `git show main:backend/migrations/schema.sql`.

### Bookkeeping

| Table | Role |
|---|---|
| `system_migrations_v0` | legacy per-name migration log; pre-cutover DBs adopt via here |
| `accounts_v0` | identities. Seeded: Darklang/Stachu/Paul/Feriel. Just `(id, name, created_at)` today |

### Branches, commits, ops

| Table | Role |
|---|---|
| `branches` | branch model. `parent_branch_id`, `base_commit_hash`, `archived_at`, `merged_at`. `main` is pre-seeded. |
| `commits` | hash-keyed; `message`, `branch_id`, `account_id` (defaults to Darklang), `created_at`. Indexed by `(branch_id, created_at DESC)`. |
| `package_ops` | source of truth for package changes. `op_blob` (binary), `branch_id`, `commit_hash` (NULL = WIP), `applied`, `propagation_id`, `created_at`. 4 indexes (WIP, created, applied, propagation_id). |
| `branch_ops` | content-addressed branch ops. `(id = content hash, op_blob, applied)`. Idempotent INSERT OR IGNORE. |

### Package projections (content-addressed)

Definitions stored once per content hash; `locations` is the
branch-scoped name-resolution layer pointing at hashes.

| Table | Role |
|---|---|
| `package_types` | (hash, body) — RT-serialized type defs |
| `package_values` | (hash, body) — RT-serialized values |
| `package_functions` | (hash, body) — RT-serialized fn bodies |
| `package_blobs` | content-addressed bytes (Blob refs); orphan sweep via `LibDB.RuntimeTypes.Blob.sweepOrphans` |
| `locations` | branch-scoped (owner, modules, name) → hash. `unlisted_at` tracks pointer lifecycle (separate from author-initiated `deprecations`). |
| `deprecations` | author-initiated deprecation markers + message + supersededBy ref |
| `package_dependencies` | (item_hash, depends_on_hash, depends_on_item_type, owner, modules, name). 4 indexes incl. partial idx_..._depends_on_location for the propagation query "who depends on this location?" |

### Traces

| Table | Role |
|---|---|
| `traces` | one row per handler invocation. `account_id` optional (NULL = anonymous). |
| `trace_fn_calls` | one row per fn call AND lambda invocation. Linked via `parent_call_id`. `args` + `result` are binary-serialized Dvals. |

### Legacy/transitional

| Table | Role |
|---|---|
| `user_data_v0` | user DB rows; pre-cutover shape |
| `toplevels_v0` | legacy toplevels (pre-package-ops world) |
| `scripts_v0` | one-off scripts |

The `_v0` suffix is the legacy-naming convention. The post-cutover
tables (`branches`, `commits`, `package_*`, `locations`,
`deprecations`, `package_dependencies`, `traces`, `trace_fn_calls`)
don't carry the suffix. Net: 12 post-cutover + 6 legacy.

## New tables proposed across substrate sketches

Each substrate-sketch deepening called for new tables. Inventory
+ source doc + sketch grade:

| New table | From | Schema sketched? |
|---|---|---|
| `account_identities` | IDENTITY (T11-T13) | yes — multi-binding to external identities (Tailscale/OAuth/token) |
| `delegations` | IDENTITY (T11-T13) | yes — ownerId/agentId/caps/scope/expires/revoked + parentDelegation |
| (column add) `package_ops.delegation_id` | IDENTITY | yes |
| `conflicts_v0` | CONFLICTS (T14) | yes — content-addressed; kind + payload_blob + status |
| `conflict_resolutions_v0` | CONFLICTS (T14) | yes — 1:1 with conflicts; outcome + decided_by_rule + decided_by |
| `capability_grants_v0` | CAPABILITIES (T15) | yes — account_id + capability + scope + revoked + expires |
| `capability_log_v0` | CAPABILITIES (T15) | yes — append-only audit |

7 new tables + 1 column add. Conservative ratio: **18 → 25 tables**.

## Cross-table relationships

The substrate graph (cross-table FK + non-FK references), with
new tables marked `*`:

```
accounts_v0 ──────┬─→  commits.account_id
                  ├─→  package_ops (via delegation_id*)
                  ├─→  delegations.ownerId / agentId  *
                  ├─→  account_identities.account_id  *
                  ├─→  capability_grants_v0.account_id  *
                  ├─→  capability_log_v0.account_id  *
                  ├─→  conflicts_v0.detected_by  *
                  └─→  conflict_resolutions_v0.decided_by  *

branches ─────────┬─→  commits.branch_id
                  ├─→  package_ops.branch_id
                  ├─→  locations.branch_id (via FK chain)
                  └─→  capability_log_v0.branch_id  *

commits ──────────┬─→  package_ops.commit_hash (NULL = WIP)
                  └─→  branches.base_commit_hash

package_functions ─→  locations.item_hash
package_types ─────→  locations.item_hash
package_values ────→  locations.item_hash
package_blobs ─────→  (no inbound; sweep-orphans)

package_dependencies ←─ from package_*; ─→ to package_* (the dep graph)

delegations *  ───→  package_ops.delegation_id  *
                ──→  conflict_resolutions_v0 (via decided_by audit)

conflicts_v0 *  ──→  conflict_resolutions_v0.conflict_id  *
              ←──  capability_log_v0.conflict_id  *
```

## Invariants worth maintaining

- **One table per concern.** Don't bolt schemas onto `accounts_v0`
  for everything; new tables for new concerns (already the
  pattern with `_v0` suffixed legacy + post-cutover separate
  shape).
- **Content-addressed where possible.** `package_*` already
  content-addressed; conflicts_v0 + branch_ops follow.
- **Append-only logs separate from mutable state.** `capability_
  log_v0`, `traces` are append-only. `capability_grants_v0`,
  `locations` mutate (revoke / unlist).
- **NULL conventions.** `commit_hash NULL = WIP`. Branch-scoped
  tables: NULL `branch_id` = merged/main-only. Maintain.
- **Indexes match query shape.** The existing partial index on
  `package_dependencies.depends_on_location` is the pattern —
  expensive joins get partial indexes.
- **Kill-and-fill stays the migration model.** Don't introduce
  incremental migrations that the substrate then drifts from.
  Schema.sql is canonical; bump the hash, replay.

## Open decisions on schema

- **(Q-sch-1) Sync table for cross-instance state.** Should we
  track per-peer `last_sync_sequence` in a table? Likely yes; one
  more table.
- **(Q-sch-2) Event-bus persistence.** `EventBus` durability
  needs its own table (or piggyback on `package_ops` /
  `branch_ops` / `conflicts_v0` depending on bus type). Likely:
  most buses don't get a dedicated table; durable buses reuse
  existing tables.
- **(Q-sch-3) `_v0` suffix retirement.** Legacy tables
  `user_data_v0` / `toplevels_v0` / `scripts_v0` — when do they
  go? Tied to bootstrap Phase 1 (`.dark` files retire) and
  potentially Phase 2 (toplevels become normal package items).
- **(Q-sch-4) Audit-log retention.** `capability_log_v0` will
  grow forever. Retention policy? Compress old rows to a
  digest? Sweep on time-based expiry? Probably keep forever
  for now; revisit at scale.
- **(Q-sch-5) Cross-instance schema versioning.** When two
  peers have different schema versions, how does sync degrade?
  Probably: reject events that target tables newer than the
  receiver knows. Bootstrap upgrade resolves it.
