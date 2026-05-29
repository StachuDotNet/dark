# Iter 11 — Projections-as-Dark-code

The big rewrite doc treats projections as F#-defined: a struct
per stream type, a fixed SQL schema, a builder that replays
ops and inserts rows. What if the projection-builder is itself
**Dark code**?

This iter pushes the "less F#, more Dark" thesis to its
conclusion. The payoff: users can define their own projections
(typed, versioned, hot-swappable) for their own app data. The
risk: bootstrap chicken-and-egg, and a performance ceiling on
projection rebuilds.

## What "projection as Dark fn" means

Today's mental model:

```
ops.db  (sqlite, append-only, master)
   ↓ F# code: replay ops, INSERT into rows
projection.db  (sqlite, per-(stream, key))
```

Tomorrow's:

```
ops.db
   ↓ Dark fn: (List<Op>) → SchemaRows
projection.db
```

Where the Dark fn is just:

```dark
module Darklang.Projections.PackageStore

let schema : Projection.Schema =
  Projection.Schema [
    Projection.Column "name" Projection.ColumnType.Text Projection.PrimaryKey
    Projection.Column "type" Projection.ColumnType.Text
    Projection.Column "hash" Projection.ColumnType.Bytes
    Projection.Column "deprecated" Projection.ColumnType.Bool
    Projection.Index "by-name" ["name"]
  ]

let build (ops: List<PackageOp>) : List<Projection.Row> =
  ops
  |> List.fold Map.empty applyOp
  |> Map.values
  |> List.map toRow

let applyOp (state: Map<String, PackageDef>) (op: PackageOp) =
  match op with
  | CreateFn(name, type_, body, hash) -> ...
  | UpdateFn(...) -> ...
  | DeprecateFn(name) -> ...
  | ...

Projection.register {
  name = "darklang.package-store"
  schema = schema
  build = build
  appliesToStream = "packages"
}
```

The daemon discovers `Projection.register` calls in the package
store on boot, sets up the projection table, runs `build` to
populate.

## What this unlocks

### Custom user projections

A user's app has a `posts` stream. They want a "popular posts"
materialized view with a 100-row cap, sorted by view count, with
fields aggregated from the events. Today: write F# code, ship a
new daemon binary. Tomorrow:

```dark
module Mycorp.Projections.PopularPosts

let schema = ...
let build (ops: List<Op>) : List<Row> =
  ops
  |> List.filter (fun op -> op.stream == "posts")
  |> List.fold Map.empty applyPostOp
  |> Map.values
  |> List.sortBy (fun p -> -p.viewCount)
  |> List.take 100
  |> List.map toRow

Projection.register {
  name = "mycorp.popular-posts"
  schema; build
  appliesToStream = "posts"
}
```

They commit, push to the daemon. The daemon picks it up
(detected as a `Projection.register` invocation in a newly-
landed op), allocates the projection file, runs `build`, makes
it queryable from the user's app:

```dark
let popular = Mycorp.Projections.PopularPosts.read ()
```

This is **user-defined materialized views in a typed language,
hot-swappable**. The closest precedent is
event-sourcing-with-CQRS in Haskell, but always bespoke per
project. Here it's a built-in primitive.

### Versioned projection schemas

A projection's schema and builder change over time. Old
projections become stale. The daemon's behavior:

- Detect change (schema-hash diff).
- Mark all projection files for that name as "stale."
- On next read, drop and rebuild. Or better: rebuild
  proactively when load is low.
- If the schema migration is incompatible (column dropped),
  the new build fn handles it; old data simply isn't there.

Equivalent to "kill-and-fill" migrations from the codebase
(per `feedback_libcloud_disable_not_delete.md` adjacent
guidance). No multi-step ALTER TABLE dance.

### Hot-swappable

Editing a projection fn → committing → daemon picks up the new
op → projections using that fn rebuild.

Same as iter 09's hot-swap LSP, iter 10's hot-swap REPL
commands. Same machine.

### Multiple projections per stream

The same `posts` stream can drive five projections:
`all-posts-by-author`, `recent-posts`, `popular-posts`,
`posts-by-tag`, `archive`. Each is its own SQLite file; each is
read independently; each rebuilds on its own schedule.

This is hard to do in F# because adding a projection means
shipping a binary. In Dark it's a fn definition.

### Cross-stream projections

Today's plan implies one stream per projection. But:

```dark
let build (allOps: List<Op>) : List<Row> =
  let users = allOps |> List.filter (fun o -> o.stream == "users")
  let posts = allOps |> List.filter (fun o -> o.stream == "posts")
  joinUsersWithPosts users posts |> List.map toRow

Projection.register {
  name = "mycorp.user-post-stats"
  appliesToStreams = ["users"; "posts"]
  ...
}
```

The daemon rebuilds whenever either stream advances. The
projection joins them at build time. Like a streaming SQL
JOIN.

