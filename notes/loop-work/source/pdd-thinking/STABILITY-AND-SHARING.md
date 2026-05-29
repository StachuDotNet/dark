# Stability & Sharing

The user's second explicit itch. Produced by loop TODOs T7-T10.

## Headline finding

The wire protocol's *target shape* is already specified in the
LibMatter sync.md + sync-model.md vault notes. The protocol's
*implementation* is unbuilt on main — no sync server, no wire
exchange code, no `matter.darklang.com` host. But: the
underlying op model (`package_ops`, `branch_ops`, idempotent
playback) is mature, and **Tailscale handles the entire
networking layer for free** (per `Networking and Internet/
Tailscale.md`). The remaining work is mostly Dark-side wiring
plus a small F# HTTP-server endpoint.

---

## Definitions (T7)

### Stable

A thing is **stable** when all of the following hold:

- **Named** — has a `location` (owner + modules + name) and/or a
  content-hash.
- **Hashed** — content-addressable; the bytes hash to the same
  thing on any instance.
- **Validated** — passed conflict + type checks at op-apply time
  (via `PackageOpPlayback`).
- **Durable** — persisted via committed `package_ops` with
  `commit_hash IS NOT NULL`.
- **Attributable** — the commit that introduced it has an
  `account_id`.

WIP ops (commit_hash NULL) are not stable by this definition —
they're working state.

### Sharing

**Sharing** is the act of an op authored by agent A on instance X
becoming observable on instance Y. Mechanism: idempotent op
replication. The wire carries the op + attribution; the receiver
applies via the same `PackageOpPlayback` it'd use locally.

Sharing has three modes worth distinguishing:

- **Self-share** — same human, different machines (e.g., desktop
  + laptop). Bidirectional. Same identity. Trust: full.
- **Pair-share** — multiple humans collaborating on a namespace
  they both own (or where one delegates to the other). Identity
  per-op; trust scoped to the namespace.
- **Public-share** — pushing to a public namespace via
  matter.darklang.com. Authorship clear; trust per-namespace-
  owner.

All three use the same wire protocol; what differs is the
authentication + the cap surface granted to the remote peer.

### Namespace ownership

From the 2025-11-12 ux-thinking note:

> "if the edits are to our own namespace, ops are applied directly,
> and visible to everyone that has access. if the edits are to
> another namespace, we automatically create an approval request."

This is the canonical sharing-vs-collaboration distinction.
**Self-share + pair-share = same as direct apply**. Public-share
(or any cross-namespace edit) routes through the approval flow,
which is itself just another op-on-the-wire (an approval request
op).

The accounts_v0 table already seeds Darklang/Stachu/Paul/Feriel;
namespaces are owner-scoped (`MyApp.Auth.validate` → owner +
modules + name). Combining: when an op targets
`User.Stachu.Foo.bar` and the author isn't Stachu, the op becomes
an approval request rather than a direct apply. The receiver
("Stachu" in this case) sees the request via the sync stream,
chooses to accept/reject/comment, and the resolution is itself
ops on the wire.

### Approval flow as ops

Following the 2025-11-12 model:

- `CreateApprovalRequest(authorId, targetLocation, op)`
- `ApprovalDecided(requestId, decided: Approved | Rejected | ChangesRequested, comment)`

These are first-class ops, content-addressed, syncable. Same
machinery. No separate "approval system" subsystem — it's just
ops with semantics.

---

## Wire protocol (T8)

### Stance: lean on Tailscale, don't build a network stack

Per `~/vaults/Darklang Dev/05.Implementation/Networking and Internet/
Tailscale.md`, the recommendation is **let Tailscale handle peer
addressing + identity + TLS + auth**. Then the wire protocol is
just an HTTP-over-Tailscale exchange of ops.

The substrate doesn't *require* Tailscale — over open internet,
the same HTTP endpoints work with normal TLS + an auth header.
Tailscale is the convenient default. **The protocol design is
network-agnostic.**

### Endpoints

