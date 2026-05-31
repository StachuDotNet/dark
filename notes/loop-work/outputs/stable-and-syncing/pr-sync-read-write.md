# PR: Sync read + write

The spine's floor effort 7 — the heart of the floor, built from [sync.md](sync.md). Ops author
on one tailnet member and replay on another through the **same op-playback path** a local edit
uses. Localhost first, then over Tailscale.

> **Integration check — sync composes with the whole pre-S&S floor.** All of sync's F# (`Accounts`,
> `SyncCursors`, `Sync`) was merged onto `compose-check` alongside every foundation + capabilities
> (with the interpreter cap-gate) — including the divergence integrations (`Conflict.CSyncDivergence`
> + `Sync.detectDivergences`). Clean merge (sync only adds `LibDB` modules), builds clean, and the
> **full backend suite is green: 9,429 passed, 0 failed, 0 errored** (re-verified after the divergence
> work, which touched `createState`'s default dispatch — no regression). The integration run earlier
> **caught a real test-isolation bug** (the `Accounts` test asserted a *global* `accounts_v0` count
> unchanged, which races other tests' inserts in the parallel suite — the upsert logic was always
> correct; the assertion was scoped to a global count) — fixed by scoping the count to the test's
> unique login. So the whole floor's F# coexists and passes at scale; only the Dark HTTP/CLI surface
> remains.

**The reassuring part:** the apply path already exists on `main`. `PackageOpPlayback.applyOps
branchId commitHash ops` folds ops into projections, and writes are already **idempotent** —
`INSERT OR IGNORE INTO package_ops (id, …)` keys on the op id, so receiving an op twice is a
no-op. This PR mostly **exposes that over HTTP**; it doesn't reinvent apply.

> **Validated in prework** (`loop-fun:prework/sync-read-write`) — **4/4 sync tests PASS**. The
> single-op idempotency test (re-applying a seeded op via `Inserts.insertAndApplyOps` leaves
> `package_ops` count unchanged) plus three more that nail the round-trip safety property:
> - **Wire round-trip over the WHOLE log.** The receiver decodes a wire blob to a `PackageOp` and
>   **re-hashes** it — `insertAndApplyOps` sets the id to `computeOpHash op`. For `INSERT OR IGNORE`
>   to *dedup* a re-sent op instead of forking the log, that re-hash must reproduce the sender's
>   stored id. **Test: every op in the seeded log re-hashes to its stored id** across
>   serialize→decode→re-hash — so dedup-by-id is sound cross-instance, not just for a byte-identical
>   blob. (This is the property the single-op test only *implied*; now explicit over the full log.)
> - **Two fully-synced peers exchanging logs is a total no-op.** Driving the real receiver path
>   (`insertAndApplyOps` → `applyOps` refold) over the *entire* log, grouped by `(branch, commit)`,
>   adds **zero** rows — `INSERT OR IGNORE` dedups every op; `applyOps` only re-runs on
>   newly-inserted ops (none), so projections are untouched.
> - **Read cursor**: `opsSince 0` = whole log in ascending rowid order; `opsSince maxRowid` = empty.
>
> > **KEY FINDING — the store-param seam, now sized by a LITERAL cross-store test.** `LibDB`'s
> > *helpers* bind to a **single global connection** (`LibConfig.Config.dbPath` → a module-level
> > `Sql.connect`); `insertAndApplyOps`/`opsSince`/`computeOpHash` take **no store argument**. But
> > Fumble's `Sql.connect` takes a *connection string* — `connect` is config, not an open handle — so
> > targeting a second store is **only a different connString**. Proven directly (**5th sync test
> > PASSES**): read the op log from store A (the global DB) and apply it to a **separate temp SQLite
> > file** (store B) via `INSERT OR IGNORE` on B's own `SqliteConnection`; B mirrors A, and a
> > re-transfer adds nothing — **dedup by id holds across two real stores.** So:
> > - **The op-LOG layer is trivially store-parameterizable** — the canonical synced table
> >   (`package_ops`) moves between stores with just a connString; the wire at the op level is small.
> > - **What's still global is the PROJECTION refold** (`applyOps` → `package_functions`/etc.), which
> >   uses the module-level connection. *That* is the real `LibDB`-as-pluggable-backend refactor (the
> >   LibPM seam below + ops-projections flag the same thing) — threading a store through the fold.
> >
> > So the HTTP receiver can already append remote ops to a chosen store's `package_ops`; making the
> > *refold* target that store is the remaining work. (F# details: this codebase's `List.groupBy`
> > returns a `Map` — chain `|> Map.toList`; `Microsoft.Data.Sqlite` is available to the Tests project
> > so a raw second `SqliteConnection` works.)
>
> **Architectural finding (Stachu's call) — op-playback should move to a `LibPM`.** Today
> op-playback lives in **`LibDB`** (`PackageOpPlayback`/`BranchOpPlayback`/`PackageManager`) and
> `applyOp` writes SQL *directly* — the fold is entangled with SQLite persistence; there is **no
> `LibPM`**. The fold ("what an op does to state") wants to be **storage-agnostic in a `LibPM`**,
> with `LibDB` as just the SQLite backend. **Prototyped + sized (compiles):** the seam is a
> **~6-method `PackageStore` interface** (`addType`/`addValue`/`addFn`/`setName`/`deprecate`/…),
> and **the existing handlers fit it with ZERO changes** (`sqliteStore = { addType = applyAddType;
> … }` builds as-is); a storage-agnostic `dispatchVia store op` covering **all** op kinds compiles.
> So the refactor is a **clean interface-extraction — moderate, not "huge"**: move `dispatchVia` +
> `PackageStore` to a new `LibPM`, leave `sqliteStore` in `LibDB`. **Precise sizing:** of 8 op
> kinds, **7 routed cleanly** through the store from the start (`addType`/`addValue`/`addFn`/
> `setName`/`deprecate`/`undeprecate`; `PropagateUpdate` is a no-op), and `RevertPropagation` was
> the one rough edge — its logic lived *inline in `applyOp`* (not a handler).
> **DONE (prework, `LibDB` builds clean):** that inline block is now extracted to a private
> `applyRevertPropagation` and added to the store as `store.revertPropagation`; `dispatchVia`'s
> `RevertPropagation` case routes through the store like every other kind — **no delegation back
> to `applyOp`**. So **all 8 op kinds are now storage-agnostic** and the seam is *complete*: the
> `PackageStore` interface is 7 methods, `sqliteStore` is exactly the existing 7 handlers with
> zero behavioral change, and `dispatchVia` is a self-contained fold with no SQL. The only
> remaining LibPM work is the mechanical lift-and-shift (move `PackageStore` + `dispatchVia` into a
> new `LibPM` project, leave `sqliteStore` in `LibDB`) — **no design risk left**. Worth doing
> before/alongside sync.
>
> **Store-param REALIZED via the seam (prework) — the cross-store fold is now production, not a
> test re-impl.** Added **`connStore connStr`**: a *second* `PackageStore` whose Add* handlers write
> to a **given** connection string (`Fumble.Sql` fully-qualified to bypass the global wrapper),
> *additive* (the global `sqliteStore` + op-playback path are untouched → no regression). Then
> **`dispatchVia (connStore connStrB) op`** folds any Add* op into store B through the same dispatch.
> **Tested (LibPmSeam 3/3):** folding a real `AddFn` into a separate temp store reproduces store A's
> `package_functions` `pt_def` byte-for-byte → B resolves the same fn. So the seam doesn't just
> *size* the LibDB-as-backend refactor — a working connection-parameterized store drops straight
> into it. **`connStore.setName` is now parameterized too** (`setNameToConn` mirrors `applySetName`'s
> three steps against the target connStr), so `dispatchVia (connStore connB)` folds **AddFn +
> SetName** into store B and **B resolves the name→hash** — tested (LibPmSeam 4/4): a folded
> `TestPeer.Sync.foldedFn` resolves to the fn's hash via B's `locations`. So "B resolves the same
> **name**→hash as A" is realized in production code (content *and* name resolution), not just the
> content row. `deprecate`/`undeprecate` are now parameterized too (LibPmSeam 5/5: a folded `Deprecate(Obsolete)`
> lands one current `deprecated` row in store B). So **6 of 7 `PackageStore` methods are
> connection-parameterized** — `dispatchVia (connStore connB)` folds content (`package_functions`),
> name resolution (`locations`), AND deprecation state into a chosen store. Only the complex
> multi-statement `revertPropagation` remains — the last follow-up; everything the floor needs to
> replicate a peer's resolvable state is covered.

**Goal.** A peer can `GET` ops since a cursor and `POST` ops; the receiver applies them via the
existing playback path. A remote op and a local op are the same thing — no separate import path.

**Prereqs.** ops⊥projections (effort 3 — `core.db` is what syncs) and Tailscale transport
(effort 5 — addressing + the `Tailscale-User-Login` header). Conflict-dispatch (4) handles
`SyncDivergence`, but the floor can ship before rich resolution (last-writer / surface-as-data).
**Identity (8) is *not* a prereq — but the thin slice is smaller than "no mapping," and the
schema pins exactly what it is** (prework-verified against `backend/migrations/schema.sql`):
- **Authorship is at the *commit*, not the op.** `package_ops` has **no `account_id` column**
  (`id, op_blob, branch_id, commit_hash, applied, propagation_id, created_at`); the author lives
  on `commits.account_id`, a **UUID FK → `accounts_v0(id)`**. So you *cannot* "store the raw login
  as the author id" — `account_id` is a UUID, not a string. The first proof therefore does the
  minimal real binding: **upsert `accounts_v0` by the login string** (its `name` column is
  `UNIQUE`) and use the resulting UUID as the commit's `account_id`. That's not a new table — it's
  `accounts_v0` itself, keyed by name — so it's still "thin," just not "none." **BUILT + tested
  (prework):** `LibDB.Accounts.upsertAccount login : Task<Guid>` does `INSERT OR IGNORE INTO
  accounts_v0 (id, name)` then `SELECT id WHERE name` — idempotent, so a repeat login returns the
  **same** id with no duplicate (`accountIdForLogin` gives the read-only lookup). **2/2 Accounts
  tests pass**: count grows by 1 on first sight, a repeat upsert returns the same id and adds no
  row, and distinct logins get distinct ids. So the receiver can bind a `Tailscale-User-Login` →
  `account_id` on receipt with no new table and no PT change.
- **Per-op `Intent` is the *later* depth, not the slice.** Attribution *inside* each op (the
  `Intent` of [identity.md](../later/identity.md)) means adding an `Intent` field to the serialized
  `PackageOp` — a **ProgramTypes change** (hash-affecting, two-build dance), explicitly out of the
  floor. The floor attributes at commit granularity via `accounts_v0`; effort 8 adds the
  structured per-op `Intent`.

This is why sync (7) can still precede identity (8): the floor needs only the `accounts_v0`
upsert (no PT change), and `main` already attributes commits via `commits.account_id`.

## The wire flow

```
 author on A ──edit──► package_ops (A)         B polls / A pushes
      │                     │                        │
      │   GET /sync/events?since=<cursor> ───────────┤  ops as serialized PackageOp blobs
      │   POST /sync/events  ◄───────────────────────┘
      ▼                                               ▼
 (nothing derived on the wire)            INSERT OR IGNORE into package_ops (B)  ← idempotent
                                          applyOps → projections refold (B)      ← existing path
```

The wire carries **serialized `PackageOp`/`BranchOp` blobs** (exactly what `op_blob` already
stores) + authorship — never derived state. Receivers regenerate projections locally.

A **new** tailnet member joining (relevant now that scope is tailnet-wide) first
`GET /sync/snapshot` once — a consistent point-in-time read — to bootstrap, then polls
`/sync/events` from the snapshot's cursor. Existing peers only ever do the incremental poll.

## .fs changes

| File (on `main`) | Change |
|---|---|
| `LibDB/Inserts.fs` (or a `Sync.fs`) | **`opsSince (cursor: int64) : Task<List<rowid * id * op_blob>>`** — `SELECT rowid, id, op_blob FROM package_ops WHERE rowid > @cursor ORDER BY rowid ASC` (**prototyped + tested**). The receiver is the *existing* `Inserts.insertAndApplyOps` (`INSERT OR IGNORE INTO package_ops` + `applyOps`) — apply already exists; no new write fn needed. |
| `LibDB/` schema | **No `seq` column needed** (prework finding). `package_ops`'s PK is `TEXT`, so SQLite keeps an implicit **`rowid`** that *is* a monotonic insertion cursor — use it for `?since=`. (`created_at` alone isn't a total order, but rowid is — and free.) **No migration for the cursor.** |
| `Builtins.Http.Server` | No change — the endpoints are **Dark HTTP handlers** (below) on the existing server. |

**ProgramTypes:** untouched as a *type*, but the **`PackageOp` serialization is the wire
format**, so its encoding must be stable across peers (a peer on an older encoding can't read a
newer op). This is the op-format-stability prerequisite the bootstrap doc names — flagged, not
solved here; the floor assumes same-version peers.

## .dark changes — the sync server + client are Dark

```fsharp
// Dark HTTP handlers (sync.md lists the endpoints) — one small App, cap-gated.
let syncEvents (req: HttpRequest) : HttpResponse =       // GET /sync/events?since=&branch=
  let author = req |> Tailscale.loginHeader              // first proof: the login IS the author
  Sync.opsSince req.branch req.since |> asOpStream       // (effort 8 maps login → account)
let postEvents (req: HttpRequest) : HttpResponse =       // POST /sync/events
  let r = Sync.applyRemoteOps (req.body |> decodeOps)
  { accepted = r.accepted; assigned = r.seqs; conflicts = r.conflicts }   // never blocks
```

Plus a `dark sync` CLI command (the poll: `GET /sync/events?since=cursor` → `applyRemoteOps` →
advance cursor) and `dark remote add <peer>` (writes the remote into `.darklang` settings).

> **The F# receiver/sender these handlers call is BUILT + tested** (prework — `LibDB.Sync`, the
> cohesive module tying the proven primitives together): `Sync.opsToSend cursor` = the GET read
> (`(rowid, id, op_blob)` via `opsSince`); `Sync.applyRemoteOps remote branchId commitHash
> opsWithRowids` = the POST receiver — `insertAndApplyOps` (idempotent) then `advanceCursor` to the
> **max sender-rowid** in the batch, returning the new cursor (empty batch = no-op). **8/8 sync
> tests** (`SyncIdempotency`): the receiver adds no row for an already-known op and advances +
> persists the remote's cursor; an empty batch leaves it unchanged. So the handlers are a thin Dark
> wrapper over a tested module, not new logic — and the `.fs` side of the floor is complete (read
> cursor, idempotent write, cross-store transfer, login→account authorship, resumable cursor, and
> the cohesive `Sync` API). What's left is the Dark HTTP/CLI surface (not F#-unit-testable here).

