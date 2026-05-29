# Package Bootstrapping — Removing `.dark` Files

The user's first explicit itch. Produced by loop TODOs T3-T6.

## Headline finding

**Most of the mechanism already exists on `main`.** The seed-DB
extract+grow flow is in `backend/src/LibDB/Seed.fs`. The kill-and-
fill schema model is in `backend/src/LocalExec/Migrations.fs`. The
remaining gap is **decoupling the *runtime path* from the legacy
`LoadPackagesFromDisk.fs` .dark-file parser** — and the *build
pipeline*'s role in producing canonical seeds.

This is much less work than the sketches implied. The bootstrap
milestone is closer than it looked.

---

## Current state (T3)

### How a Darklang instance starts today (on `main`)

1. **Schema bootstrap.** `LocalExec.Migrations` hashes
   `backend/migrations/schema.sql`, compares to
   `schema_state_v0.hash` in the existing DB. If they differ (or
   the DB is empty), drop every non-system table and replay the
   schema file fresh ("kill-and-fill"). Then run any new
   `migrations/incremental/*.sql` files.
2. **Load packages from disk.** `LoadPackagesFromDisk.load`
   reads every `.dark` file under `/home/dark/app/packages`, parses
   each via `LibParser.Parser.parsePackageFile`, runs a 2-phase
   convergence loop (initial parse with unresolved names allowed,
   then iterative re-parse until hashes converge — typically 2–3
   passes, capped at 50), produces a `List<PT.PackageOp>`.
3. **Apply ops via `PackageOpPlayback`** into the SQLite tables
   (`package_functions`, `package_types`, `package_values`,
   `locations`, etc.).
4. **Grow the seed (if applicable).** `LibDB.Seed.growIfNeeded`
   applies any unapplied ops to rebuild projection tables. Single
   fast SELECT COUNT if everything's applied; expensive only the
   first time.

### What's good about this

- The schema kill-and-fill is correct and tidy.
- `LibDB.Seed.export` is *already* the snapshot-export side:
  "copy data.db, strip derived data, VACUUM."
- `LibDB.Seed.growIfNeeded` is *already* the snapshot-import side:
  apply unapplied ops, rebuild projections, run automatically on
  startup.
- The builtin `pmSeedExport` exposes the export step to Dark
  code (so `dark` itself can produce seeds).

### What's not good

- `.dark` files in the repo are the **canonical source** today.
  CI/build re-parses them every run. This is the bit the user
  wants gone.
- `LoadPackagesFromDisk` is in `LocalExec`, which couples
  bootstrap to the parser and to F#-side seeding logic. The
  *intent* is that nobody runs this path post-cutover; the seed
  DB ships with everything already applied.