## Bootstrap chicken-and-egg

Problem: the daemon needs to *project the package store* to load
any Dark code. But the projection-builder *is* Dark code. How
does it run before it's loaded?

Solution: a small **F# bootstrap projection** for the package
store specifically. Hardcoded schema; hardcoded
op-replay logic. Just enough to project the package types and
fns into a queryable form. ~500 LOC of F#.

Once the bootstrap projection is built:
1. Daemon loads `Darklang.Projections.PackageStore` — a Dark fn
   with the same logic.
2. Daemon compares: does the Dark version produce the same
   output as the F# bootstrap? If yes, retire the bootstrap;
   use the Dark fn going forward (now it's hot-swappable). If
   no, log the divergence (likely a bug) and stay on bootstrap.
3. Future projection rebuilds use the Dark fn.

This is the kernel-boot pattern. The F# bootstrap exists only
to get the system to a state where Dark can take over. It
never disappears entirely (you need it on first launch and
after corruption recovery), but it's pinned and rarely changed.

Same pattern handles other foundational projections (sessions,
maybe traces).

## Performance

Concern: Dark fns are slower than F# (10x? 100x? depends on the
fn). Projection rebuilds happen on schema changes and op
batches. Is this fast enough?

Numbers (assuming iter 06's perf budget):

- Trace stream at 100 ops/sec, 1KB each. Building the projection
  for 1 hour of ops = 360K ops. At 10μs/op in Dark = 3.6s.
  Acceptable for a one-shot rebuild.
- Package store at typical load: 100s of ops in total. Rebuild
  is sub-second.
- App data streams (posts, users, etc.): typically 100K ops
  per high-traffic app per day. ~1s rebuild.

So the **default** rebuild path is fine.

What's not fine: rebuilding 100M-op streams (heavy production
apps). Two strategies:

1. **Incremental projections.** The daemon tracks the last-
   projected op hash. On rebuild, it only replays new ops on
   top of the existing projection. Linear in new ops, not in
   total ops.
2. **Snapshot + delta.** Periodically, the daemon snapshots the
   projection's state to a file. Rebuild = restore snapshot +
   replay delta.

Both are essentially "checkpointing." Add to the projection
fn's contract:

```dark
type ProjectionStrategy =
  | FullRebuild  // simple, replay all ops every time
  | Incremental of (CurrentState -> List<Op> -> CurrentState)
  | Snapshot of (CurrentState -> Bytes) * (Bytes -> CurrentState)

Projection.register {
  ...
  strategy = ProjectionStrategy.Incremental updateRow
}
```

The user opts in to faster strategies. Default is `FullRebuild`
for simplicity.

### What about hot-path queries?

Reads of the projection (the SELECT side) are SQLite-fast — same
as today's plan. The Dark layer is only on the *build* side.

So:
- Write op: F# (fast, log-append).
- Build projection: Dark (slow-ish, but cached).
- Read projection: F# / SQLite (fast, cached).

The "slow-ish" only matters at projection rebuild time. With
incremental updates, that's amortized to ~10μs per new op, well
under the per-op throughput budget.

## Sandboxing

A user-defined projection fn can:
- Take CPU time (limit: 10s per build, configurable).
- Allocate memory (limit: 1GB heap).
- Read other projections (allowed; common for joined views).
- NOT do IO outside the projection contract (no HTTP, no FS).
- NOT mutate state outside its return value.

Standard Dark sandboxing applies. The fn's signature is
`(List<Op>) → List<Row>`; nothing else is reachable.

If the fn loops infinitely or panics, daemon catches:
- Marks projection "broken."
- Logs the error to the projection's audit stream.
- Surfaces in `dark projections list` with status flag.
- The user gets a notification; affected reads return an
  explicit error (`projection unavailable: see logs`).

## Versioning of projection definitions

A projection fn evolves. Each commit produces a new hash. The
projection's stored data records the build-fn-hash that produced
it. On daemon boot:
- Compare current build-fn-hash vs stored.
- If different, rebuild.
- If same, accept.

If two daemons disagree on the build-fn-hash (e.g., one is
running a newer commit), they keep their own projections.
Sync brings their packages into alignment first; then both
rebuild.

This makes projections **branch-aware**. Branch A and branch B
can have different `popular-posts` projection fns; the daemon
keeps both projections separately keyed by `(branch, projection-
name)`.

## Defining schemas in Dark

The schema syntax has to express:
- Columns + types.
- Primary keys, indexes.
- Foreign-key references? (Probably not — we're going for
  embarrassingly-parallel projections, not normalized schemas.)

```dark
type Column =
  { name: String
    type_: ColumnType
    nullable: Bool
    primaryKey: Bool }

type ColumnType =
  | Text
  | Int64
  | Float
  | Bool
  | Bytes
  | Json   // serialized Dval

type Index =
  { name: String
    columns: List<String>
    unique: Bool }

type Schema =
  { columns: List<Column>
    indexes: List<Index> }
```

