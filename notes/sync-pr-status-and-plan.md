# Stable & Syncing — status & remaining

**Done, green, pushed** to `github/syncing-again`. The full "stable & syncing" floor (loop-fun):
`+5,336 / −50`, 7 commits on current `main`. Full backend suite **9,750 passed / 0 failed**.

## What's in it

- **Ops ⊥ projections** — op log canonical; 5 regenerable projection tables; `rebuildProjections` +
  `projectionRegistry`; surfaced as `dark status` / `dark branch rebuild`. (`OpsProjections` 5/5.)
- **Conflict-dispatch seam** — `ExecutionState.conflictDispatch` hook, default `FailLoudly` =
  byte-identical build; the place every "can't proceed, here are the options" routes. (`ConflictDispatch` 4/4.)
- **Sync engine** — op log + per-peer cursors + timestamp-LWW fold; idempotent apply; divergence
  detection + `sync_conflicts` record; peer registry. File + HTTP/Tailscale transports.
- **Divergence routing** — sync emits each divergence through the dispatch seam as a first-class
  `CSyncDivergence`; default behavior unchanged, a sync policy can keep-local. (`SyncScenarios` 8/8.)
- **Conflict-resolution UX** — structured builtin + pure Dark `Sync.Display.conflictReport`:
  last-write-wins framed, winner-marked, the exact `ack <id>` inlined; surfaced at pull → status →
  `dark conflicts`. Nothing silently lost. (`conflicts-list.dark` 19.)
- **Coverage proven** — every `PackageOp` kind rides sync (`applyOp` has no wildcard; `opsSince` no
  filter; propagation rides via companion `SetName`s). Release CLI: two instances converge via file pull.
- **CLI app sketches** (render-only) — Explorer / TreeView / Repl, toward a multi-view CLI. Vision in
  `cli-ux-redesign-vision.md`. (`apps-sketches.dark` 18.)

## Open questions (your call — don't block the merge)

1. **Migration kill-and-fill** of `package_ops` — pre-existing; leave as-is, or make data-preserving?
2. **Commit-only vs WIP pull** — gate `sync pull` to committed ops only (`opsSinceCommitted` exists)?
3. **LWW tie-break** — accept application-order resolution for the rare same-millisecond cross-instance tie?

## Known edge (non-blocking)

Two *keep-local* resolutions in one `routeDivergences` pass: the second re-fold can fail to flip. No
keep-local policy ships (default is `FailLoudly`); the shipped multi-divergence path (LWW) converges and
is tested. Revisit if/when a keep-local policy lands.

## Remaining

- [ ] **(your go)** squash the WIP commits into the sync commit, then `gh` push + open the PR.
- [ ] Later: HTTP/Tailscale two-instance live demo; wire the app sketches interactive; unify the other
      constraints (merge / propagation / deprecation) through the dispatch seam.

## Pointers

- Branch `syncing-again` (loop-fun), base `github/main`. Prework branches: `ops-projections`, `conflict-dispatch`.
- PR description / commit message: `sync-pr-description.md`. Code-diff report: `sync-pr-code-diff.md`.