## SQL/schema

`package_ops`/`branch_ops` already exist and are canonical. The cursor is the **implicit
`rowid`** (no `seq` column — prework finding). The only new state is a small **`sync_cursors`**
table (per remote: `folded_through_rowid`) so a poll resumes where it left off — local-only,
not synced. No projection tables touched — they refold from the applied ops.

> **Built + tested (prework).** `LibDB.SyncCursors`: `sync_cursors (remote TEXT PK,
> folded_through_rowid INTEGER)`; `cursorFor remote` (0 if unseen) and `advanceCursor remote rowid`
> — a **monotonic** upsert (`ON CONFLICT DO UPDATE SET = MAX(existing, incoming)`) so a
> stale/duplicate advance can't rewind the cursor and re-fold older ops. Local-only; the table is
> created on demand (`CREATE TABLE IF NOT EXISTS`) so prework needs no migration (the real PR moves
> it into `schema.sql`). **+2 tests (SyncIdempotency 7/7):** the cursor starts at 0, advances, and
> ignores a backward advance; and **cursor + `opsSince` = a resumable poll** — fold the whole log,
> advance to the max rowid, and a re-poll from the cursor returns **no** already-folded ops. So the
> poll-resume bookkeeping the floor needs is proven against the rowid cursor.

## Test plan