```
GET  /sync/snapshot
  → returns the canonical seed.db (or current branch state)
  Used for: bootstrap (BOOTSTRAP.md bootstrap-8)
  Auth: Tailscale-User-Login header OR Bearer token

GET  /sync/snapshot/hash
  → returns the SHA256 of the current snapshot
  Used for: install-time + upgrade-time cache validation

GET  /sync/events?since=<seq>&branch=<id>
  → returns events with sequence > since for the given branch
  Body: JSON-encoded SyncEvent[]
  Auth: same

POST /sync/events
  Body: SyncEvent[] (the events the client is pushing)
  Response: { accepted: bool; assigned_sequences: int[]; conflicts: Conflict[] }
  Auth: same
  Note: never blocks on conflicts (per 2025-11-12) — accepts the ops,
        returns any conflicts for client to surface separately.

GET  /sync/branches
  → list branches the peer has access to (filtered by identity)

GET  /sync/whoami
  → echo back which Tailscale-User-Login the server saw + which
    Darklang account that maps to. Used for sanity-checks.

WS   /sync/live
  → optional: WebSocket for low-latency push of new events as
    they're applied. Falls back to polling /sync/events on
    interval.
```

This is ~6 endpoints. Small. Most are simple SQL queries against
the existing tables.

### SyncEvent schema

```fsharp
type SyncEvent =
  | PackageOpEvent of
      patchId: Uuid
      * partId: Uuid
      * op: PackageOp           // existing PT.PackageOp shape
      * author: AccountId
      * timestamp: DateTime
      * sequence: Int64         // per-source-instance monotonic

  | BranchOpEvent of
      op: BranchOp
      * author: AccountId
      * timestamp: DateTime
      * sequence: Int64

  | CommitEvent of
      commit: Commit            // hash + message + branchId + accountId
      * sequence: Int64

  | ApprovalRequestEvent of
      requestId: Uuid
      * authorId: AccountId
      * targetLocation: Location
      * proposedOp: PackageOp
      * sequence: Int64

  | ApprovalDecidedEvent of
      requestId: Uuid
      * decided: ApprovalDecision  // Approved | Rejected | ChangesRequested + comment
      * deciderId: AccountId
      * sequence: Int64
```

Note: the existing `package_ops` table has `propagation_id` for
PropagateUpdate ops — sync inherits that machinery.

### Authentication

Two modes:

- **Tailnet mode** (default for sync between trusted peers):
  Tailscale terminates TLS at `tailscale serve --https=443`.
  The proxied request has `Tailscale-User-Login` and
  `Tailscale-User-Name` headers injected. The server maps the
  login to an `account_id` (with a one-time onboarding step that
  binds tailnet login → Darklang account).
- **Token mode** (for non-Tailscale peers or public web):
  Bearer-token in `Authorization: Bearer <token>`. Tokens are
  per-account, scope-limited. Same account lookup, different
  identity source.

The server doesn't care which mode the client used; both produce
an `account_id` that goes into op authorship.

### Sync semantics

- Events are append-only and idempotent. Receiving the same event
  twice is a no-op (the existing `package_ops.applied` flag +
  the content-hash op-ID handle de-dupe).
- Ordering is per-source: each source has a monotonic sequence
  number. Cross-source ordering is by `(timestamp, author_id)`
  tie-break.
- Sync **never blocks on conflicts** (per 2025-11-12). Ops apply
  in the order received; any conflicts are surfaced via the
  conflict-resolution dispatch (`CONFLICTS-AND-RESOLUTIONS.md`).
- The protocol carries no derived data — only ops + commits.
  Receivers regenerate projections via the existing
  `growIfNeeded` flow.

### Replication topology

```
matter.darklang.com         (canonical host; long-running)
        ↑↓
   ┌────┴────┬───────┬───────┐
   │         │       │       │
stachu/desktop  stachu/laptop  ocean/desktop  feriel/laptop
   │         │       │       │
   └─peer──┬─┘       │       │
          ?─────────?──peer──?
          (optional p2p sync via Tailscale)
```

