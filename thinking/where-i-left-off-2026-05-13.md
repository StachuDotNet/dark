# Where you left off — 2026-05-13

## Backup branch

**Branch:** `aot-and-cold-start-improvements`
**Pushed to:** `origin/aot-and-cold-start-improvements` (StachuDotNet/dark)
**Backup commit:** `832d20b99` — *wip: back up planning docs + AOT branch loose ends before PDD experiment*

The working tree is clean. Everything you had in flight is on that branch, both committed and pushed. Safe to pivot.

## What that branch was about

The AOT + cold-start branch. Recent commits, in order, from when you picked it up:

- `2ef0a59` — ci: revert to JIT publish; add JIT size-breakdown doc
- `813d526` — fix: bind UUIDs as TEXT, not BLOB, in raw-Sqlite playback
- `83418522a` — docs: comprehensive progress report on the AOT + cold-start branch
- `e6a252b1` — ci: build SQLite static archives + AOT-publish on PRs
- `6873bf21` — build: drop unused FsRegEx + System.Diagnostics.DiagnosticSource refs
- `a41741f9` — perf: cache prepared SqliteCommands across the playback batch
- `a526c12c` — aot: unsuppress trim/AOT warnings so latent issues surface
- `bd9fb989` — aot: kill sprintf "%.Ng" float formatters
- `2298832d` — docs: AOT progress + status reports
- `c615e527` — size: drop FSharpPlus / FSharpx.Extras / FSharp.STJ / NodaTime.STJ
- `a7b6a11b` — perf: rewrite package-op playback against raw Sqlite; one tx for cold start
- `474593fe` — build: silence NU1510 (unprunable transitive package refs)
- `375fe5b2` — aot: static-link libe_sqlite3 + tighten AOT subsystem switches
- `064eb0e4` — aot: build SQLite static archives via zig cc for static-link

## What was uncommitted (now folded into 832d20b9)

**Code edits:**
- `backend/testfiles/execution/stdlib/nomodule.dark` — small test cleanup (-6 lines)

**Thinking/planning docs (most of these were untracked before — now safely on the branch):**
- `thinking/cli-aot-progress-2026-05-12.md` — heavy rework of AOT progress notes
- `thinking/advisor-call-additions.md` + `thinking/advisor-call-export.md` — advisor-call follow-ups
- `thinking/build-speed-refactor.md` — dev-cycle speedup planning
- `thinking/data-architecture-rewrite.md` (+ `-summary`, `-unified-model`, `-networking`) — a four-doc series planning the data-architecture rewrite
- `thinking/db-create-replace-attr.md` — DB attr design
- `thinking/parser-generator-from-types.md` — parser-from-types idea
- `thinking/pseudocode-driven-dev-2026-05-13.md` — the synthesis report we wrote tonight

## Where you stopped

Tonight's session ended at the point of:
1. Synthesizing your PDD notes into one report (the `pseudocode-driven-dev-2026-05-13.md` doc).
2. Deciding to pivot to a fresh experimental `pdd` branch off main and hack on a JIT-code/PDD prototype directly in this F# repo.
3. The AOT branch itself was *not blocked* — it's in a fine paused state, with the most recent commits being CI/build polish (`ci: revert to JIT publish`, `fix: bind UUIDs as TEXT`).

## What you wanted to do next on the AOT branch (when you come back to it)

I'm inferring from your `thinking/cli-aot-progress-2026-05-12.md` and the recent commits:

- The JIT-vs-AOT decision is currently *reverted to JIT* in CI (commit `2ef0a59`). There's a JIT size-breakdown doc that argues the case. The bigger AOT push is paused, not abandoned.
- The cold-start rewrite (raw Sqlite playback) seems to be working — most recent perf commits were polish on top of it.
- Outstanding-looking thinking docs that suggest follow-up work: `build-speed-refactor.md`, `data-architecture-rewrite.md` series. Neither has obviously been started in code.

## Where to find tonight's PDD work when you wake up

- **New branch:** `pdd` (off `main`, local-only, **never pushed**)
- **Notes dir:** `notes/pdd/`
- **Loop summary:** `notes/pdd/00-LOOP-SUMMARY.md` — start there
- **Final report:** `notes/pdd/FINAL-REPORT-2026-05-13.md` — printed in the morning, will also be on your physical desk

## To resume the AOT branch later

```bash
git checkout aot-and-cold-start-improvements
```

Everything's there. Pushed copy on origin as a safety net.