| Step | Test | Done-signal |
|---|---|---|
| idempotent apply | `.fs`: `applyRemoteOps [op; op]` (same op twice) | second is a no-op (`INSERT OR IGNORE`); state identical |
| two-instance round-trip | `.fs` integration: author on A, `opsSince` from A, `applyRemoteOps` on B | B resolves the same name→hash as A — **DONE across two real stores:** the op-LOG transfer (A→B, idempotent dedup) *and* the PROJECTION refold (folding an `AddFn` into store B's `package_functions` reproduces A's `pt_def` byte-for-byte → B resolves the same fn). Finding: only the fold's *write* is connection-coupled (serialization is pure), so store-parameterizing it is a connString swap |
| cursor advances | `.fs`: POST 3 ops, assert `sync_cursors.folded_through_seq += 3` | cursor correct, no re-fold of old |
| auth maps | `.dark`/`.fs`: a request with `Tailscale-User-Login: x` lands ops authored by x's account | authorship correct — **`.fs` half DONE (Accounts 3/3):** `upsertAccount login → account_id`, a commit with that `account_id` attributes back to the login via the `commits.account_id → accounts_v0` join; only the Dark header-extraction remains |
| `.dark` end-to-end | `.dark` test: a fn defined via the sync handler is callable after apply | resolves + runs |