Default: every peer syncs to/from `matter.darklang.com`. Optional:
peers can additionally sync directly to each other via Tailscale
(e.g., stachu's desktop pushes to laptop without going through the
central server). The protocol is symmetric — `matter.darklang.com`
is "just another peer" with the convention of being always-on.

---

## Sequencing (T9)

Ordered named work-units. Each is a shippable PR.

### Pre-work / readiness

- **share-1: identity binding.** A Darklang account_id is bound
  to a Tailscale login (or a token). Add a `linked_identities`
  table (or column on accounts_v0) + a one-shot `dark link
  --tailscale` CLI command. Depends on T11-T12 (identity).

### Local read endpoints

- **share-2: GET /sync/snapshot + GET /sync/snapshot/hash.**
  Smallest possible step: a Dark HTTP handler (built on the
  existing `Builtins.Http.Server`) that returns the current
  seed.db file + its hash. No auth yet. Localhost only.

- **share-3: GET /sync/events.** Add the events endpoint
  returning `package_ops` + `branch_ops` filtered by sequence +
  branch. Also localhost only.

- **share-4: GET /sync/whoami + Tailscale identity binding.**
  Read `Tailscale-User-Login` from request headers; look up the
  account; return JSON. This is the moment Tailscale enters
  the picture.

### Write endpoint + apply path

- **share-5: POST /sync/events.** Accept inbound events, run them
  through `PackageOpPlayback`. Return any conflicts. Idempotent.

- **share-6: peer-discovery + auto-sync cron.** A background
  loop in the CLI app (or as a Dark cron) that periodically pulls
  events from configured peers (one of which is
  matter.darklang.com). Per 2025-11-12: "we allow the user to
  have full control over when syncing happens (autosync on/off)."

### Approval flow

- **share-7: ApprovalRequest + ApprovalDecided ops.** New
  PackageOp variants. When op targets a namespace not owned by
  author, automatically produce an ApprovalRequest op instead
  of direct-apply. The owner sees the request in their feed and
  decides via the SCM CLI.

- **share-8: cli/scm/approve.dark.** Dark-side CLI command for
  listing pending approval requests + accept/reject/comment.

### Hosted central instance

- **share-9: deploy matter.darklang.com.** Bring up a GCP /
  Tailscale-served Dark instance with the sync endpoints
  exposed. Initial seed: the canonical Stdlib snapshot from
  Phase 1 (Bootstrap). Auth: tailnet members only initially;
  public funnel later.

- **share-10: distribute first onboarding.** Get a second user
  (Ocean or Feriel) onboarded: they install Dark with the
  bundled seed → run `dark link --tailscale` → see Stachu's
  commits flow in via auto-sync. **MVP-cohabitation is roughly
  this** (see T23).

### Optional / later

- **share-11: WebSocket live-push channel.** Latency win for
  real-time collab. Polling works for most cases.
- **share-12: public funnel.** Open matter.darklang.com to
  non-tailnet members via `tailscale funnel` or a real public
  ingress. Needs rate-limiting + abuse defense.

### Dependencies

```
share-1 (identity binding) — needs T11-T12 identity work
   ↓
share-2 (snapshot read)
share-3 (events read)              — parallel; both localhost
   ↓
share-4 (whoami + tailnet identity) — adds Tailscale
   ↓
share-5 (POST /events)              — full read+write loop
   ↓
share-6 (autosync) ←─ done locally     ┐
                                       │
share-7 (approval ops)                 ├── ApprovalRequest can ship
   ↓                                   │   with or without share-9
share-8 (approve CLI)                  │
                                       │
share-9 (matter.darklang.com deploy) ←─┘  needs the protocol above

share-10 (first 2nd-user onboarding)  ←  MVP-cohabitation target
```

share-1 through share-6 is **mostly Dark-side code** on top of
existing HTTP builtins — ~1-2 weeks. share-7 through share-9 adds
weeks of refinement. share-10 is the goal-line.

