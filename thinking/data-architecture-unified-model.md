# The unified model — ops, projections, conflicts, resolutions

The previous two docs (`data-architecture-rewrite.md`, `data-architecture-networking.md`) treat each kind of state as its own design problem: packages have ops, traces have rows, sessions have a separate DB, app data has its own world. That's wrong. **Every kind of state in Dark is the same shape: a stream of ops, a projection derived from that stream, occasional conflicts when two ops disagree, and resolutions that name winners.** Internalizing this collapses ~5 separate subsystems into one.

This doc replaces the "what syncs, what doesn't" thinking in both prior docs. The answer is: **everything sync-able is a stream of ops, and the streams all use the same machinery.** Whether a stream actually syncs is a per-stream config, not a categorical distinction.

Includes the user's note that **trace data and sessions probably need to sync too**, and the user's framing that **everything is ops/projections/conflicts/resolutions**.

---

## 1. The four primitives

```
            ┌─────────────┐                           ┌─────────────┐
ops  ──────►│   PROJECTION│   queries (read-only) ──► │ application │
appended    │  rebuild on │ ◄──── reads ──────────── │ (e.g., the  │
            │ schema drift│                           │  package    │
            │  catch-up on│                           │  manager,   │
            │  new ops    │                           │  the trace  │
            └─────────────┘                           │  viewer,    │
                  ▲                                   │  the CLI)   │
                  │                                   └─────────────┘
                  │
                  │  conflict detected when two ops disagree on the same key
                  │
            ┌─────┴───────┐
            │ RESOLUTION  │ ── written as another op into the stream
            │  (auto or   │
            │  human)     │
            └─────────────┘
```

**Op.** A small, immutable, content-addressed unit of change. Carries `(stream, key, payload, timestamp, origin)`. Has a hash. Identical ops dedup. Once written, never edited; only superseded by another op.

**Projection.** A derived view, optimized for queries. Rebuilt by replaying ops. Has a high-watermark pointer into the stream so it can catch up incrementally. Schema-versioned; on drift, drop and rebuild.

**Conflict.** Two ops in the stream that target the same logical "key" with different content. The system *detects* conflicts during projection-replay; the projection records them as well-known rows.

**Resolution.** An op whose payload is "for this conflict, pick winner X" (or "merge per this rule"). Resolutions are themselves ops in the stream — same shape, same sync, same idempotence. Auto-resolutions are written by Dark code (e.g., LWW); human resolutions are written by the user clicking a button.

That's it. Four primitives. Everything else is a specialization.

---

## 2. Every state in Dark, through this lens

Here's the inventory. Each row says: what's the stream, what's the projection, what counts as a conflict, what's the typical resolution.

| Subsystem | Stream | Projection | Conflict | Typical resolution |
|---|---|---|---|---|
| **Package edits** | `package_ops` | `pkg.db` (per-branch) | Two `SetName(loc)` ops with different hashes | Last-writer-wins on `created_at`; user can override |
| **Branch lifecycle** | `branch_ops` | `branches` table | Two `RenameBranch(id)` to different names | LWW |
| **Deprecations** | `package_ops` (PropagateUpdate, MarkHarmful) | `deprecations` table | n/a typically | n/a |
| **Trace events** | `trace_ops` (NEW: each fn call is an op) | `traces.db` | n/a (append-only by nature) | n/a |
| **Sessions** | `session_ops` (NEW: create/update/end ops) | `sessions.db` | Two devices update same session simultaneously | LWW per field |
| **App user data** | `app_ops` (NEW, opt-in: each `DB.set` is an op) | `data.db` (per-app) | Two devices `DB.set("k", ...)` to different values | LWW per key, or app-defined merge fn |
| **Sharing** | `share_ops` (lives at the hub, but same shape) | `share_grants` table | Concurrent grant + revoke | LWW |
| **Account/profile** | `account_ops` (handle change, email change) | `users` row | Concurrent profile edits across devices | LWW per field |

Once you frame it this way, the question "what syncs?" becomes "which streams have sync enabled?" — a per-stream config, defaulting to "yes" for most:

- `package_ops`: yes (this is the headline use case)
- `branch_ops`: yes (branches need to follow you across devices)
- `trace_ops`: yes for "important" traces, no for noise (see § 5)
- `session_ops`: yes (your tmux follows you)
- `app_ops`: opt-in per app (some apps want sync, some are local-only)
- `share_ops`: yes (granted by hub, replicated to instances)
- `account_ops`: yes (your account is yours wherever you log in)