## CLI impact

New: **`dark sync`** (one-shot poll-pull-apply; the autosync PR wraps it in a loop), **`dark
remote add <peer>`**, **`dark remote list`**. Existing commands unchanged.

## UX change

```
$ dark remote add major          # major.tail-scale.ts.net
✓ remote 'major' added
$ dark sync
↓ pulled 4 ops from major (2 fns, 1 type, 1 rename) · applied · projections refolded
↑ pushed 1 op to major
```

Before: an edit on the desktop is invisible on the laptop. After: `dark sync` (or autosync)
makes it appear — the goal's first observable proof.

## Risks / problems not yet raised

- **Op-format stability.** Serialized `PackageOp` is the wire format; mismatched peer versions
  can't interop. Floor assumes same-version peers; long-term needs a versioned op encoding.
  *Concrete handle:* carry a **hash of the current PT shape** in the handshake (`/sync/whoami`)
  — peers compare PT-hashes and refuse / negotiate on mismatch, rather than corrupting on a
  silent encoding drift. (The versioned encoding is the real fix; the PT-hash is the cheap guard.)
- **Seq monotonicity across sources.** Each store's `seq` is local; a global order needs the
  `(timestamp, author_id)` tiebreak. Fine for a star (everyone orders against the hub).
- **Concurrent write during snapshot — SOLVED (prework).** Rather than a transaction/lock,
  `Sync.snapshot` derives the bootstrap cursor as the **max rowid among the ops it just read**
  (one `opsSince 0` query), not a separate `SELECT MAX(rowid)`. So (ops, cursor) is **consistent by
  construction** — never torn: a write landing *after* the read gets a higher rowid and is picked up
  on the peer's first incremental poll from the snapshot cursor. **9/9 sync tests** (the snapshot =
  the whole log, its cursor = its own max rowid, and a poll from that cursor returns nothing already
  included). No transaction needed.