- 400+ `.dark` files in `packages/` plus 74 in `backend/testfiles/`.
  Their fate differs: package files become seed data; test files
  stay (they're test inputs, not source).

### Target state

A new Dark instance bootstraps from a **content snapshot** (seed
DB) that already has all package ops applied. No `.dark` parse on
first run. `LoadPackagesFromDisk` exists only as a *seed-builder*
tool used by CI/release, never by end-user installs.

Concretely:

- The release pipeline produces a `seed.db` (or `data.db`, name
  TBD) with all canonical package state applied + ops marked
  applied + derived projections precomputed.
- `dark install` (or first-run) downloads/copies that seed into
  the user's local data location.
- Subsequent boots only run `growIfNeeded` (cheap — should be
  near-instant).
- `LibParser` is loaded only when the *editor* (not bootstrap)
  needs it. The bootstrap path doesn't need a parser at all.

---

## Snapshot format (T4)

A "snapshot" is the SQLite `seed.db` file. Conceptually:

```
seed.db (what ships)
  ├─ system_migrations_v0   — bookkeeping
  ├─ accounts_v0            — pre-allocated UUIDs for Darklang/Stachu/etc.
  ├─ branches               — main branch pre-seeded
  ├─ commits                — the "release commit"(s)
  ├─ package_ops            — all ops, applied=1 (so growIfNeeded is a no-op)
  ├─ branch_ops             — branch creation ops, applied=1
  ├─ package_functions / types / values / blobs
                            — derived projections, populated
  ├─ locations              — name → id, populated
  ├─ deprecations           — populated
  ├─ package_dependencies   — populated
  └─ (excluded) traces / trace_fn_calls / user_data_v0 / scripts_v0
                            — user-specific; not shipped
```

The current `LibDB.Seed.export` strips **derived data + archived
branches**; the importing user runs `growIfNeeded` to rebuild
projections. We can either:

- **Option A** — strip derived data on export, regrow on import.
  Smaller snapshot file; install does a bit of work. (Current
  behavior.)
- **Option B** — ship with derived data already populated. Larger
  snapshot; install is near-instant. The seed DB *is* the
  running DB; no growIfNeeded work to do.

Recommendation: **start with Option A** (matches today), measure
the grow time. If it's noticeable (>1s on a release-sized seed),
switch to Option B for shipped seeds.

### What ships in the canonical seed

- All `Stdlib.*` ops + projections — this is the language.
- `Darklang.*` ops — internal/dev/cli/lsp packages.
- *Not* user-namespace packages (`User.Stachu.*`, etc.) — those
  arrive via sync, not bootstrap.
- *Not* test-only ops — they're test inputs, separate flow.

### Distribution

Per `~/vaults/Darklang Dev/05.Implementation/Package Bootstrapping/dl-bootstrapping.md`:

> matter.darklang.com hosts /data.db (or /seed.db) and /lastUpdated
> or /hash.

The release pipeline pushes `seed.db` to `matter.darklang.com`.
First-time install fetches it via Tailscale-serve-style URL (per
the Tailscale doc) or plain HTTPS. The content hash is what
versions the snapshot.

### Open decision: signing / trust

A seed is privileged code — everything in `Stdlib` is implicitly
trusted. Two options:

- **Trust transport** (HTTPS + matter.darklang.com hardcoded as
  the canonical source). Simple. Works if you trust the server +
  CA.
- **Sign the seed.** A Darklang signing key signs the seed-hash;
  install verifies. Doesn't trust the server; defends against
  CDN compromise.

Recommendation: **(a) for v1, (b) for v2.** Signing is a real
add-on that needs key mgmt + key distribution. Don't ship it as
a hard dependency.

### Upgrade migrations

When a `dark` binary is upgraded:

- If the new binary's expected schema-hash differs from the
  installed DB's: run `LocalExec.Migrations` (kill-and-fill +
  incremental migrations).
- If the new binary expects newer package versions: fetch the
  delta as events (per `SYNC-AND-STABILITY.md`), not a fresh
  seed.

The seed is **only used for first-time install + dev-reset**. After
that, the event stream handles updates. Same model as git: clone
once, fetch updates.

---

## Sequencing the work (T5)

Ordered named work-units, each shippable independently. Use these
as branch names / PR titles.

### Pre-work (likely already done or trivial)

- **bootstrap-1: audit current usage of `LoadPackagesFromDisk`.**
  Confirm: it's only called from `LocalExec.LocalExec.fs` and
  (test-mode only) from `TestModule.fs`. **Done** — confirmed
  during T3 recon.

- **bootstrap-2: confirm `LibDB.Seed.export` produces a
  reload-able file.** Run it; copy the output; spin a new
  instance from the copy; verify everything works. (May already
  be tested; check.)

### Core: decouple end-user bootstrap from `.dark` parsing

- **bootstrap-3: separate "build seed" from "run instance".**
  Add a CLI mode `dark build-seed --output seed.db` that runs
  `LoadPackagesFromDisk` + applies the resulting ops + calls
  `LibDB.Seed.export`. The end-user runtime path no longer calls
  `LoadPackagesFromDisk`.

- **bootstrap-4: package the seed with releases.** CI builds the
  seed via `bootstrap-3`. The build artifact is the seed file +
  the binary. Both ship together (initially via release-asset;
  later via matter.darklang.com).

- **bootstrap-5: detect "needs install" at startup.** If the
  data dir is empty, copy the bundled seed into place. Otherwise
  open the existing DB (existing behavior).

### Cleanup: move `LoadPackagesFromDisk` out of the runtime path

