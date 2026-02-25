# Stability & Sharing for Local Darklang Development

## The Two Problems

1. **Stability**: "My stuff isn't gonna be constantly wiped." When PT changes, SQL schema changes, or binary serialization format changes, the current answer is `rm data.db` and reload from `.dark` files. That works for stdlib but destroys any user-created code that only lives in the DB (branches, WIP, committed work done via the CLI).

2. **Sharing**: You and your coworker want to share _some_ branches between your local installs. Maybe also sync with a central server. Today there's no mechanism for this at all — each local `data.db` is an island.

### Direction (from initial feedback)
- The whole point of Darklang is to be a distributed Smalltalk-ish thing. The DB (ops, content-addressed items, branches) _is_ the program. The `.dark` text syntax is a UI concern — needed sometimes for reading/writing, but not the important part.
- `.dark` _files_ exist only because we need some way to get code into the DB initially. Until bootstrapping is solved, every internal dev cycle re-parses them on startup. That's the tax.
- Central server with op sync is part of the strategy, not a distant future thing. It's the natural home for the canonical program state.
- Goal: eliminate the `packages/` dir as a runtime dependency. Local dev based on `.db` snapshots or server sync.
- Backup is essential as a safety net for upgrades that don't go well.
- Better CLI release management — stop purging old releases from GitHub so you can roll back.

---

## Understanding What's At Risk

### What survives a DB wipe today
- Everything in `packages/` on disk (`.dark` files) — reloaded automatically on startup
- SQL schema — recreated by migrations
- The `main` branch with a single "Init" commit

### What gets destroyed
- **Any Darklang branches** you created via `branch create`
- **All commits** on those branches (and on main beyond the Init commit)
- **All WIP** (uncommitted changes)
- **User data** (`user_data_v0` — app state stored by running Darklang apps)
- **Canvas toplevels** (HTTP handlers, DBs created via `dark` CLI)
- **Traces** (execution history)
- **Scripts** (stored scripts)

### What triggers a wipe
- **PT structure changes** (new variant in `Expr`, `TypeReference`, `PackageOp`, etc.) — old binary blobs in `package_ops` become undeserializable. The binary format has no versioning; unknown tags crash with `CorruptedData`.
- **SQL schema changes** — new migrations are additive, but if you rewrite the package schema migration (as was done in Oct 2025), old data is incompatible.
- **Serialization format changes** — any change to how `LibBinarySerialization` writes/reads fields.

---

## Current Architecture (Relevant Facts)

- **Storage**: SQLite (`rundir/data.db`), WAL mode
- **Source of truth**: `package_ops` table (immutable event log of all changes, binary-serialized)
- **Projection tables**: `package_types`, `package_values`, `package_functions`, `locations` — derived from ops
- **Content tables are global** (not branch-scoped); **locations are branch-scoped**
- **Branches form a tree** rooted at `main`; merge only goes to parent
- **Content-addressed**: items identified by SHA256-based UUID of their serialized content
- **ID stabilization**: same code at same location gets same UUID across reloads
- **No export/import**: planned on the CLI_ROADMAP but not implemented. Git branches `export-import-work` and `start-language-canvas-export-and-pretty-print` exist but appear to be early explorations
- **Migrations**: append-only SQL files in `backend/migrations/`, run on startup, tracked in `system_migrations_v0`. No rollback mechanism.

---

## Strategy

Given the direction above, here's a layered strategy where each piece builds on the previous.

### Layer 0: Safety Net (do immediately)

#### Automatic DB backup before destructive operations
Before `reloadPackages` purges and rebuilds, copy `data.db` to `backups/data-{timestamp}.db`. A few lines in `LocalExec.fs`. Rotation: keep last N.

This doesn't solve anything architecturally — but it means if an upgrade goes wrong, you can `cp backups/data-<good>.db rundir/data.db` and pin yourself to the old CLI release to get back to a working state.

**Requires**: stop purging old CLI releases from GitHub. If the binary format changes between v42 and v43, you need to be able to run v42 against the old backup.