- **Conflict volume tailnet-wide — surfacing is BUILT.** N authors → more `SyncDivergence`; the
  floor surfaces them (never blocks the POST) and defers rich resolution to the conflict-dispatch
  policy. **Prework:** `Sync.detectDivergences branchId ops` does exactly this — for each incoming
  `SetName` it queries `locations` for the current (`unlisted_at IS NULL`) binding and, if a
  *different* `item_hash` is already there, records `(location, existingHash, incomingHash)` as
  **data** (never blocking the apply). A higher layer turns each into `Conflict.CSyncDivergence`,
  which flows through the policy (strict default fails loudly; a last-writer policy `Substitute`s).
  **Tested (SyncIdempotency 10/10 + ConflictDispatch 15/15):** a rebind to a different hash is
  flagged with both hashes; a same-hash rebind is not; and the conflict resolves via the policy. So
  the *whole* divergence path — detect (LibDB) → conflict type (LibExecution) → resolve (policy) —
  is built and tested at the F# level; only the Dark handler that wires detection to the policy and
  returns the surfaced conflicts in the POST response remains.

## Above / below

- **Below:** ops⊥projections (`core.db`), Tailscale transport, conflict-dispatch (for
  `SyncDivergence`).
- **Above expects:** autosync (effort 9) wraps `dark sync` in a poll loop; identity (8) supplies
  the login→account mapping; print-md-as-an-App (10) is the thing whose edits ride this wire.
