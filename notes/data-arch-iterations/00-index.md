# Data architecture iteration log

Stachu asked me to think about the data-management reorg in a loop
overnight (every 15 min, walking back to him in a few hours).
Source material lives in `~/code/dark/main/thinking/`:

- `data-architecture-rewrite.md` (1189 lines) — the big "split into
  master ops + projections + daemon" proposal.
- `data-architecture-rewrite-summary.md` (70 lines) — one-pager.
- `data-architecture-unified-model.md` (476 lines) — "everything is
  ops/projections/conflicts/resolutions" reframe.
- `data-architecture-networking.md` (635 lines) — replaces § 10 of
  the rewrite with hub-based identity / sync / discovery.

What this log is for: **iterate**. Not summarize. Each entry
attacks a question the existing docs leave open, or sharpens a
piece, or imagines failure, or pushes the "less F#, more Dark"
discipline.

I'll number entries `NN-<topic-slug>.md`. Don't expect them to
form a clean narrative arc — they're a thinking trail, not a
deliverable. By the morning there should be ~12-16 entries
totaling a few thousand lines.

## The thesis I'm pushing on

The existing docs already arrive at a strong position:

1. Master `ops.db` — append-only, content-addressed, only thing
   that syncs.
2. Per-(stream, key) projections — disposable SQLite files,
   rebuilt by replaying ops.
3. A daemon owns projections + supervises apps + does sync.
4. Sessions, traces, app data, account changes — all become op
   streams under the same machinery.
5. A hosted hub ("dark.run") brokers identity, presence, and
   sync-relay so users don't think about networking.

What the docs *don't* fully resolve, in roughly the order I'll
attack:

- The op binary format itself — what's the wire shape, what's the
  on-disk shape, are they different, how do versioned op shapes
  evolve.
- "Less F#" — which of today's F# subsystems migrate to Dark, in
  what order, and what stays in F# forever.
- Per-stream ACL: who can append, who can read, how are grants
  revoked, what happens to ops written under a now-revoked grant.
- The conflict-resolution UX in concrete terms — not "we'll have
  a conflicts list," but "here are the screens / commands / fns."
- Bootstrap from absolute zero — `dark login` then what, exactly,
  in milliseconds.
- The performance budget: what's the daemon's RAM at idle, at
  100-app load, at 10K-trace/sec; what's the SQLite write
  amplification look like.
- Failure modes: ops.db corruption, projection rebuild storms,
  hub partition, malicious peers, bricked tokens.
- Test infrastructure for a world without a global DB — every
  test needs its own DARK_ROOT.
- The migration from today's state to the new world — slice by
  slice, what's the minimum-viable diff.
- The LSP / VS Code story.
- The interactive shell story (a daemon-aware REPL).
- A design for the "schema as Dark code" idea — projections
  expressed as Dark fns, run by the daemon.

Each one is one or more entries. Some will be short (problem +
recommendation), some long (problem + design + critique +
revision).

## Iter log

- 01 — op binary format on the wire and on disk
- 02 — less F#, more Dark (the migration order)
- 03 — per-stream ACL & grants (revoke modes, fork)
- 04 — conflict resolution UX (lenses, sidebar, fns)
- 05 — bootstrap from zero (`dark login` → ms-by-ms)
- 06 — performance budget (RAM, write amp, trace pps)
- 07 — failure modes (corruption, partition, poison ops)
- 08 — concrete migration plan (slice-by-slice PRs)
- 09 — LSP / editors (daemon as the LSP server)
- 10 — interactive shell (`dark repl`, app-attach, notebook export)
- 11 — projections-as-Dark-code (user-defined materialized views)
- 12 — multi-tenant hosting (dark.run as peer, not server)
- 13 — time-travel debugging (replay any trace, edit & re-run)
- 14 — test infrastructure (scoped accounts, multi-peer tests, trace regressions)
- 15 — package ecosystem (search, publish, fork, deprecation propagation)
- 16 — per-stream encryption (tiers 0-2, key management, what dark.run sees)
- 17 — synthesis (cross-cutting threads, load-bearing pieces, recommended order)
- 18 — deployment pipeline (sub-second deploys, instant rollback, canary, PR previews)
- 19 — backups & disaster recovery (replication-as-backup, op-log revert, recovery rituals)

## Pacing

ScheduleWakeup 15-min ticks. Cache TTL is 5 min so I'll burn it
each cycle — that's fine for thinking work. If I find myself
just churning the same ground, I'll skip the wakeup and stop.
