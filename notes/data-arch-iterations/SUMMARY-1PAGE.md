# Dark data architecture — overnight thinking, one page

19 iterations interrogating the rewrite-doc vision. The architecture holds up.

## The five forces that show up everywhere

1. **Content-addressing eliminates whole problem classes.** Lockfiles, dependency hell, trace-replay determinism, deploy rebuilds, multi-version coexistence — all evaporate when every fn / op / package is keyed by content hash.
2. **Daemon as the only client target.** CLI, REPL, LSP, editor, hub — all JSON-RPC clients of one daemon. One source of truth, many UIs, hot-swap uniformly.
3. **Sync as the data model.** Distributed-by-default; single-machine is the special case. Conflicts are first-class ops; resolutions are ops too.
4. **Hot-swap is the dominant interaction.** Edit-and-see-it across LSP, REPL, projections, traces, deprecation auto-migrate.
5. **Cost is for compute, not custody.** dark.run is a peer, not a server — structurally different SaaS economics; lock-in is honest.

## Recommended build order (after iter 08's 8 PR slices)

| Phase | Topic | LOC | Why |
|-------|-------|-----|-----|
| 1 | Test infrastructure (14) | 3K | Unblocks everything else |
| 2 | LSP daemonization (9) | 0.5K | Better dev UX everywhere |
| 3 | Projections-as-Dark (11) | 2K | Completes "less F#" thesis |
| 4 | At-rest encryption (16 t1) | 1.5K | Security baseline |
| 5 | Trace replay UI (13) | 1K | Edit-and-replay live |
| 6 | REPL (10) | 2.2K | Power-user feature |
| 7 | dark.run launch (12) | 5K | Hosted commercial |
| 8 | Hub ecosystem (15) | 5K | Community / discovery |
| 9 | E2E encryption (16 t2) | 1.5K | Compliance niche |

Total: **~22K LOC over ~12 months for 2-4 engineers.**

## Seven surprises

1. **Content-addressing eliminates lockfiles.** Multiple versions coexist; version pin = the lock.
2. **Trace replay falls out for free** from determinism + content-addressing.
3. **dark.run-as-peer simplifies HIPAA/GDPR** — tier 2 means we literally cannot read encrypted streams.
4. **Tests-as-ops give flake/trend dashboards for free** via projection-as-Dark.
5. **The LSP can be user-extensible** — register your own diagnoser fn, hot-swap.
6. **Edit-and-replay-live is genuinely new debugging UX** — production trace + new code, evaluated in seconds.
7. **The hub itself is a Dark app** — eat the dogfood; we feel every operational pain.

## The pitch

Dark is the language with the best dev loop on Earth. Edit → see effect → ship in seconds, not minutes. Production bugs come with full traces; replay them, edit the fix, re-run live, ship. No lockfiles, no dependency hell. Multi-peer tests tractable. End-to-end encryption a checkbox. Your data lives on your machine; dark.run is just a peer that runs while your laptop is closed.

## Risks (honest)

- **22K LOC over 12 months** for a small team; runway risk.
- **Bootstrap chicken-and-egg** — F# bootstrap layer is permanent surface area; bugs there are infectious.
- **Determinism leaks in builtins** quietly break trace replay; needs CI guardrails.
- **Sync correctness under partition** is CRDT-adjacent territory; subtle bugs emerge after long divergence.
- **Ecosystem cold start** — empty registry → no users; need anchor packages + community seed.
- **Pricing must beat free** (a user's own laptop is free); compute pricing has to land at a sweet spot.

## Gaps not deeply explored

Mobile delivery, observability dashboards, billing internals, i18n. Worth iters of their own; not architecturally blocking.

## Bottom line

The rewrite-doc vision is sound. The 19 iterations validated it from many angles, identified load-bearing pieces, and surprised me in good ways more than bad. Time to build.
