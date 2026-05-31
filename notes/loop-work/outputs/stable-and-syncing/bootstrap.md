# Package Bootstrapping — Removing `.dark` Files

## Decision: PUNT

Removing the `.dark` files from the repo is **punted** until after baseline
sync works and the environment is stable. It is not realistic in the short or
medium term.

The reason is mutual reference. F# and Dark code reference each other tightly
and often: F# builtins are surfaced into Dark packages, and Dark packages are
loaded and called from F# at startup and at runtime. The `.dark` files are the
canonical source of that Dark side today, and the build pipeline re-parses them
every run. Pulling them out is not a contained refactor — it touches the
runtime load path, the build pipeline, local dev ergonomics, and CI. None of
that is worth doing before sync is real and the environment underneath it has
stopped moving.

So: this doc is no longer a sequenced work plan. It is the consolidated home
for the **blockers** to removing `.dark` files. We list them so that when the
prerequisites land, the work can be picked up with the hard parts already named.

> This is a separate concept from `dark-virtual-files.md`, which describes
> projecting Dark state *as* a filesystem (a view over live state). That stays
> its own document; it is not about repo bootstrapping and is not consolidated
> here.

---

## The hard part (lead with this)

The blocker that actually gates everything is **F# -> Dark -> F# mutual
reference under language/ops change**.

When the language or the op model changes, F# and Dark have to move together,
because each references the other:

- **Local dev.** A developer changes the language (parser, op representation,
  a core type) and also adds or edits package code and F# code that depend on
  each other. Today the `.dark` files are re-parsed locally so the two sides
  stay consistent on that one machine. If the `.dark` files are gone and the
  canonical state is a shipped seed, how does local dev keep the old packages
  running while the developer adds new package + F# code back and forth — with
  changes affecting **only the local machine** until they are ready? The seed
  was built against the *old* language; the developer is mid-flight on a *new*
  one. That gap is the unsolved problem.

- **CI.** CI faces the same coupling without a human in the loop. It must build
  F#, build (or fetch) the package state, and run them against each other — for
  a language version that may differ from whatever seed exists. Re-parsing the
  `.dark` files is exactly how CI sidesteps this today.

**Sequencing:** get sync working first, then worry about this. Assume a central
server. Once ops can be authored on one instance and replayed on another, and
once a developer can migrate a Dark environment forward without re-parsing the
repo, the mutual-reference problem becomes tractable — local dev pulls the
delta from the server, and CI builds against a known baseline rather than
re-parsing source. Until then, the `.dark` files are the cheapest way to keep
F# and Dark consistent across a language change.

**The concrete binding is `package-ref-hashes.txt`.** F# doesn't reference Dark items by name —
it references them by **content hash**, via `LibExecution.PackageRefs` (e.g.
`PackageRefs.Type.Stdlib.option ()`), which loads hashes from `package-ref-hashes.txt` (embedded
as a resource in the release binary). So the F#→Dark edge is literally *a file of hashes baked
into the build*. When the Dark op log changes a package's shape, its hash changes, the file must
regenerate, and **F# must rebuild** to pick up the new hashes — the "two-build dance" (build with
stale hashes → seed → regenerate → rebuild). This is why sync *alone* doesn't remove the coupling:
shipping a package update as ops changes the projections, which changes the hashes, which the
embedded file won't reflect until a rebuild. Two ways out, weighed when the time comes: **(a)** keep
the two-build dance but trigger it off **op-log changes** (a sync pull or an import) instead of a
`.dark` edit — minimal; or **(b)** make the F#→Dark binding **dynamic** — resolve hashes from the
seeded op-log DB at startup instead of embedding them — so a synced package update lands *without* an
F# rebuild, at the cost of a startup resolution step. (b) is the real "decouple F# from Dark source"
move; (a) is the pragmatic interim. Either way the hash file is the edge to manage.

See `sync.md` — sync is the prerequisite, not a parallel track.

---

## The blockers, enumerated

1. **Stable environment.** The environment underneath has to stop moving.
   Bootstrapping change layered on top of an unstable base just multiplies the
   surfaces that can break at once.

2. **Working sync.** Ops author on one instance and replay on another through
   the same op-playback path (see `sync.md`). Without this, there is no way to
   distribute or update package state except by re-parsing `.dark` files, which
   is the thing we want to remove.

3. **Migrations of Dark environments.** A developer must be able to migrate a
   Dark environment forward — **including upgrading core things like the
   language itself locally** — while still running against a backed-up or
   dev-ready DB of *old* package code. The new language has to load and operate
   on package state that was authored under the old one, without a full
   re-parse from source.

4. **F# <-> Dark mutual references when lang/ops change.** The hard part above.
   Local dev must keep old packages running while new package + F# code are
   added back and forth, affecting only the local machine until ready; CI must
   do the equivalent against a known baseline. This is the gating blocker and
   is solved only after sync + central server exist.

---