The daemon's F# layer translates this to:

```sql
CREATE TABLE projection_<name>_<branch> (
  <col> <sqltype> [PRIMARY KEY] [NOT NULL],
  ...
);
CREATE [UNIQUE] INDEX <ix_name> ON projection_<name>_<branch>(<cols>);
```

Schema migrations: schema-hash changes → drop + recreate. (Per
the kill-and-fill stance.)

## Custom queries

A projection's SQL is queryable directly from Dark:

```dark
Projection.query "mycorp.popular-posts" "SELECT * WHERE author = ?" ["alice"]
```

But that's painful. Better:

```dark
let popular : List<Mycorp.Post> =
  Projection.read Mycorp.Projections.PopularPosts {
    filter = (fun p -> p.author == "alice")
    sortBy = (fun p -> -p.viewCount)
    limit = 10
  }
```

The daemon compiles the filter/sort/limit into SQL. Same as
today's `RTQueryCompiler` for `Stdlib.DB.query`. Reuses
existing infrastructure.

## Testing custom projections

Standard Dark testing:

```dark
[<Test>]
let projectionBuildsCorrectly () =
  let ops = [Op.CreatePost ...; Op.IncrementView ...; ...]
  let result = Mycorp.Projections.PopularPosts.build ops
  result == [
    {id = ...; viewCount = 5};
    ...
  ]
```

Pure fn → pure test. No daemon needed.

Daemon-level integration tests run the projection in a real
SQLite + assert reads work. Few of these; the unit tests cover
most of it.

## Observability

For each registered projection, the daemon tracks:
- Last build time (timestamp, duration).
- Last build's op count (input).
- Output row count.
- Build error (if any) + error history.
- Stale-ness (ops behind master).

Surface in `dark projections list`:

```
NAME                       BRANCH    ROWS   STALE  LAST-BUILT
darklang.package-store     main      8421   0      2026-05-09 14:22
mycorp.popular-posts       main      100    132    2026-05-09 13:08
mycorp.user-post-stats     main      245    0      2026-05-09 14:22
```

Stale > N → daemon schedules a rebuild.

## Migration story

Today's projections are F#. The plan:

1. **Phase A.** Add the `Projection.register` machinery (Dark
   API + daemon hooks). F# projections still in place.
2. **Phase B.** Port the F# projections one-by-one to Dark
   equivalents. Run both side-by-side; assert outputs match.
3. **Phase C.** Retire the F# projections. Keep only the
   bootstrap projection for the package store.
4. **Phase D.** User-defined projections are documented and
   exposed.

Cost: ~3 weeks. Risk: medium (the package-store projection is
load-bearing; getting bootstrap right matters).

## Open questions

1. **Concurrent projection builds.** Two projections sharing a
   stream both want to rebuild after a batch of ops lands.
   Run sequentially? In parallel? In parallel with shared
   read-buffer of ops, separate writes — straightforward.
2. **Projection eviction.** The daemon's disk fills with
   projection files. LRU eviction; rebuild on read miss. Cheap
   to ship.
3. **What if a projection fn's body has a bug that returns an
   inconsistent snapshot?** (E.g., misses some ops). Detection:
   row count vs. expected. Mitigation: nothing automatic;
   daemon surfaces "anomaly" warnings the user can investigate.
4. **Projections of trace data.** Traces are high-volume. A
   "trace projection" that surfaces, say, "all errors in the
   last hour" — does the user want this rebuilt every minute?
   Every op? The rebuild cadence is part of the registration:
   `rebuildEvery = Duration.fromMinutes 1`. Daemon batches.
5. **Projection consumers in apps.** App reads its own
   projections through a typed API: `Mycorp.Projections.X.read
   ()`. The compiler ensures the projection is registered (a
   build-time check). Same as today's `Stdlib.DB.queryAll`
   shape but for projections.
6. **Back-pressure.** Op stream growing faster than projections
   can rebuild. Solution: daemon reports stale-ness; user
   decides whether to switch the projection to incremental, or
   accept staleness, or scale the daemon (more cores = more
   parallel builds).

## TL;DR

Projection-builders become Dark fns: `(List<Op>) → List<Row>`,
plus a `Schema` value. Bootstrap projection is F# (the
package-store one); everything else is Dark.

User-defined projections, hot-swappable, branch-aware, sand-
boxed. Performance is fine for typical loads with incremental
strategies for big streams.

The deeper point: **the daemon's job becomes "run Dark code on
behalf of users."** Projections, LSP, REPL, app handlers — all
the same model. F# is the platform; Dark is the substance.
That's the goal of this whole rewrite.

3-week migration. Worth it.