There's no longer a categorical "this kind of state syncs, this kind doesn't." It's all per-stream, and the default is "sync."

---

## 3. The storage primitive: the op store

Concretely, in the daemon there's one module that everything else builds on:

```fsharp
module LibOps.Store

/// A logical stream of ops. Each subsystem has at least one stream.
type StreamName = string  // e.g., "packages", "branches", "traces", "sessions", "app:<id>"

/// An op as it lives on disk and on the wire.
type Op = {
  hash: Hash                  // content-addressed; full hex
  stream: StreamName
  key: string                 // the logical "thing" this op affects (location-id, branch-id, trace-id, session-id, app-key, ...)
  payload: byte[]             // serialized op-specific shape
  parent_op: Hash option      // previous op on this (stream, key); for conflict detection
  created_at: int64
  origin_instance: InstanceId
  signature: byte[]           // signed by origin_instance's key
}

/// One SQLite file backing the op store. Schema is fixed across all streams.
let opsDbPath = Config.opsDbPath  // ~/.darklang/ops.db

let appendOp (op: Op) : Task<bool> = ...     // returns false if already present
let opsForStream (stream: StreamName) (since: Hash option) : IAsyncEnumerable<Op> = ...
let opsForStreamAndKey (stream, key) : IAsyncEnumerable<Op> = ...
let detectConflicts (stream, key) : Task<List<Conflict>> = ...
let watermark (stream: StreamName) : Hash option = ...
```

Schema:

```sql
-- Single ops.db. One table for ALL streams. (We can shard later by stream
-- if write contention matters; for now, one table is correct and fast.)
CREATE TABLE ops (
  hash             TEXT PRIMARY KEY,
  stream           TEXT NOT NULL,
  key              TEXT NOT NULL,
  payload          BLOB NOT NULL,
  parent_op        TEXT,                    -- NULL for first op on (stream,key)
  created_at       INTEGER NOT NULL,
  origin_instance  TEXT NOT NULL,
  signature        BLOB NOT NULL
);
CREATE INDEX idx_ops_stream_created   ON ops(stream, created_at);
CREATE INDEX idx_ops_stream_key       ON ops(stream, key, created_at);
CREATE INDEX idx_ops_origin           ON ops(origin_instance);

-- Per-(stream, key) conflict detection: count parents that themselves have
-- multiple children. Materialized lazily on read; we don't write a conflicts
-- table because resolutions are themselves ops and we can compute "is this
-- conflict still open?" from the stream.

-- Sync watermarks: per peer, how far we've pulled per stream.
CREATE TABLE sync_watermark (
  peer_instance TEXT NOT NULL,
  stream        TEXT NOT NULL,
  last_pulled   TEXT,
  last_pushed   TEXT,
  PRIMARY KEY (peer_instance, stream)
);

-- Stream config (sync enabled, retention, encryption).
CREATE TABLE stream_config (
  stream         TEXT PRIMARY KEY,
  sync_enabled   INTEGER NOT NULL DEFAULT 1,
  retention_days INTEGER,                   -- NULL = forever
  encryption_key TEXT                        -- per-stream key, wrapped under user master key
);

-- Blobs (large payloads referenced by ops).
CREATE TABLE blobs (
  hash       TEXT PRIMARY KEY,
  length     INTEGER NOT NULL,
  bytes      BLOB NOT NULL,
  created_at INTEGER NOT NULL
);
```

Three tables of substance: `ops`, `sync_watermark`, `stream_config`. Plus `blobs` for content-addressed payloads.

This replaces the prior doc's `package_ops` + `branch_ops` + (implicit) trace storage + (implicit) session storage + (implicit) app data storage with **one table**. 

Not because monoliths are good — but because **the layer that handles ops as ops doesn't need to care what kind of op**. Detection, sync, dedup, signing, idempotency: all per-op-blob, stream-agnostic. The stream column is just a routing key.

---

## 4. Projections — one builder per stream

A projection is a code module that knows how to fold a stream of ops into a SQLite file optimized for its queries. The shape is uniform:

```fsharp
module LibProjection.Builder

type ProjectionBuilder = {
  projectionName: string                              // "pkg", "traces", "sessions", "app:<id>", ...
  fileFor: ProjectionKey -> string                    // path on disk, e.g., projections/<branch>/pkg.db
  schema: SqliteConnection -> Task<unit>              // CREATE TABLE for this projection
  applyOp: SqliteConnection -> Op -> Task<unit>       // fold one op
  schemaVersion: int                                  // bump to force rebuild
  detectConflicts: SqliteConnection -> Op -> Task<List<Conflict>>
}

/// Daemon-side: open or build the projection. Same code path for everyone.
let openOrBuild (b: ProjectionBuilder) (key: ProjectionKey) : Task<SqliteConnection> = ...

/// The pkg projection (per-branch package state): replay all package_ops
/// for the branch chain, populating pkg.db tables.
let packageProjection : ProjectionBuilder = ...

/// The traces projection (per-branch): replay trace_ops, populating
/// traces.db tables.
let tracesProjection : ProjectionBuilder = ...

/// The sessions projection (global): replay session_ops, populating sessions.db.
let sessionsProjection : ProjectionBuilder = ...

/// Per-app data projection: replay app_ops for one app, populating data.db.
let appDataProjection (appId: AppId) : ProjectionBuilder = ...
```

Every `applyOp` is small. Every `schema` is small. Every projection rebuilds in seconds-to-minutes from cold. The complexity isn't anywhere — it's distributed across many tiny ProjectionBuilders, each ~50-200 lines.

The ProjectionBuilder concept replaces ~3000 lines of `LibDB/PackageOpPlayback.fs` + `LibDB/BranchOpPlayback.fs` + `LibDB/Tracing.fs` + (would-have-been) sessions code + (would-have-been) app-data code, with five 200-line ProjectionBuilders sharing one ~400-line core.

---

## 5. Traces as ops

This is the thing the user pointed at. Currently `LibDB/Tracing.fs` (594 lines) writes directly to `traces` and `trace_fn_calls` tables. That's a projection, but with no underlying op log — so it can't sync, can't be replayed, can't be shared.

Reframe: every trace event is an op.

```fsharp
type TraceOpPayload =
  | StartTrace of trace_id: TraceId * root_tlid: TLID * input: Dval * session_id: SessionId option
  | RecordCall of trace_id: TraceId * call_id: CallId * parent: CallId option * fn_hash: Hash * args_json: string * result_json: string * duration_ms: int64
  | RecordExprValue of trace_id: TraceId * call_id: CallId * expr_id: ExprId * value_json: string
  | EndTrace of trace_id: TraceId * status: TraceStatus
```

Stream: `"traces"`. Key: `trace_id`. Each event appends one op. The traces.db projection's `applyOp` is exactly today's insert code — but driven by a stream rather than direct calls.

What this gets us:

- **Traces sync.** A trace recorded on `stachu-laptop` shows up on `stachu-major`. Now `dark traces tail` from any device sees in-progress traces from all devices.
- **Traces share.** "Look at this bug, here's the trace" is a single grant. The recipient pulls the trace ops from your stream and gets a faithful replay.
- **Traces survive instance reset.** Today, deleting `data.db` loses traces. With ops stored in `ops.db`, you can drop the projection and rebuild.
- **Traces are time-travel-able.** A trace IS its op stream — replaying with different projection logic gives you different views (e.g., a "show me only impure calls" projection).

Cost: write throughput. Today's tracing writes directly to a single table; with ops indirection, every trace event is an op-table append + a projection-table append. SQLite WAL handles this fine for normal dev usage (hundreds of events/sec), but a high-throughput app could pile up trace ops.

Mitigations:
- Per-stream retention (`stream_config.retention_days`): traces older than 7 days dropped automatically.
- Selective sync: `sync_enabled=0` on `traces` stream by default for now; user opts in. A trace explicitly marked "shared" gets its own per-key sync grant.
- Coalescing: bunch many `RecordCall` ops into a single batch op (`RecordCalls of List<...>`) — same shape, fewer rows.

Result: trace storage gets unified with everything else, gains sync and sharing, costs slightly more on write (acceptable).

---

## 6. Sessions as ops

Sessions today are imagined as a small local SQLite. The user's note: those should sync too.

Reframe:

```fsharp
type SessionOpPayload =
  | CreateSession of session_id: SessionId * name: string * intent: string * branch_id: BranchId * cwd: string
  | UpdateSession of session_id: SessionId * field: SessionField * value: string
  | AttachTrace of session_id: SessionId * trace_id: TraceId
  | EndSession of session_id: SessionId
  | SetSessionEnv of session_id: SessionId * key: string * value: string
```