#### CLI release management
- Stop purging old releases from GH
- Consider a version pinning mechanism (e.g., `~/.darklang/version` or `.darklang-version` file in project root)
- Tag releases with which binary serialization format version they use, so you can tell at a glance which CLI versions are compatible with a given `.db`

---

### Layer 1: Binary Serialization Versioning (the real stability fix)

The fundamental problem is that the binary format has no forward/backward compatibility. Every PT change is a breaking change to the on-disk format. This must be fixed before anything else makes sense long-term.

**What to build**:
- Each serialized blob gets a format version (beyond the current single header version)
- Discriminated union tags are stable and never reused (they already mostly are, but needs to be a strict rule)
- New variants get new tags; removed variants keep their tag numbers reserved
- A migration path: on startup, if the DB contains blobs with an old format version, run a migrator that reads old → writes new
- The migrator is F# code that ships with the CLI (it needs to understand both old and new formats)

**The hard question**: how many old versions do you support? Options:
1. **Only N-1 → N** (rolling migration): each release can read the previous version's format. If you skip a version, you chain through intermediates. Simple but requires upgrading version by version.
2. **Any → current** (universal migration): the current release can read any historical format version. More code, but users never get stuck.
3. **Pragmatic**: support N-1 → N for now. If you need to skip, restore from backup + replay from central server (once that exists).

**Effort**: Significant one-time refactor of `LibBinarySerialization`. Ongoing discipline to never break tag numbering. But eliminates the biggest pain point.

---

### Layer 2: Central Server with Op Sync (the sharing solution)

This is the core of the sharing strategy. The architecture already has all the right primitives — content-addressed storage, branch-aware ops, rebase/merge. What's missing is the network transport.

#### Minimal viable version

A server that:
1. Stores a canonical `data.db` (the "origin")
2. Exposes an HTTP API (or even just SSH + SQLite file access):
   - `POST /ops` — push committed ops from a branch
   - `GET /ops?branch={id}&since={commit}` — pull ops since a commit
   - `GET /branches` — list branches
   - `POST /branches` — create a branch on the server
3. Local CLI commands:
   - `dark push` — send local committed ops to server
   - `dark pull` — fetch new ops from server, apply locally
   - `dark clone` — bootstrap a new local install from the server's DB

#### How it relates to the packages/ dir problem

Today the bootstrap path is: empty DB → run migrations → load `.dark` files from `packages/` → you have a working system.

