# design/ — index

Durable design docs, grouped by the six themes (also the reading/priority order).
The keystone is [distributed-event-sourcing.md](distributed-event-sourcing.md) — read it first.

## 1. Stable & Syncing (priority)

- [distributed-event-sourcing.md](distributed-event-sourcing.md) — the keystone: ops, projections, the thin `App` type
- [event-bus.md](event-bus.md) — the op/event substrate + frame parking
- [conflicts.md](conflicts.md) — reconciliation, organized by evaluation-time
- [sync.md](sync.md) — wire protocol + sharing over Tailscale
- [async.md](async.md) — kill Task/Ply, explicit parking
- [cli-daemon.md](cli-daemon.md) — the resident host for the live substrate
- [hot-reload.md](hot-reload.md) — one consumer of `BodyChanged` events
- [cohabitation.md](cohabitation.md) — the multi-actor mental model
- [remote-access.md](remote-access.md) — remote-control as a mode over the same stack
- [package-system-layers.md](package-system-layers.md) — layers as composable ops/apps/projections

## 2. Removing .dark files (punted)

- [bootstrap.md](bootstrap.md) — the blockers, consolidated; punted until sync + stability land

## 3. PDD (resting)

- [pdd.md](pdd.md) — thin overview (spike status)
- [algorithm.md](algorithm.md) — the materialization/dispatch sketch
- [claims.md](claims.md) — what PDD is trying to prove
- [pdd-elevator-pitches.md](pdd-elevator-pitches.md) — the pitch

## 4. Capabilities & Identity

- [capabilities.md](capabilities.md) — per-category effect permissions
- [identity.md](identity.md) — the `Identity` model + intent

## 5/6. Apps & editing software

- [composable-mvu.md](composable-mvu.md) — MVU on top of the `App` type
- [view-sketches.md](view-sketches.md) — the fn viewer, extended
- [structural-editor.md](structural-editor.md) — projectional ProgramTypes editor
- [dark-virtual-files.md](dark-virtual-files.md) — Dark state projected as a filesystem (distinct from removing `.dark` files)

## Research / comparisons

- [research/beam-vs-dark.md](research/beam-vs-dark.md)
- [research/swamp-vs-dark.md](research/swamp-vs-dark.md)
- [research/visibility-vs-dark.md](research/visibility-vs-dark.md)