Stream: `"sessions"`. Key: `session_id`. Projection: `sessions.db`.

What this gets us:

- **Sessions follow you.** Start a session on the laptop with `--intent="fix Cli completion"`; resume on the desktop. The desktop's daemon receives the session ops, projects them into its local sessions.db, and `dark session list` shows it.
- **Session-attached traces follow too.** When a session has an `AttachTrace`, the trace's ops get pulled too (assuming sync is enabled for that trace's key). The Bug-In-Progress flow — "I was debugging this on my laptop, want to continue from my big PC" — Just Works.
- **Sessions can be shared.** Pair-debugging: Stachu shares his session with Feriel. She sees its branch, its intent, its attached traces — exactly the state Stachu has. She can fork a session off it (her own session_id, with `parent_session_id` in its op) and work in parallel.
- **Conflict semantics are clear.** Two devices both `UpdateSession(s, "intent", ...)` concurrently: LWW per field. The op stream records both; the projection shows the latest; nothing is lost (the older op is still readable in the stream).

Sessions stop being an afterthought. They're real, distributed, shareable.

---

## 7. App user data as ops (opt-in)

App data is the trickiest because of write throughput. A high-traffic Dark-served HTTP app might do 1000 `DB.set` per second, and turning each into an op is real work.

The right move: **default-on for low-traffic apps; opt-out for hot paths; mode is per-table not per-app.**

```dark
// Default: ops-modeled, syncable, time-travelable
let users : Datastore<User> = Datastore.declare "users"

// Performance escape hatch: direct SQLite, no op log, local-only
let metrics : LocalDatastore<Metric> = Datastore.declareLocal "metrics"
```

For ops-modeled tables, every `DB.set/get/delete` builtin emits an `app_op` — `SetKv(table, key, blob)`, `DeleteKv(table, key)`, etc. Stream: `"app:<app_id>"`. Projection: `data.db`. Conflicts (two devices SET the same key concurrently): default LWW per key; app authors can register a Dark merge fn for richer semantics:

```dark
// In app code
let mergePolicy : Dict<String, MergeFn> =
  { "users" = User.mergeLastWriteWins
    "counters" = Counter.mergeAddDeltas        // CRDT-style merge
    "documents" = Document.mergeOpTransform }  // OT-style if user wants
```

Apps that don't care about sync (single-device tools, CI scratch, personal notepads) declare `LocalDatastore` and pay no overhead.

What this gets us:

- **Apps follow the user.** Start a TODO app on laptop, see your TODOs on phone. Without writing sync code.
- **Apps share.** Multi-user apps emerge naturally — a shared TODO list is the same TODO app, with two users granted to its data stream.
- **Apps time-travel.** "What was this user's profile last Tuesday?" — replay the app's stream up to the timestamp.
- **Apps explain themselves.** The op log IS the audit log. "Who set this field?" is an `origin_instance` lookup.

This is what the user meant by "val.town but better." Val.town gives you SQLite. We give you SQLite that automatically syncs, conflicts cleanly, audits itself, and survives device migration. None of that is implementable without a uniform op model underneath.

---

## 8. Conflicts and resolutions, made concrete

The four-primitive model is only useful if conflicts and resolutions are concrete enough to implement. Here's the cut.

### Detection

A conflict on `(stream, key)` exists when there are two ops `o1` and `o2` such that `o1.parent_op == o2.parent_op` (or both NULL) and `o1.hash != o2.hash` and neither is a descendant of the other in the per-key DAG.

This is a graph property. Computed during projection replay: when applying `o`, check whether another op with the same parent already exists for this key. If yes: this key has a conflict.

### Conflicts as data

A projection can carry a `conflicts` table:

```sql
CREATE TABLE conflicts (
  stream   TEXT NOT NULL,
  key      TEXT NOT NULL,
  op_hash  TEXT NOT NULL,                -- the conflicting op (one row per branch of the conflict)
  PRIMARY KEY (stream, key, op_hash)
);
```

`dark status` queries this table and surfaces "you have N conflicts." `dark resolve <stream> <key>` shows the diverging ops and asks the user to pick (or runs auto-resolution).

### Resolutions are ops

A resolution is just another op:

```fsharp
type ResolveOpPayload = {
  resolves_stream: StreamName
  resolves_key: string
  conflicting_ops: List<Hash>     // the ops that were in conflict
  winner: Hash                    // which op wins (could be a brand-new op too)
  reason: ResolveReason            // 'auto-lww' | 'auto-merge-fn:<fn>' | 'human'
}
```

