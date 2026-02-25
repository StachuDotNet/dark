# Phase 3 — Bootstrap Artifact (future PR)

**Punted from the hashing PR.** Needs more consideration around sync
mechanics, upstream `.db` updates, etc. The hashing work in Phases 1-2
is a prerequisite; bootstrapping builds on it.

The payoff: a `.db` that doesn't need to be regenerated from `.dark`
files on every startup. Parse once, produce a deterministic DB, ship it.

- [ ] Verify determinism: same .dark files -> identical .db every time
- [ ] Add `generate-seed-db` command to LocalExec.fs
  - Runs the full parse pipeline, outputs a `.db` file
  - Can be called from CI to produce release artifacts
- [ ] Add seed DB to CI release pipeline (upload alongside CLI binary)
- [ ] CLI startup: detect missing `data.db`, offer to use seed DB or
  clone from server
- [ ] Back up a known-good .db for reference during development
- [ ] Simplify `stabilizeOpsAgainstPM` (likely unnecessary now that
  hashes are deterministic — same content = same hash regardless of
  parse order)
- [ ] Simplify `LoadPackagesFromDisk.fs` two-pass pipeline if possible
- [ ] Eliminate purge-and-reload cycle: startup reads existing `.db`
  instead of re-parsing `.dark` files from disk
- [ ] Long-term: eliminate `packages/` directory as a runtime dependency
  (only needed for seed DB generation in CI)
- [ ] Tests:
  - F# (`Tests/`): two independent full-parses produce identical `.db`
  - Darklang (`testfiles/`): CLI startup from seed DB, package resolution
