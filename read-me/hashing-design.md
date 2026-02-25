# Content-Addressed Hashing for Package Items

## Vision

**Content-addressed package items.** Replace random UUIDs with
deterministic content hashes as the primary identifier for package
items (types, functions, values). References between items become
hash-based, making the entire package system reproducible and
deterministic.

This is also a prerequisite for **package bootstrapping** (stop
re-parsing `.dark` files every time F# or Darklang code changes —
produce a stable `.db` artifact that can be shipped and reused).
Bootstrapping is a future goal, not in scope for this PR — there are
more considerations around sync, upstream DB updates, etc.

**Two kinds of identifiers going forward:**

- **Hashes** — for anything whose identity is defined by its content:
  package types, functions, values, ops, and potentially commits
- **UUIDs** — for anything with no well-defined initial content:
  branches (mutable pointers to commits)

**Display:** Anywhere hashes are visible to users (CLI output, logs,
debugging), we need a short form of at most 7 characters (like git's
short SHA). The full hash is stored internally; display truncates.


## ContentHash Type

Defined in ProgramTypes.fs (and possibly RuntimeTypes.fs).
Single-case DU wrapping a hex-encoded SHA-256 string. NOT System.Guid.

```fsharp
/// Content-addressed hash for package items.
/// Hex-encoded SHA-256 digest, wrapped for type safety.
type ContentHash = ContentHash of string
```

Helpers: `fromSHA256Bytes`, `toHexString`, `toShortString` (first
7 hex chars for display).


## What Changes in the Types

`FQTypeName.Package`, `FQFnName.Package`, `FQValueName.Package` each
change from `uuid` to `ContentHash` (6 definitions: 3 in PT, 3 in RT).

`NameResolution` carries both original name and resolved reference:

```fsharp
/// Wraps a resolved reference alongside the name written in source.
/// 'resolved is FQTypeName, FQFnName, or FQValueName — which already
/// has Builtin/Package variants where applicable.
type NameResolution<'resolved> =
  { originalName : List<string>
    resolved : Result<'resolved, NameResolutionError> }
```

### What Goes Into the Hash

- **Resolved dependency hashes ARE included** — if your function calls
  `List.map`, the hash of `List.map`'s content is part of your hash
- **Original names are NOT included** — metadata, not identity
- **Structural content IS included** — literals, patterns, types, etc.
- **Builtin references included as-is** — `{name; version}` is
  already deterministic

Same code + same dependency versions = same hash.


## Commits as Hashes

```
commitHash = hash(parentCommitHash + sorted(opHashes))
```

Git-like hash chain. The `commits.id` column becomes a hash, not UUID.
Branch IDs stay as UUIDs (mutable pointers).


## Mutual Recursion (SCCs)

If A references B and B references A, and each hash includes the
other's hash, we have a cycle. Solved via strongly-connected component
(SCC) detection and batch hashing:

1. Identify the SCC — the set of mutually-referencing items
2. Sort deterministically by FQN
3. Serialize with **name-refs for intra-SCC references only** —
   where you'd normally write the hash of `Expr`, write the FQN
   string `"Darklang.LanguageTools.Expr"` instead. This breaks the
   circularity. References to items outside the SCC still use
   resolved hashes.
4. Hash the concatenated canonical forms → **group hash**
5. Each item's hash = `hash(groupHash + itemFQN)`

/// The canonical serializer takes a HashRefMode parameter:
/// - Normal: write resolved hashes for all references
/// - SccNameRef of Set<FQName>: write FQN strings for intra-SCC refs
///
/// This is purely a hashing strategy — stored/runtime data still uses
/// resolved hashes everywhere. The name-ref substitution only happens
/// during hash computation.

23 SCCs exist in the current DB (96 items, largest is 24 in the
expression parser). All predictable patterns: AST types, parsers,
traversals, CLI registry, recursive types/functions. Must be solved.

### SCC detection timing

At **parse/insert time** — when items are created and hashes computed.
This covers the initial load (parsing `.dark` files from disk in batch).

For **runtime dev-time** (user adds items one-at-a-time via CLI): if a
user adds `type A = | A of B` and B doesn't exist yet, A gets hashed
with an unresolved reference — it's WIP. Then they add
`type B = | B of A`. B resolves A fine, but now there's an SCC.

The WIP bucket is the key: all uncommitted items on a branch are WIP,
and WIP hashes are provisional. When new WIP content is added, we can
re-resolve other WIP items on the same branch that had broken
references. If re-resolution creates new SCCs, those items get
re-hashed as a group. This is safe because WIP hashes haven't been
committed — nothing outside the branch depends on them yet.