- **bootstrap-6: relocate the parser-based loader.** Move
  `LoadPackagesFromDisk` from `LocalExec` to a separate
  `LibBuildTools` (or similar) that's only linked into
  `bootstrap-3`'s `build-seed` mode, not into the regular
  runtime.

- **bootstrap-7: delete `packages/*.dark` from the repo.** Move
  the canonical state to the seed file. The `.dark` files become
  "historical input" preserved in a separate repo or branch (or
  archived), not source-of-truth. Test files (`backend/testfiles/`)
  stay since they're test inputs, not package source.

### Distribution

- **bootstrap-8: matter.darklang.com hosts the seed.** Server
  exposes `GET /seed.db` and `GET /seed.hash`. `dark install`
  fetches if not local. (Depends on the wire protocol from
  STABILITY-AND-SHARING T8.)

- **bootstrap-9: upgrade flow.** On version mismatch, prefer the
  event stream over a fresh seed; fall back to fresh seed if the
  delta is too large or if the local DB is corrupt.

### Optional / later

- **bootstrap-10: signed seeds (v2).** Per the open decision
  above. Deferred.

### Dependencies

```
bootstrap-1 ─ done (audit)
bootstrap-2 ─ done-or-quick (verify Seed.export round-trips)
   ↓
bootstrap-3 ─ separate build-seed CLI mode
   ↓
bootstrap-4 ─ CI produces seed + binary
   ↓
bootstrap-5 ─ first-run install detection
   ↓
bootstrap-6 ─ relocate LoadPackagesFromDisk to LibBuildTools
   ↓
bootstrap-7 ─ delete packages/*.dark from repo

bootstrap-8 ─ matter.darklang.com hosts seed (needs sharing T8)
bootstrap-9 ─ upgrade flow (needs sharing T8 + T9)
```

bootstrap-1 → 7 is **mostly local + sequential, ~1-2 weeks of
work** assuming no surprises. bootstrap-8 + 9 depend on the
sharing work in Phase C.

---

## Phase decision (T6)

**Bootstrapping ships in Phase 1 (steps 1-7) + Phase 3 (steps 8-9).**

- **Phase 1 — "Removing .dark files."** Lands bootstrap-1 through
  bootstrap-7. End state: `packages/*.dark` is gone from the repo;
  the runtime path never invokes LibParser; new installs do
  schema bootstrap + open the bundled seed. **No sharing
  dependency**; can be done in parallel with conflicts /
  capabilities / identity design. Realistic timeline: 2-3 weeks
  with focused effort.

- **Phase 3 — "Bootstrap from network."** Lands bootstrap-8 +
  bootstrap-9. Requires the sharing wire protocol from Phase C
  (T7-T10). End state: `dark install` fetches the seed from
  matter.darklang.com over the wire; upgrades use the event
  stream.

Cross-link: see `ROADMAP.md` §"Phase plan" once filled in (T24).

### Why this ordering

- Phase 1 is fully local — doesn't depend on identity, sharing,
  capabilities, conflicts dispatch, or the network wire format.
- Phase 1 alone gets the user their first itch (`.dark` files
  gone from repo). The repo gets simpler; CI gets faster; install
  no longer parses 400 files.
- Phase 3 is the "now strangers can install Dark from the
  internet" story. Needs the sharing work first.

---

## Open questions (carried for ROADMAP §"Open decisions")

- **(Q-bs-1) Snapshot file name + path.** `seed.db` vs `data.db`
  vs `dark.db`. Currently `data.db` for runtime, `seed.db` for
  export. Probably keep both names with that distinction.
- **(Q-bs-2) Option A vs Option B** for whether shipped seeds
  carry derived data. Default to A until measured.
- **(Q-bs-3) Versioning.** A seed is content-addressable
  (sha256 of the file); how does install know which version to
  fetch? Embed the version in the binary; binary fetches the
  matching seed. Probably right.
- **(Q-bs-4) Signing.** Deferred to v2.
- **(Q-bs-5) Test-only packages.** `backend/testfiles/*.dark`
  stay as `.dark` files (test inputs). Confirm: not in scope for
  removal.
- **(Q-bs-6) Fate of `packages/*.dark` post-removal.** Archive in
  a separate repo? Tag a final release before deletion?
  Documentation reference only? Lock decision before
  bootstrap-7.