Stream: same stream as the thing being resolved (or a parallel `resolutions` stream — design decision; same-stream is simpler).

When the projection encounters a `Resolve`, it updates its `conflicts` table to mark this conflict resolved, and updates the main projection rows to reflect the winner. The old ops aren't deleted; they're just no longer "current."

This makes resolution **distributed and agreement-driven** without CRDTs:
- Auto-resolutions (LWW) run on every device, deterministically — they all produce the same `Resolve` op (content-hashed, so writing it on multiple devices dedups).
- Human resolutions are explicit user actions — only one device writes the op; sync propagates.
- Custom merge fns are Dark fns running on a chosen device (typically "the device that received the second op"). Same — they produce a deterministic Resolve op.

This subsumes the "conflicts and resolutions" notes in `~/vaults/.../Ops and Playback/` cleanly. The vault has been talking about Conflicts and Resolutions as separate types; here they collapse into "Conflicts are projection-level data; Resolutions are just ops."

### What about merges that need a third op?

Sometimes resolution isn't "pick A or B" but "produce C from A and B" (e.g., text 3-way merge of a function body). Easy: the resolution op carries C as its own `payload`, and the resolution itself is the op that defines the new content. This is exactly the same shape; just `winner` is a freshly-minted op rather than one of the conflicting ones.

---

## 9. Sync, restated

In the networking doc, sync was described as "push package ops, pull package ops." With the unified model, it's:

```
Sync session = for each enabled stream, push my new ops on it, pull peer's new ops.
```

The hub doesn't care which streams. The protocol is one shape:

```
TO { to: peer, payload: SyncRequest { streams: [...], since: { stream → last_hash } } }
FROM { from: peer, payload: SyncResponse { ops_by_stream: { stream → [op...] } } }
```

For a typical "stachu-laptop ↔ stachu-major" sync, the request says:

```
streams: ["packages", "branches", "sessions", "traces", "app:my-blog"]
since: { packages: <hash>, branches: <hash>, sessions: <hash>, traces: <hash>, app:my-blog: <hash> }
```

One round-trip pulls all the new state across all subsystems. The daemon then catches up the relevant projections (pkg.db for packages and branches, sessions.db for sessions, per-branch traces.db for traces, per-app data.db for app data).

Per-stream sync flags (`sync_enabled` in `stream_config`) control which streams are included. A user who's bandwidth-constrained can disable trace sync; a user who wants only their packages on a given device can disable everything else. The default is "sync everything that has sync enabled by default" (packages, branches, sessions, account; opt-in for traces and most app data).

Cross-user sharing reuses this exactly: a `share_grant` says "user X may sync stream Y, key Z" — and the hub enforces the grant when relaying ops between instances of different users.

---

## 10. What this changes about the prior docs

Concretely:

### Replaces in `data-architecture-rewrite.md`:

- **§ 4 (master DB schema):** the `ops` table replaces the separate `package_ops` and `branch_ops` tables. `stream_config` and `sync_watermark` join in. Same total complexity; uniformly applied.

- **§ 7 (Trace DBs):** stays — but `traces.db` is now a *projection*, with ops in the master `ops.db` under stream `"traces"`. Drop and rebuild works the same.

- **§ 9 (Sessions):** stays in spirit — `sessions.db` is a projection — but session ops live in master `ops.db` under stream `"sessions"`, so sessions sync.

- **§ 10 (Sync):** is now per-stream sync; networking transport is the hub design from the networking doc.

- **§ 16 ("What dies"):** `LibDB/Tracing.fs` shrinks dramatically (becomes a ProjectionBuilder); session-storage code never gets written as a separate thing; per-app data routing is just another ProjectionBuilder.

### Replaces in `data-architecture-networking.md`:

- **§ 5 (Sync):** sync transport is per-stream; the hub frames are stream-tagged. Hub doesn't care which streams.

- **§ 14 ("What this means for the rest of the architecture"):** instead of "sessions are local-only" and "trace are local-only," sessions and (selected) traces ride the same sync as packages.

The unified model doesn't change the *outline* of either prior doc; it changes the *implementation* by collapsing several near-duplicate subsystems into one.

---

## 11. Why this matters more than it might seem

Three reasons:

### a. New features for free

