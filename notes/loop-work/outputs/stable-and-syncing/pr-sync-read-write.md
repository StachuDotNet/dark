# PR: Sync read + write

The spine's floor effort 7 — the heart of the floor, built from [sync.md](sync.md). Ops author
on one tailnet member and replay on another through the **same op-playback path** a local edit
uses. Localhost first, then over Tailscale.

**The reassuring part:** the apply path already exists on `main`. `PackageOpPlayback.applyOps
branchId commitHash ops` folds ops into projections, and writes are already **idempotent** —
`INSERT OR IGNORE INTO branch_ops (id, …)` keys on the content-hash id, so receiving an op twice
is a no-op. This PR mostly **exposes that over HTTP**; it doesn't reinvent apply.

**Goal.** A peer can `GET` ops since a cursor and `POST` ops; the receiver applies them via the
existing playback path. A remote op and a local op are the same thing — no separate import path.

**Prereqs.** ops⊥projections (effort 3 — `core.db` is what syncs) and Tailscale transport
(effort 5 — addressing + the `Tailscale-User-Login` header). Conflict-dispatch (4) handles
`SyncDivergence`, but the floor can ship before rich resolution (last-writer / surface-as-data).
**Identity (8) is *not* a prereq:** the first proof attributes ops by the **raw
`Tailscale-User-Login` string** (the login *is* the author id — no mapping table), which is why
sync (7) can precede identity (8). Effort 8 then replaces the raw login with a proper
login→account binding + structured `Intent`.

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
| `LibDB/` (new `Sync.fs` or builtins) | Two thin builtins: **`opsSince : branchId -> cursor -> Ply<List<SerializedOp>>`** (a `SELECT … WHERE seq > cursor ORDER BY seq` over `package_ops`/`branch_ops`) and **`applyRemoteOps : List<SerializedOp> -> Ply<ApplyResult>`** = `INSERT OR IGNORE` + the existing `PackageOpPlayback.applyOps`. Apply already exists; this wraps it. |
| `LibDB/` schema | Add a monotonic **`seq`** column (autoincrement per store) for the `?since=` cursor; `created_at` alone isn't a total order. Per-source seq; cross-source ties break by `(timestamp, author_id)` (sync.md). |
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

## SQL/schema

`package_ops`/`branch_ops` already exist and are canonical. Add: a `seq` column (cursor), and a
small **`sync_cursors`** table (per remote: `folded_through_seq`) so a poll resumes where it
left off. No projection tables touched — they refold from the applied ops.

## Test plan

| Step | Test | Done-signal |
|---|---|---|
| idempotent apply | `.fs`: `applyRemoteOps [op; op]` (same op twice) | second is a no-op (`INSERT OR IGNORE`); state identical |
| two-instance round-trip | `.fs` integration: author on A, `opsSince` from A, `applyRemoteOps` on B | B resolves the same name→hash as A |
| cursor advances | `.fs`: POST 3 ops, assert `sync_cursors.folded_through_seq += 3` | cursor correct, no re-fold of old |
| auth maps | `.dark`/`.fs`: a request with `Tailscale-User-Login: x` lands ops authored by x's account | authorship correct |
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
- **Seq monotonicity across sources.** Each store's `seq` is local; a global order needs the
  `(timestamp, author_id)` tiebreak. Fine for a star (everyone orders against the hub).
- **Concurrent write during snapshot.** `GET /sync/snapshot` must be a consistent read (a
  transaction / point-in-time), or a bootstrapping peer sees a torn state.
- **Conflict volume tailnet-wide.** N authors → more `SyncDivergence`; the floor surfaces them
  (never blocks the POST) and defers rich resolution to the conflict-dispatch policy.

## Above / below

- **Below:** ops⊥projections (`core.db`), Tailscale transport, conflict-dispatch (for
  `SyncDivergence`).
- **Above expects:** autosync (effort 9) wraps `dark sync` in a poll loop; identity (8) supplies
  the login→account mapping; print-md-as-an-App (10) is the thing whose edits ride this wire.