At commit time, all WIP items get their final hashes (re-resolved
and re-hashed if needed), and those hashes become immutable.

### Op coupling (aside)

SCC members' hashes are interdependent, so their ops are coupled —
same as any dependency, just bidirectional. Current `discard` is
all-or-nothing and safe. See `op-coupling-notes.md` if selective op
removal is ever added.


## Op Model Changes

```fsharp
// Content operations (content first, hash second)
| AddType of content : PackageType.PackageType * hash : ContentHash
| AddFn of content : PackageFn.PackageFn * hash : ContentHash
| AddValue of content : PackageValue.PackageValue * hash : ContentHash

// Location operations
| SetLocation of location : PackageLocation * itemKind : ItemKind * hash : ContentHash

// Punted for now (commented in PT.fs when needed):
// | UnassignName of location : PackageLocation
// | DeprecateContent of hash : ContentHash * reason : string * replacement : ContentHash option
```

`SetLocation` is idempotent — "this name now points to this hash."
Two machines setting the same location to the same hash sync harmlessly.

### Propagation (minimal change for this PR)

Propagation creates genuinely new content when dependencies change
(new resolved reference → new hash). For this PR: swap UUID→hash
minimally, replace `Guid.NewGuid()` with hash computation. Don't
redesign. Future PR may simplify further (see `phase4-propagation-rework.md`).


## SQL Schema Changes

Rewriting migrations from scratch is fine. Historical data doesn't
matter. Delete the `.db` and let it rebuild.

Key changes: content tables use `hash TEXT PRIMARY KEY` instead of
`id TEXT PRIMARY KEY`. Locations drop the synthetic `location_id` UUID,
use composite PK instead. Commits use hash IDs.

```sql
-- Locations (other tables follow the same pattern)
CREATE TABLE IF NOT EXISTS locations (
  -- branch_id: which branch this location lives on (branch-scoped resolution)
  branch_id TEXT NOT NULL REFERENCES branches(id),
  -- commit_id: NULL = WIP. Bulk-updated when ops are committed.
  -- Used for: WIP detection, rebase conflict detection, discard
  -- (restore committed version), merge (move to parent branch).
  commit_id TEXT REFERENCES commits(id),
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,
  name TEXT NOT NULL,
  item_type TEXT NOT NULL,
  item_hash TEXT NOT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT (datetime('now')),
  deprecated_at TIMESTAMP NULL,
  PRIMARY KEY (owner, modules, name, item_type, branch_id, item_hash)
);
```


## PackageRefs.fs (née PackageIDs.fs)

`PackageIDs.fs` (541 lines, ~100+ hardcoded UUIDs) gets renamed to
`PackageRefs.fs` in an early commit (before any hash work). Then in
Phase 2 it transitions to hardcoded content hashes.

**Seeding:** Not a chicken-and-egg problem. In Commit 2a, hashes are
computed and stored alongside UUIDs. By Commit 2b, we query the DB for
UUID→hash mappings and hardcode them. A verification test ensures
entries match.

**If serialization changes:** all hashes recompute; test catches it.

**WIP items from F# code:** hashes are deterministic, so compute
locally and hardcode — same content = same hash on any branch.


## Package Values and RT Dvals

Hash is computed from **PT content only**. The RT dval is a derived,
deterministic output of interpreting the PT. Multi-phase loading is
unaffected: hash at parse time, fill in RT dval later.


## Verification checklist

Run before every commit. Referred to as "verify" in the TODOs below.

- [ ] F# builds with 0 errors
- [ ] Packages reload successfully
- [ ] `./scripts/run-backend-tests` passes
- [ ] `./scripts/formatting/format format`
- [ ] `./scripts/run-cli help` works
- [ ] Check in with Stachu


---

## Phase 1 — Early safe commits

Get the foundation stable first. Each commit is independently
mergeable.

### Commit 1a: Rename PackageIDs → PackageRefs

Mechanical rename before any hash work, so the bigger changes don't
also have to deal with the rename.

- [ ] Rename `PackageIDs.fs` → `PackageRefs.fs`
- [ ] Update all 41+ references across the codebase
- [ ] Update .fsproj
- [ ] Verify

### Commit 1b: NameResolution revision

Update `NameResolution` to carry both original name and resolved
reference. Structural change, no hashing yet (resolved type stays uuid).

