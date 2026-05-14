# pdd-thinking/archive — historical artifacts

Working notes from the spike that have been *superseded* by the
reflection-layer docs in the parent directory. Kept for tracing the
provenance of decisions; not needed to understand the current state.

## Three waves of archival

### Wave 1 (mid-spike, ~2026-05-13 midday)

Numbered iter-by-iter notes (`00-` through `20-`) plus the day-1
session reports. These are the early-spike planning docs that drove
the initial type-system work.

- `00-LOOP-SUMMARY.md`
- `09-carving-the-codebase.md`, `10-day-1-hacking-plan.md`
- `13-libpdd-materializer.md`
- `17-day-1-quick-reference.md`, `18-minimum-viable-spike.md`
- `20-elevator-pitches.md`
- `CODING-LOOP.md`, `progress.md`
- `FINAL-REPORT-2026-05-13.md`, `SESSION-2-REPORT-2026-05-13.md`

### Wave 2 (spike-end, ~2026-05-14)

Session reports + the SCM-INTEGRATION sketch. Superseded by the
reflection layer (WRAP-UP.md, SPIKE-LEARNINGS.md, INTEGRATION-PLAN.md,
BIG-PICTURE.md, F-SHARP-TO-DARK.md).

- `REPORT-state.md` (2026-05-13 evening — ~80 commits past main)
- `REPORT-thoughts.md` (companion to state — architectural decisions)
- `REPORT-overnight.md` (2026-05-13/14 night — the 7-iter overnight arc).
  Holds the original framing of the **PackageID-on-promote
  forwarding** question. That discussion is now lifted into
  `../INTEGRATION-PLAN.md` §"Cross-cutting decisions" Decision 2.
- `REPORT-morning.md` (2026-05-14 ~2:30am wake-up brief — 126 commits)
- `SCM-INTEGRATION.md` — the "ID until commit, then hash" pivot.
  The insight (PackageID = working copy; Package(hash) = commit;
  `promote` = boundary) is captured live in the code and in
  `../WRAP-UP.md` (the lifecycle + Decision 2 forwarding-semantics
  discussion).

### Wave 3 (consolidation, ~2026-05-14)

The per-topic reflection layer (SPIKE-LEARNINGS, INTEGRATION-PLAN,
BIG-PICTURE, F-SHARP-TO-DARK) was distilled into a single dense
`../WRAP-UP.md`. The originals live in `reflection-layer/` for
anyone who wants the expanded form.

- `reflection-layer/SPIKE-LEARNINGS.md` — what worked / didn't / surprises
- `reflection-layer/INTEGRATION-PLAN.md` — 3-wave merge plan
- `reflection-layer/BIG-PICTURE.md` — horizon scan
- `reflection-layer/F-SHARP-TO-DARK.md` — self-hosting roadmap

`WRAP-UP.md` is the canonical version; these are the source material.

## When to read these

- Tracing where a specific design choice came from (e.g., why is
  `FQFnName.PackageID` shaped the way it is? → `SCM-INTEGRATION.md`).
- Understanding the historical sequence of the spike's reasoning.
- Otherwise: don't. The current docs subsume them.