---

## Phase decision (T10)

**Stability+sharing ships in Phase 3.**

- **Phase 3 — "Two machines, one substrate."** Lands share-1
  through share-10. Depends on:
  - Identity from Phase 2 (T11-T12)
  - Conflict-resolution dispatch from Phase 2 (T14)
  - Bootstrap Phase 1 (so matter.darklang.com can host a real
    seed.db)
- **Phase 4 (later)** — share-11 (WebSocket) + share-12 (public
  funnel) + replication topology refinements (multi-peer p2p, sync
  cycles, conflict-detection at scale).

Phase 3 is the **user's primary milestone** because it's when
sharing actually works between two real machines. The MVP-
cohabitation demo (T23) is essentially "Phase 3 working
end-to-end with 2 users."

### Why this ordering

- share-1 (identity binding) depends on T11-T12 → goes in
  Phase 2 alongside identity.
- share-2 through share-6 can land in Phase 3 once identity is
  done. None of them need conflict-resolution machinery to be
  fully designed — they just need *some* policy for what to do
  with conflicting ops (default: surface, don't block).
- share-7 (approval ops) needs the conflict-resolution dispatch
  story to be at least sketched; can stay in Phase 3.
- share-9 (deploy) is when stability+sharing becomes real for the
  user.

Cross-link: see ROADMAP §"Phase plan" once T24 fills it.

---

## Cross-cutting interactions

### With bootstrap (BOOTSTRAP.md)

- bootstrap-8 (matter.darklang.com hosts the seed) **uses**
  share-2's GET /sync/snapshot endpoint.
- After bootstrap, the user's instance subscribes to /sync/events
  (share-3) to keep current. **Bootstrap and sync are
  complementary**: bootstrap is the cold start; sync is the
  warm path.

### With conflicts (CONFLICTS-AND-RESOLUTIONS.md)

- POST /sync/events (share-5) never *blocks* on conflicts (per
  2025-11-12). It applies what it can, returns conflicts for the
  client to surface.
- The conflict-dispatch is invoked client-side after sync:
  arriving ops that disagree with local state generate
  Conflict.OpVsOp; dispatch decides.

### With capabilities (CAPABILITIES.md)

- A remote peer can only invoke caps the local user granted that
  peer. Per-peer cap grants live alongside per-session grants.
  A "trusted peer" (e.g., my laptop) gets all my caps; an
  "untrusted peer" gets minimum (read-only).

### With identity (IDENTITY.md — not yet written)

- The account_id ↔ Tailscale-login binding is identity work.
  T11-T12 should design the binding shape.

### With remote access (REMOTE-ACCESS.md — not yet written)

- Sharing is one mode of remote interaction. Remote-control
  (running a command on another peer) is a separate mode that
  uses the same Tailscale + identity stack.

---

## Open decisions

- **(Q-ss-1) Default sync target.** Every install auto-syncs to
  matter.darklang.com? Or opt-in? Probably opt-in — local-first
  default; user adds remotes explicitly. (Mirrors git's
  `remote add`.)
- **(Q-ss-2) Approval-required default.** Cross-namespace ops
  auto-create approval requests, or fail loudly? Per 2025-11-12:
  approval request. Confirm.
- **(Q-ss-3) Sync granularity.** Per-branch? Per-namespace?
  Always-all? Probably per-branch with namespace as a filter
  on top.
- **(Q-ss-4) Public funnel vs. tailnet-only.** Phase 4 decision.
  Initial deployment: tailnet-only. Public exposure: deliberate
  later step.
- **(Q-ss-5) Conflict resolution latency.** When does the
  receiver dispatch arriving conflicts? Immediately?
  Batched-and-surfaced-on-next-CLI-interaction? Per-session
  policy.
- **(Q-ss-6) WIP across instances.** Does Stachu's WIP on the
  desktop sync to the laptop? Per the COHABITATION wip-sync
  tension: hybrid — WIP stays local by default, `share-wip`
  opt-in. Confirm.