Once you have the op store + projection builders + per-stream sync, **adding a new kind of state is a one-day project**. Want to track which branches each user has open? `branch_views` stream, projection updates a "currently-open" set. Want to add "favorites"? `favorites` stream. Want collaborative cursor positions in a future editor? `cursor_positions` stream — emit positions as ops, project into a presence table. Each new feature inherits sync, sharing, conflict resolution, audit, time-travel, and crash recovery.

This is the thing the vault has been gesturing at across many notes: "everything in dark should be data," "everything is just ops." Make it real and you stop writing storage code.

### b. The product story unifies

We can describe Dark in one sentence: **"Dark stores everything you do as a stream of ops, projects them into queryable views, and syncs the streams between every place you have Dark."** Code changes, traces, sessions, app data, account changes — all the same machinery. The user only ever learns this once.

### c. Conflict UX gets one design

Today the vault has separate sketches of "package conflict UI," "branch merge conflict UI," would-have "data merge conflict UI," etc. Unified: there's one conflict view, one resolve flow, one set of merge primitives. `dark conflicts` lists every open conflict across every stream. `dark resolve <id>` works the same regardless of subsystem. The user learns the model once, applies it everywhere. Same for the API surface in Dark code: one `Stdlib.Ops.Conflicts.list` rather than five domain-specific cousins.

---

## 12. The cost of the unification

It's not free.

- **Write throughput.** Every state change becomes an op. For low-throughput state (sessions, package edits), zero noticeable cost. For high-throughput (traces, hot app data), the per-op overhead matters. We'll need batch ops, careful indexing, and the per-table `LocalDatastore` opt-out for hot paths.

- **Schema discipline.** Every subsystem has to define its op shapes carefully and version them. We have one binary serialization layer for this already (`LibSerialization.Binary`); now it serves *every* op type, not just package ops. We need a clear convention for "how do I add a new op type without breaking older deserializers."

- **Conflict semantics need real attention.** The framing "everything is ops/projections/conflicts/resolutions" sounds clean, but conflicts in app data are genuinely harder than conflicts in package edits. We need a clear story for each subsystem's conflict model — not a uniform "LWW everywhere," because LWW is wrong for some shapes (counters, sets, text).

- **Mental shift for daemon engineers.** "Just write to the table" is no longer the API; "emit an op, projection rebuilds" is. That's a step up in abstraction. The first few times we write a feature in this model it'll feel like overkill.

---

## 13. Updated phasing

The unified model changes how the slices in the prior docs flow. New ordering:

1. **Slice 0-3** stay (establish ops.db, split user data, build projection-per-branch for packages).
2. **Slice 3.5 (NEW): introduce the unified op store.** Generalize `package_ops`/`branch_ops` into `ops` with a `stream` column. Migrate existing rows. Update sync watermarks.
3. **Slice 4 (revised): unified projection builder.** Refactor `PackageOpPlayback` into a ProjectionBuilder. Add the trace ProjectionBuilder. Wire sessions.
4. **Slice 5 (daemon)** unchanged — the daemon now uses the unified store.
5. **Slice 6 (apps):** apps are first-class, with default ops-modeled data and `LocalDatastore` opt-out.
6. **Slice 7 (sessions):** sessions are an op stream from day one (no separate `sessions.db` shape needed).
7. **Slice 8 (sync):** the hub WS protocol carries per-stream frames. Per-stream sync flags. Cross-user share grants are stream+key scoped.
8. **Slice 9 (conflicts):** unified conflict listing + resolve flow across all streams.

This shaves time off slices 6 and 7 (no separate session storage / app data storage to design), and adds slice 3.5 (the unification work), netting roughly even on calendar but **massively** simpler conceptually for everyone working on the codebase from then on.

---

## 14. Summary

- Everything in Dark is an op. Storage is one `ops` table, keyed by content hash, with a `stream` column.
- Every queryable view is a projection — a SQLite file rebuilt by replaying ops in a stream. Disposable, schema-versioned, per-key kill-and-fill.
- Conflicts are detected during projection replay; surfaced as data; not blocking.
- Resolutions are themselves ops. Auto (LWW, app-defined merge fns) or human. Distributed by sync.
- Sync is per-stream, transport-agnostic. The hub from the networking doc routes stream-tagged frames.
- Traces, sessions, app data — all become ops streams. They sync, they share, they survive resets, they time-travel.
- The cost is some write overhead and serious attention to per-subsystem conflict semantics. Both manageable.

The product story collapses to: **Dark stores everything you do as ops, projects them into useful views, and syncs them everywhere you have Dark.** That's the whole thing.
