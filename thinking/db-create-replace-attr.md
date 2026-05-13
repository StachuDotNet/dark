# Drop "toplevels"; rebuild DB access around references

This started as "replace `[<DB>] type XDB = X` in testfiles with
explicit `DB.create<X> "xdb"`." The cleaner target is much larger
and much simpler: **delete the toplevels abstraction**, **address
DBs by reference rather than by name**, and **reshape the entire
DB stdlib surface from the ground up around references**. The
testfile rewrite falls out for free.

## Pre-flight checks (do these first)

Before writing any of the rewrite, confirm:

1. **Parse-time type resolution gives us the hash.** Trace
   `DB.create<X>` through `FSharpToWrittenTypes` →
   `WrittenTypesToProgramTypes` and confirm the TypeArg lands at
   the builtin call site as a fully-resolved
   `TCustomType({resolved = Ok (FQTypeName.Package hash)}, _)`,
   not a lazy/unresolved reference. This is the design's load-
   bearing assumption (see "type_hash" section). ~10 min check.

2. **TypeArgs on Dark-side package fns.** The friendly wrappers
   `Stdlib.DB.findByName` / `findByType<'a>` need TypeArgs through
   *package* (Dark-defined) functions, not just builtins. Grep
   `packages/darklang/stdlib/` for existing `<'a>`-bearing fn
   declarations to confirm the shape works. If it doesn't, the
   wrappers stay as builtins, no big deal.

If either check fails, the plan stays but the surface changes
slightly.

## The framing shift

Today: DBs are addressed by **name**. `[<DB>] type XDB = X`
declares "XDB" as a magic name; bare `XDB` references resolve via
a `VarNotFound → program.dbs` shim at `Interpreter.fs:393`.
Builtins receive `DDB of name` and look the DB up.

Better: DBs are addressed by **reference**. A `DDBRef` is
fundamentally an opaque ID. The DB record (`id`, `name`,
`type_hash`, `version`, …) lives in the registry; the ref
points at it. **Name is mutable** — `DB.rename(ref, newName)`
works, and existing refs keep working. **Type is immutable**
once a DB is created (no one can change a DB's stored type
out from under existing refs). Type info travels alongside the
ref as a cache for type-checking (`DDBRef of id * typeRef`)
but doesn't define the ref's identity — multiple DBs of the
same type are a normal case to plan for.

This is the shape that file handles, channels, and other
resource-references have everywhere. It's also the shape DBs
already want — today's `name` is doing double duty as "stable
ID" and "display name," and the magic-name resolution is a
known wart (`Interpreter.fs:389` TODO).

## The new DB record + registry

Persistent shape:

```
dbs:
  id          (opaque UUID — see "ID type" below)
  name        (mutable; user-facing handle; unique)
  type_hash   (immutable for a given DB; same shape package
               values use to record their stored type)
  deleted     (soft delete)
  created_at, updated_at
```

No `version` column. Today's `PT.DB.T.version` is always `0` and
was meant to partition rows when a user's type structure
changed. With `type_hash` immutable per DB, a type change is "a
new DB" instead — no counter needed. Similarly, drop
`user_data_v0.user_version` and `dark_version` (both also always
0 today; `currentDarkVersion = 0` constant at
`LibDB/UserDB.fs:38` was reserved for Dark-internal format
changes that never happened). Composite key on `user_data_v0`
collapses to `(db_id, key)`. If a real format change happens
later, add a stamp then.

A note on `type_hash` — this is the load-bearing invariant of
the design:

- The DB records the *type's hash*, not a location (FQTypeName).
- Package types are content-addressed: a hash uniquely
  identifies a structural definition, immutably. A type *can't
  change*. Editing source-level `type X` produces a new package
  type with a new hash; the old hash still exists in the package
  store, and any DB pointing at it keeps working against the
  frozen old definition.
- Locations (the human-facing names for types) can be repointed
  over time. The DB doesn't care — it holds the hash.
- This mirrors how package values record their type (by hash).