With a central server, the bootstrap path becomes: empty DB → run migrations → `dark clone` (or just download the server's `data.db`) → you have a working system.

**This eliminates the need for `packages/` on disk.** The stdlib, SCM code, CLI code — all of it — lives in the server's DB. A fresh install pulls it down. The `.dark` files become a build artifact or a bootstrapping fallback, not the source of truth.

#### The bootstrapping chicken-and-egg

You need a running CLI to talk to the server. The CLI needs stdlib to function. Stdlib currently comes from `.dark` files (re-parsed on every startup — the current pain point). So how do you bootstrap without `.dark` files?

**Seed DB** (the likely answer): Ship a pre-built `data.db` as a release artifact alongside the CLI binary. The release process parses the `.dark` files _once_, generates the DB, and bundles it. First run on a new machine: drop the seed DB into `rundir/`. No parsing needed. Then `dark pull` to get latest from the server.

This turns `.dark` files into a **build input** (used during the release process to generate the seed DB) rather than a **runtime dependency** (re-parsed on every startup). The `packages/` dir stays in the repo for the release build but users/developers never interact with it.

The re-parse-on-every-startup tax goes away. PT changes during internal dev still require a re-parse to generate a new seed DB, but that happens once in CI, not on every developer's machine on every restart.

Other options considered but probably worse:
- **Embedded bootstrap**: Bake minimal stdlib into the CLI binary itself. Complex, couples stdlib to the binary build.
- **`.dark` files as fallback**: Keep them around for "break glass" recovery. Maybe worth keeping as an emergency escape hatch, but shouldn't be the normal path.

#### Who owns the server?

Options:
- **You host it** (a VPS, a Darklang cloud instance, etc.) — canonical for your team
- **Each developer can run one** — `dark server start` runs a local HTTP server backed by `data.db`. Your coworker points their CLI at your machine (or vice versa). Peer-to-peer-ish.
- **Both** — a "hub" server for canonical state, plus the ability to sync directly between peers

For two people, "one of you runs the server" is probably fine. It can literally be `dark server start` on your laptop when you're online.

---

### Layer 3: Upgrade Safety (ties it all together)

With layers 0-2 in place, the upgrade path becomes:

1. **Before upgrading CLI**: automatic backup of `data.db` (Layer 0)
2. **Upgrade CLI binary**
3. **On first run**: migration runs (Layer 1) — reads old-format blobs, writes new-format blobs
4. **If migration fails**: restore backup, pin to old CLI version, file a bug
5. **If migration succeeds**: `dark push` to sync the migrated state to the central server
6. **Coworker upgrades**: their CLI runs the same migration on their local DB. Or they just `dark pull` the already-migrated data from the server.

The central server itself needs an upgrade path too:
- Upgrade server CLI binary
- Server runs migration on its DB
- If it fails: restore server backup, roll back server binary

---

## Open Questions

1. **How often do PT changes happen right now?** This determines how urgently Layer 1 (serialization versioning) is needed vs. just relying on Layer 0 (backups).

2. **What does "sharing a branch" mean concretely?** "I push my branch to the server, you pull it and can see my functions" — is that the workflow? Or do you need real-time concurrent access to the same branch?

3. **Do you care about preserving commit history when sharing?** Or is "here's the current state of my branch's code" enough?

4. **Canvas/user data**: Are you writing Darklang apps that store data in `user_data_v0`? If so, that data is also at risk and needs its own backup/sync story.

5. **What's the actual workflow today?** When you create a function via `dark fn ...`, does it only live in the DB? (I believe so, but confirming.)

6. **Server hosting preference?** Dedicated VPS? One of your machines? Does the central server need to be always-on, or is "run it when you need to sync" acceptable?

7. **How much stdlib churn is there?** If stdlib changes frequently (via `.dark` file edits), the seed DB approach means regenerating the seed on each release. Fine if automated, annoying if manual.

8. **Existing export-import branches**: The git branches `export-import-work` and `start-language-canvas-export-and-pretty-print` — is there anything salvageable there, or are they stale experiments?

---

## Rough Sequencing

| Phase | What | Unblocks | Effort |
|-------|------|----------|--------|
| **0** | Auto-backup + stop purging GH releases | Rollback safety | Hours |
| **1** | Binary serialization versioning + migration | Non-destructive upgrades | Days-weeks |
| **2a** | `dark snapshot` / `dark snapshot restore` CLI | Explicit user-controlled backup/restore | Day |
| **2b** | Op-level export/import (per-branch) | Offline branch sharing via files | Days |
| **3** | Central server (HTTP API + push/pull/clone) | Real-time sharing, eliminate packages/ dir | Week+ |
| **4** | Seed DB as release artifact | Bootstrap without `.dark` files | Day (once server exists) |
| **5** | Kill the `packages/` dir | Clean architecture | Depends on 3+4 |

Phases 0, 1, and 2a are independent of the server and provide immediate value. Phase 2b is a stepping stone to 3 (the export format becomes the wire format). Phase 3 is the big one. Phases 4 and 5 are cleanup that falls out naturally once the server exists.

---

## Relevant Existing Work

- `read-me/CLI_ROADMAP.md` — mentions `export`/`load` commands as P2 features
- `read-me/scm-branches-plan.md` — full design doc for the branching system (implemented)
- `read-me/ai-scm-review.md` — review of SCM design, identifies gaps
- `read-me/hot-reload-design.md` — generation-based cache invalidation (relevant to server sync)
- Git branches `export-import-work`, `start-language-canvas-export-and-pretty-print` — earlier explorations of export
- `backend/src/LocalExec/LoadPackagesFromDisk.fs` — the "import from `.dark` files" pipeline
- `backend/src/LibBinarySerialization/` — the binary format that breaks on PT changes
- `backend/src/LocalExec/Migrations.fs` — the SQL migration runner (model for binary format migrations?)
