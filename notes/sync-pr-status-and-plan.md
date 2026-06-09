# Stable & Syncing — status & remaining

**Done, green, pushed** to `github/syncing-again` (loop-fun), 8 commits on current `main`. Full backend
suite green. Not squashing — keeping the commits granular for review.

## What's in it

- **Ops ⊥ projections** — op log canonical; the package tables are regenerable projections;
  `rebuildProjections` + `projectionRegistry`; surfaced as `dark status` / `dark branch rebuild`.
- **Conflict-dispatch (the resolution spine)** — `ExecutionState.conflictDispatch`, default fail-loud =
  byte-identical build. Now wired in **two** places: the interpreter's missing-fn path (teed up for
  fetch-on-miss) and sync's divergence routing — so it's shared infrastructure, not a sync appendage.
- **Sync engine** — op log + per-peer cursors + the latest-op-wins fold; idempotent apply; race detection
  + a reviewable `sync_conflicts` record; peer registry. File + HTTP-over-Tailscale transports. Exact
  same-millisecond ties break deterministically by content hash (portable → every machine + a rebuild
  converge); local sequential edits never tie (each gets a strictly-increasing local stamp).
- **Conflict resolution** — a SetName race auto-resolves to whichever op was written later; recorded for
  review. You `ack` to OK it or `resolve mine|theirs` to override — and an override is itself an op that
  rides sync, so peers converge on your choice. Pure Dark `Sync.Display.conflictReport` (LWW-framed,
  winner-marked, ack inlined), surfaced at pull → status → `dark conflicts`.
- **Coverage** — every `PackageOp` kind rides sync (`applyOp` has no wildcard; `opsSince` no filter).
  Release CLI: two instances converge via file pull.
- **Built to grow** — `Conflict` is an open meta-model; the coming cases are named (`CMoveCollision`,
  `CValueUpdateRace`, `CCapabilityDenied`) for MoveItem/MoveModule + long-lived mutable value updates.
  Design in `sync-future-ops.md`.
- **CLI app sketches** (render-only) — Explorer / TreeView / Repl with inline sync/lifecycle badges,
  toward a multi-view CLI. Vision in `cli-ux-redesign-vision.md`.

## Open questions (your call — don't block the merge)

1. **Migration kill-and-fill** of `package_ops` — pre-existing; leave as-is, or make data-preserving?
2. **Commit-only vs WIP pull** — gate `sync pull` to committed ops only (`opsSinceCommitted` exists)?

## Remaining

- [ ] **(your go)** review the diff on the fork, then open the PR.
- [ ] Later: HTTP/Tailscale two-instance live demo; wire the app sketches interactive; route the other
      constraints (merge / propagation / deprecation) and fetch-on-miss through the dispatch spine; make a
      received override **auto-ack** the matching race on the peer (today it converges, but the peer's own
      record isn't cleared automatically).