At parse time, `DB.create<X>` resolves `X` to its hash. From
that point on, the DB is bound to that specific structural
definition forever. There's no "DB schema version drift"
because there's no way for the schema to drift.

`user_data_v0` keys on `db_id` instead of today's `table_tlid`.
Drop `tlid` as a concept here entirely — it was the
toplevels-era system-wide identifier; with toplevels gone, `id`
is just "DB id," no shared namespace.

ID is *not* content-addressed — there's nothing useful to hash
(names mutate, types are shared across DBs, "this specific DB
row" has no inherent content). It's an opaque token. UUID v7
(timestamp-prefixed; sortable by creation, nicer for debugging
and cursor-style pagination). Two DBs of the same type with the
same starting name (created in sequence, with a rename in
between) get distinct IDs.

The registry (replaces `LibCloud/Toplevels.fs` — drop the
"toplevels" branding entirely; this becomes
`LibDB/Registry.fs` or `LibDB/UserDBs.fs`):

- In-process cache: `Map<id, DB>` plus `Map<name, id>` for name
  lookups. Cache invalidated on writes (create / rename / drop).
- Single writer (this process); reads come from cache.

## The runtime Dval

```fsharp
| DDBRef of id : Uuid * typ : TypeReference
```

- `id` is the ref's identity; never changes. If a DB is renamed
  or its row is updated, the ref keeps working.
- `typ` is a cache for type-checking — both static (so
  `TDB<X>` and `TDB<Y>` are distinguishable; mirrors today's
  `TDB of TypeReference`) and dynamic (`DB.set` can sanity-check
  the value's type without consulting the registry). Cached
  from the DB record's `type_hash` at ref-construction time;
  immutable because the DB's type is immutable.
- No `name` on the Dval. To get the current name:
  `DB.name(ref) : Result<String, _>` consults the registry.

`TDB of TypeReference` stays. `KTDB of ValueType` stays.

## The reshaped DB stdlib surface

Don't port the existing name-takers; rebuild the API around
refs. Sketch:

### Acquisition

- `DB.create<'a>(name : String) -> Result<DDBRef<'a>, DB.CreateError>`
  Writes a new DB record. Returns the ref. Name uniqueness is
  *not* enforced — two DBs can share a name (use IDs to
  disambiguate). The error case is "type `'a` isn't storable"
  — see the **Storable types** section below.

- Builtin: `DB.find(filter : { name : Option<String>; type : Option<TypeReference> }) -> List<DDBRef>`
  Generic discovery. Both filter fields are `Option` —
  `Some` narrows, `None` doesn't. Returns a list because
  multiple DBs of the same type is a normal case (and a
  type-only filter naturally returns many).

  Dark-side stdlib wraps the builtin with friendlier helpers
  (or builtins, if pre-flight check 2 rules out Dark-side
  TypeArgs):
  - `DB.findByName(name : String) -> List<DDBRef>` — list,
    since names aren't unique. Calls `find` with name=Some,
    type=None.
  - `DB.findByType<'a>() -> List<DDBRef<'a>>` — list. Calls
    `find` with name=None, type=Some('a). Returns DBs whose
    `type_hash` exactly matches `'a`'s structural hash;
    structurally-different `'a`s never match.
  - Callers narrow via list operations (`List.head`,
    `List.find …`). No `findOne` helper — narrowing is a
    normal step.

- If you already have an `id` (deserialized from some prior
  context), construct a ref directly:
  `DB.fromId<'a>(id : Uuid) -> Result<DDBRef<'a>, Error>`
  Verifies the DB exists and type-checks.

### Mutation of the registry

- `DB.rename(ref : DDBRef<'a>, newName : String) -> Result<Unit, _>`
  Names are mutable; refs aren't.

- `DB.drop(ref : DDBRef<'a>) -> Result<Unit, _>`
  Drops the DB (and its rows). Existing refs to this DB error
  on use.

### Introspection

- `DB.id(ref : DDBRef<'a>) -> Uuid`
- `DB.name(ref : DDBRef<'a>) -> Result<String, _>` (registry lookup)
- `DB.list() -> List<{ id; name; type }>` (for tooling)

### Data operations (rewritten from today's `DB.fs`)

All take a `DDBRef<'a>` where today they take a `DDB name`. No
name-based addressing anywhere.

- `DB.set(val: 'a, key: String, ref: DDBRef<'a>) -> 'a`
- `DB.get(key: String, ref: DDBRef<'a>) -> Option<'a>`
- `DB.getMany`, `DB.getAll`, `DB.getAllWithKeys`,
  `DB.getExisting`, `DB.delete`, `DB.deleteAll`, `DB.query`,
  `DB.queryOne`, `DB.queryCount`, `DB.count`, `DB.keys` — all
  switch from `DDB name` to `DDBRef<'a>` as the last parameter.

The point: not "replace one builtin." Rebuild from the ground
up around references.

## Storable types

`DB.create<T>` runs `T` through an `isStorable` predicate at
call time. The predicate walks the type recursively (substituting
through aliases, descending into tuples / lists / dicts / records
/ enums / type args) and rejects any sub-position that's
non-serializable.

Reading `LibSerialization/DvalReprInternalQueryable.fs` (the
serializer that backs `Stdlib.DB.set` / `get`), the universe
splits:

**Storable**: Unit, Bool, Int8/16/32/64/128, UInt8/16/32/64/128,
Float, Char, String, Uuid, DateTime, Tuple, List, Dict, Record,
Enum, Alias (recurse), `TCustomType` with `resolved = Ok`, Blob
(envelope form).

**Not storable** (the writer/reader raise today):
- `TFn _` — `DvalReprInternalQueryable.fs:377`. No useful
  serialization of a function value.
- `TStream _` — `:391, :190-193`. Explicit "drain to a Blob
  before storing."
- `TDB _` — `:378, :197`. With `DDBRef of Uuid * TypeReference`
  the value is structurally serializable, but DB-refs-in-DBs has
  no use case yet — keep excluded; revisit if we ever need it.
- `TVariable _` — `:379`. Unresolved generics can't be stored;
  `DB.create<'a>` with `'a` unbound is nonsensical.
- `TCustomType` with `resolved = Error` — can't look up the
  structure to recurse through.

### The `CreateError` enum

Modeled on `Stdlib.Json.ParseError` (`Builtins.Pure/Libs/Json.fs:159`):
typed Dark-side enum with structured payload per variant,
`toDT`/`fromDT` for round-tripping, lives in
`PackageRefs.Type.Stdlib.DB.CreateError`. Builtin signature is
`TypeReference.result (TCustomType(DDBRef...)) (TCustomType(CreateError))`.

Sketch:

```fsharp
type CreateError =
  /// The type contains a function somewhere — can't serialize.
  | TypeContainsFn of original : TypeReference * offending : TypeReference
  /// The type contains a stream — drain to a Blob first.
  | TypeContainsStream of original : TypeReference * offending : TypeReference
  /// The type contains a DB ref — not supported yet.
  | TypeContainsDBRef of original : TypeReference * offending : TypeReference
  /// The type has an unresolved type variable — can't determine layout.
  | UnresolvedTypeVariable of name : String * original : TypeReference
  /// A nested type name doesn't resolve in the current branch.
  | TypeNotResolvable of FQTypeName.FQTypeName
```

Each case carries enough for a useful error message: the
original `T` for context, plus the offending sub-position or
unresolved name. The structured enum gives callers programmatic
recovery (try a different type, surface to UI, log structured)
in a way a raised exception wouldn't.

`isStorable` lives next to the registry in
`LibDB/Registry.fs` (or a `LibDB/TypeStorability.fs`). Also
useful to expose Dark-side as `DB.checkStorable<'a>() ->
Result<Unit, CreateError>` for introspection — callers that want
to validate before calling `create`.

I/O failures (SQLite write errors, etc.) still raise as
exceptions — `CreateError` is for "your call site is wrong,"
not "infrastructure broke."

## Where the registry is consulted

- **From Dark code**: only via the introspection / find /
  rename / drop / create surface above. No implicit lookups.
- **From builtins (set, get, …)**: by `id` (from the `DDBRef`),
  for `user_data_v0.db_id` joins. Cache-hit fast path.
- **From the parser / name resolver**: never. Parse-time has no
  knowledge of DBs.
- **From tooling (LSP, `dark search`)**: via `DB.list` or a
  dedicated F# API on the registry. Cold path.

## What dies

- `LibCloud/Toplevels.fs` — entire file.
- `Serialize.fs` toplevel-loading helpers (`loadToplevels`,
  `fetchTLIDsForAllDBs`, etc.).
- `toplevels_v0` table — kill-and-fill (the
  `module`/`modifier`/`digest`/`data` columns weren't doing
  anything; no migration to write, just drop and recreate as
  `dbs`).
- `[<DB>]` attribute branch in `LibParser/TestModule.fs`
  (`parseTypeDecl`, the `isDB` guard).
- `WT.DB` type and the `parentDBs` threading through
  `parseModule`.
- `Program.dbs` field on `RT.Program` and `RT.Program` itself if
  it becomes empty.
- `VarNotFound → program.dbs` shim in `Interpreter.fs`.
- `setupDBs` preload in `LibExecution.Tests.fs:32` plus the
  `rtDBs`/`dbs` plumbing through `t`.
- The `# FUTURE: drop program.dbs entirely` comment block in
  `LibExecution.Tests.fs:38-82` — this design supersedes it.
- `Toplevel.toTLID` shim at `ProgramTypes.fs:1090` (no remaining
  callers).
- The concept of `tlid` as it applies to DBs — DBs get their own
  `id`. (`tlid` may still exist as a type alias used elsewhere;
  audit and remove if it has no other consumers.)
- `DDB of name : string` Dval (replaced by `DDBRef of id * typ`).

## What changes

| File | Change |
|---|---|
| `backend/src/LibDB/Registry.fs` (new) | DB registry: load/save/delete by `id`, name↔id index, in-process cache. Replaces `LibCloud/Toplevels.fs`. |
| `backend/src/LibDB/UserDB.fs` | Switch `user_data_v0.table_tlid` → `db_id`. Drop `user_version`/`dark_version` columns and the `currentDarkVersion` constant. Composite key becomes `(db_id, key)`. |
| `backend/src/LibExecution/RuntimeTypes.fs` | `DDB` → `DDBRef of Uuid * TypeReference`. Drop `Program.dbs` field. Keep `TDB`/`KTDB`. |
| `backend/src/LibExecution/Interpreter.fs` | Delete `VarNotFound → program.dbs` shim. |
| `backend/src/Builtins/Builtins.Matter/Libs/DB.fs` | Full rewrite around the new API surface (see above). Add `CreateError` enum + `toDT`/`fromDT` following `Stdlib.Json.ParseError` shape. Add `isStorable : TypeReference -> Result<Unit, CreateError>` helper. |
| `packages/darklang/stdlib/db/createError.dark` (new) | Dark-side `CreateError` enum declaration in `PackageRefs.Type.Stdlib.DB.CreateError`. |
| `backend/src/LibParser/TestModule.fs` | Drop `isDB` branch in `parseTypeDecl`; drop `parentDBs` threading. |
| `backend/src/LibParser/WrittenTypes.fs` + `WrittenTypesToProgramTypes.fs` | Delete `WT.DB`, `DB.toPT`. |
| `backend/src/LibSerialization/Binary/Serializers/PT/Toplevel.fs` | Delete or rename; not a "toplevel" anymore. |
| `backend/src/LibCloud/Toplevels.fs` | Delete. |
| `backend/src/LibCloud/Serialize.fs` | Drop toplevel-loading helpers. |
| `backend/tests/Tests/LibExecution.Tests.fs` | Drop `setupDBs`, `rtDBs` plumbing. Per-test wipe still needed (now wipes `dbs` + `user_data_v0`). |
| `backend/testfiles/execution/cloud/db.dark` | Rewrite all 26 module blocks. |
| `backend/testfiles/execution/README.md` | Update `## DBs` section. |
| `packages/darklang/stdlib/db/` (or wherever Dark-side DB code lives) | Update Dark-side stdlib wrappers to use refs. |

## Migration: how `cloud/db.dark` actually looks

Today:

```dark
type X = { x: String }
[<DB>]
type XDB = X

module SetDoesUpsert =
  (let old = Stdlib.DB.set (X { x = "hello" }) "hello" XDB
   let newval = Stdlib.DB.set (X { x = "goodbye" }) "hello" XDB
   Stdlib.DB.getAllWithKeys XDB) = Dict { hello = X { x = "goodbye" } }
```

After:

```dark
type X = { x: String }

module SetDoesUpsert =
  (let xdb = (Stdlib.DB.create<X> "xdb").unwrap()
   let old = Stdlib.DB.set (X { x = "hello" }) "hello" xdb
   let newval = Stdlib.DB.set (X { x = "goodbye" }) "hello" xdb
   Stdlib.DB.getAllWithKeys xdb) = Dict { hello = X { x = "goodbye" } }
```

The `.unwrap()` reflects `create`'s `Result<DDBRef,
CreateError>` return — tests use storable types so it never
actually fires, but the call site is honest about the
possibility. Each `module` block creates its DBs inline.
Per-test wipe means names can repeat across blocks (and name
uniqueness isn't enforced anyway — IDs are identity). Avoids
the file-level-let-into-nested-module problem (file-level `let`
becomes a `PackageValue`, not visible as a bare identifier
inside `module` blocks).

12 `[<DB>]` declarations × ~2 module blocks per DB on average =
~26 `DB.create<...>` calls in the rewritten file. Mechanical.

## Sequencing

One PR. Pieces are too tightly coupled to split:

1. New `LibDB/Registry.fs` + `dbs` schema.
2. Rewrite `Builtins.Matter/Libs/DB.fs` around the new API.
3. `DDB` → `DDBRef` across `RuntimeTypes.fs` + consumers.
4. Drop `VarNotFound → program.dbs` shim, `Program.dbs`,
   `setupDBs`, the FUTURE comment.
5. Drop `[<DB>]` branch + `WT.DB` + `parentDBs`.
6. Rewrite `cloud/db.dark`.
7. Delete `LibCloud/Toplevels.fs`, toplevel-serialize helpers,
   `Toplevel.toTLID` shim.

Build is broken until step 6. Single branch.

## Open questions

- **Multi-tenant future.** "All DBs accessible to all programs"
  matches today's no-canvas-scoping reality. The `dbs` schema
  should leave room for `owner`/`tenant` columns that go
  unpopulated for now. Out of scope.

## Why the whole rebuild is the right scope

Smaller PR: testfile syntax change only.
Bigger PR: this one.

The bigger PR is the right scope because:

- The smaller PR leaves `program.dbs`, `VarNotFound` magic,
  `setupDBs`, `toplevels_v0`, the `[<DB>]` → `WT.DB` → `PT.DB` →
  `RT.DB` pipeline, and a name-keyed builtin surface all in
  place. Testfiles change; production code can't yet write
  `DB.create<X>` in any clean way.
- The bigger PR makes DBs work the way other resources do —
  reference-as-value, runtime-acquired, no parse-time magic — and
  is what new Dark code (outside tests) will want. There's no
  future where production code uses `[<DB>]` attributes.
- Toplevels is doing zero load-bearing work today. Every hour
  spent navigating it is sunk cost.

Migration is contained: one testfile, ~150 lines of F# deletion,
a schema swap, and an API rewrite that mostly threads the same
builtins under different signatures. End state is meaningfully
simpler.