- [ ] Update `NameResolution<'resolved>` to wrap `{originalName; resolved}`
- [ ] Update all 7 expression positions in ProgramTypes.fs
- [ ] Update RuntimeTypes.fs, parser, serializers, DarkTypes converters
- [ ] Update any .dark files with corresponding type changes
- [ ] Tests:
  - F# (`Tests/`): serialization roundtrip for new NameResolution shape
  - Darklang (`testfiles/`): existing name resolution tests still pass
- [ ] Verify

### Commit 1c: ContentHash type definition

- [ ] Define `type ContentHash = ContentHash of string` in ProgramTypes.fs
  (and RuntimeTypes.fs if needed)
- [ ] Add equality, comparison, hashing, ToString, toShortString
- [ ] Add `fromSHA256Bytes`, `toHexString`
- [ ] Add serialization support (binary serializer Common.fs)
- [ ] Tests:
  - F# (`Tests/`): roundtrip, short string is 7 chars, equality
- [ ] Verify

### Commit 1d: Hash computation module in LibSerialization

- [ ] Create `LibSerialization/Hashing.fs`
- [ ] `computeTypeHash`, `computeFnHash`, `computeValueHash`,
  `computeOpHash` (move from Inserts.fs)
- [ ] Canonical serializer with `HashRefMode` parameter
- [ ] SCC detection (Tarjan's algorithm)
- [ ] Batch hashing for SCC groups
- [ ] Tests:
  - F# (`Tests/`): determinism, SCC hashing, Tarjan's on known graph
  - Darklang (`testfiles/`): hash a simple fn, hash a type with deps
- [ ] Verify

### Commit 1e: Commit hashing

- [ ] `computeCommitHash : parentHash option -> List<ContentHash> -> ContentHash`
- [ ] Update Commit type and creation logic
- [ ] Update SQL queries for commits table
- [ ] Tests:
  - F# (`Tests/`): determinism, op order independence (sorted)
- [ ] Verify


## Actual ID→Hash Transition (three incremental commits)

Each commit leaves the system fully working. Both IDs coexist during
the transition, which helps debugging and makes PackageRefs.fs seeding
trivial.

### Commit 2a: Add hashes alongside UUIDs (additive only)

Nothing changes behavior. Hashes ride along next to existing UUIDs.

- [ ] Add `hash : ContentHash` field to `PackageType`, `PackageFn`,
  `PackageValue` (alongside existing `id : uuid`)
- [ ] SQL: add `hash TEXT` column to content tables and locations
- [ ] Compute hash at insert time via LibSerialization.Hashing
- [ ] Update serialization, PT→RT, DarkTypes converters
- [ ] Update .dark files if type shapes changed
- [ ] Tests:
  - F# (`Tests/`): hash populated, deterministic, serialization roundtrip
  - Darklang (`testfiles/`): existing tests still pass (additive)
- [ ] Verify

### Commit 2b: Switch references to use hashes

UUIDs still in DB for debugging but no longer the primary reference.

- [ ] `FQXName.Package`: `uuid` → `ContentHash` (PT + RT, 6 defs)
- [ ] Update `PackageOp` variants (SetLocation, propagation, etc.)
- [ ] Binary serialization: all PT/RT serializers, bump format version
- [ ] LibPackageManager: SQL lookups by hash, propagation uses hash
  computation instead of `Guid.NewGuid()` (minimal changes only)
- [ ] Name resolution: returns ContentHash
- [ ] Create `PackageRefs.fs` with hashes (query DB for UUID→hash map)
- [ ] Consumers (41+ files): update references
- [ ] Update .dark files (PackageOp types, SCM, languageTools)
- [ ] Tests:
  - F# (`Tests/`): PT→RT flow, serialization, PackageRefs verification
  - Darklang (`testfiles/`): existing tests still pass (main regression)
- [ ] Verify

### Commit 2c: Remove UUIDs (cleanup)

Small, mechanical.

- [ ] Remove `id : uuid` from `PackageType`, `PackageFn`, `PackageValue`
- [ ] SQL: drop `id` column from content tables, drop `location_id`
- [ ] Remove UUID from serialization
- [ ] Clean up remaining UUID references
- [ ] Tests:
  - F# (`Tests/`): serialization roundtrip without UUIDs
  - Darklang (`testfiles/`): existing tests still pass
- [ ] Verify
- [ ] Two independent re-parses produce identical DBs (determinism)


## Future work (separate docs)

- `phase3-bootstrapping.md` — stable `.db` artifact
- `phase4-propagation-rework.md` — propagation redesign
- `hash-inspectability.md` — debugging/dev tooling for hash inspection
- `op-coupling-notes.md` — selective op removal safety