## The eventual sequence (once the blockers clear — start thinking now)

Not startable yet, but the ordered shape is worth naming so the work is ready when sync +
a stable env exist. Each step leans on a mechanism that already exists on `main`:

1. **Seed becomes canonical.** Build the corpus as a seed with all package ops already
   applied (`LibDB.Seed.export` already strips derived data + VACUUMs). Ship the seed, not
   the `.dark` files.
2. **First-run bootstraps from the seed, no parse.** A new instance loads the snapshot;
   `LoadPackagesFromDisk` stops running on the end-user runtime (`Seed.growIfNeeded` already
   reapplies unapplied ops + rebuilds projections on startup).
3. **Local dev pulls deltas from the server.** A dev changing the language pulls the migrated
   package state over sync instead of re-parsing — the step that *needs* working sync + env
   migrations (blockers 2, 3).
4. **CI builds against a known baseline.** CI loads the seed and builds F# against it rather
   than re-parsing source; `LoadPackagesFromDisk` survives only as the **seed-builder** for
   CI/release — the one place `.dark` parsing remains.
5. **Archive `packages/*.dark`.** The final removal (archive vs. tag-and-delete decided then).

The gating blocker (4 above — F#↔Dark mutual reference under language change) sits *under*
steps 3–4: until a dev can move the language forward locally without re-parsing, those steps
can't land. So the sequence is real but strictly downstream of [sync.md](sync.md).

**How the sync work plugs in (the loop into LocalExec seeding).** The prework sync primitives
*are* the seed-replacement machinery — the eventual sequence is mostly already coded, just not
wired to seeding yet. The meeting point is the **`PT.PackageOp` type**: today
`LoadPackagesFromDisk` produces `List<PackageOp>` from `.dark`; sync produces the same ops from a
peer. Both fold through the one `PackageOpPlayback` path.
- Step 2 "first-run bootstraps from the seed, no parse" **is `Sync.snapshot`** — the whole op log
  + a consistent bootstrap cursor, applied through the same fold `growIfNeeded` already runs.
- Step 3 "local dev pulls deltas" **is `Sync.opsSince(cursor)` + `applyRemoteOps`** — incremental,
  idempotent by op-id, resuming from the rowid cursor.
- Folding a pulled stream into a *fresh* store **is `connStore`/`dispatchVia`** — a seed is just an
  op stream folded into an empty projection DB (proven cross-store in LibPmSeam).
- Content the op stream doesn't carry (large `package_blobs`) rides the **blob channel**
  (`Blob.missing` → `getMany`, fetch-on-miss after the ops apply).
- `LoadPackagesFromDisk` survives as the **op *importer*** (`.dark` → ops, for authoring + the
  CI/release seed-builder), no longer the bootstrap source.

So "replace `.dark` seeding with sync" concretely = make the **committed/synced op-log snapshot**
the seed source (`Seed.export` already produces it), `.dark` parsing demoted to importer +
seed-builder — gated only by blocker 4 (the `package-ref-hashes.txt` binding above).

---

## What already exists (carried forward, not yet load-bearing)

These mechanisms are on `main` today and reduce the eventual work, but none of
them resolve the blockers above. Recorded here so the eventual effort starts
from what is real:

- **Schema bootstrap** — `LocalExec.Migrations` hashes
  `backend/migrations/schema.sql`, and on mismatch drops non-system tables and
  replays the schema fresh ("kill-and-fill"), then applies
  `migrations/incremental/*.sql`.
- **Disk load** — `LoadPackagesFromDisk.load` parses every `.dark` file under
  `packages/`, converges names over a few re-parse passes, and produces a
  `List<PT.PackageOp>` applied via `PackageOpPlayback`. This is the path we
  eventually want off the end-user runtime — but it is also the path that keeps
  F# and Dark consistent across a language change, which is why it cannot leave
  until blocker 4 is solved.
- **Seed export/import** — `LibDB.Seed.export` strips derived data and VACUUMs;
  `LibDB.Seed.growIfNeeded` reapplies unapplied ops and rebuilds projections on
  startup. The builtin `pmSeedExport` exposes export to Dark code.

The eventual target is unchanged in spirit: a new instance bootstraps from a
content snapshot with all package ops already applied, no `.dark` parse on
first run, and `LoadPackagesFromDisk` surviving only as a seed-builder for
CI/release. That target is **downstream of the four blockers**, not a thing to
start now.

---

## Notes that survive the punt

- **Test files stay.** `backend/testfiles/*.dark` are test inputs, not package
  source. They are out of scope for removal regardless of when the rest lands.
- **Distribution and signing** were sketched against a network-install story
  (`matter.darklang.com` hosting a seed, optional seed signing). Both sit
  behind working sync and a central server, so they are folded into the punt
  and not detailed here.
- **Fate of `packages/*.dark` after removal** (archive vs. tag-and-delete) is a
  decision to make at removal time, not before.
