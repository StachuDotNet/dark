# Data architecture rewrite — one-pager

**Thesis.** `rundir/data.db` mixes four kinds of state with wildly different lifetimes (ops, projections, user-program data, traces). Crushing them into one file forces the worst of every world: one writer, one schema, one migration story, one backup unit, one sync unit. **Split along actual seams.**

## The split

```
~/.darklang/
  ops.db                                # MASTER — append-only, the only thing that syncs
  projections/<branch>/pkg.db           # disposable cache, rebuilt from ops
  projections/<branch>/traces.db        # ephemeral, schema-volatile, isolated
  apps/<app>/data.db                    # per-app userdata (val.town but better)
  sessions.db                           # local-only workflow state
  daemon.sock                           # darkd
```

| Layer       | Lifetime       | Source of truth | Syncs?   | Migration story        |
|-------------|----------------|-----------------|----------|------------------------|
| ops.db      | forever        | yes             | yes      | schema frozen          |
| projections | rebuildable    | no              | no       | drop + rebuild         |
| traces      | ephemeral      | no              | no       | drop + recreate        |
| app data    | per-app        | yes             | opt-in   | per-app, Dark-authored |
| sessions    | local workflow | yes             | no       | trivial                |

## What the daemon (`darkd`) gets us

- Second `dark` invocation is fast (no .NET startup tax).
- `branchChainCache` / `harmfulCache` and their never-invalidated TODOs delete — projections *are* the cache.
- Apps run as supervised processes alongside the CLI. `serve` stops blocking.
- A single place to host the sync agent, the trace recorder, and the LSP backend.
- `dark` becomes a thin RPC client over a unix socket; `dark --no-daemon` is the escape hatch.

## What dies

- Recursive-CTE branch-chain walk (materialized into `branch_chain` table).
- Two-phase `applied=false → applied=true` pattern in `Inserts.fs`.
- `WHERE branch_id IN (...)` filter on every projection query.
- `LibCloud` (already disabled-not-deleted; absorb survivors).
- Most of `WipRefresh.fs`.
- Per-tlid filter pattern in `user_data_v0` (each app has its own DB; `tlid` becomes the table name, not a column).
- Most migrations in `backend/migrations/` — trace redesigns and projection-shape changes become drop+rebuild.

## Sync, made boring

Star topology to one hub (`major`) over Tailscale. `tailscale serve --https` for transport, `Tailscale-User-Login` for auth. POST/GET on `package_ops`, idempotent on content-hash PK. ~200 lines of F#. Mesh and selective-sync come later.

## Phasing (each slice ships independently)

0. Move `data.db` → `~/.darklang/ops.db`. Pure rename.
1. Split traces out into `traces-global.db`.
2. Split userdata out into `apps/_default/data.db`.
3. Per-branch projection DBs for `main`; compare-in-CI; flip default.
4. All branches get projections. Master schema cleanup.
5. **Daemon ships.** CLI becomes a thin client.
6. Apps as a first-class concept. `serve` deprecates to `dark app start`.
7. Sessions.
8. Sync (HTTP endpoints in a Dark router; pull/push agent in daemon).
9. Per-app subprocess isolation.

## Biggest bet

Content-addressed ops + cheap projection rebuilds means **we can stop being precious about schema**. Today every schema change walks `data.db`. Tomorrow every schema change is "drop the projection." That changes how aggressively we can iterate on every layer above ops.

## Biggest open risks

- ATTACH-based parent-chain reads may be slower than the monolith with realistic data → fall back to denormalizing parent rows into each projection (cheap; locations are tiny).
- Two devices both editing `main` concurrently → rebase-before-push, last-writer-wins on names (definitions still exist at their hashes); CRDTs not needed in v1.
- Tests need per-process `DARK_ROOT` → small fixture in `TestUtils.initializeTestCanvas`.

Full doc: `thinking/data-architecture-rewrite.md`.
