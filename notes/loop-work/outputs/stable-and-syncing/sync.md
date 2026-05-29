# Sync and sharing

This is the durable home for the sync wire protocol and the sharing
story. The notion of "stable" as it relates to PDD iterative development
lives in the PDD algorithm doc, not here.

The one-line frame: **the protocol carries only ops and commits.**
Receivers regenerate every projection locally from those ops. Nothing
derived travels the wire.

## Definitions

### Sharing

**Sharing** is the act of an op authored on instance X becoming
observable on instance Y. The mechanism is idempotent op replication:
the wire carries the op plus its attribution, and the receiver applies
it through the same op-playback path it would use for a locally authored
op. There is no separate "import" path — a remote op and a local op are
the same kind of thing.

Sharing has three modes worth distinguishing:

- **Self-share** — same human, different machines (desktop + laptop).
  Bidirectional, single identity, full trust.
- **Pair-share** — multiple humans collaborating on a namespace they
  both own (or where one delegates to the other). Identity is per-op;
  trust is scoped to the namespace.
- **Public-share** — pushing to a public namespace via
  `matter.darklang.com`. Authorship is explicit; trust is set per
  namespace owner.

All three ride the same wire protocol. What differs is the
authentication and the capability surface granted to the remote peer.

### Namespace ownership

The canonical sharing-vs-collaboration distinction:

> if the edits are to our own namespace, ops are applied directly and
> visible to everyone with access. if the edits are to another
> namespace, we automatically create an approval request.

So **self-share and pair-share behave like a direct apply.** Public-share
(and any cross-namespace edit) routes through the approval flow, which is
itself just more ops on the wire.

Namespaces are owner-scoped: a location is owner + modules + name (e.g.
`User.Stachu.Foo.bar`). When an op targets a namespace whose owner is not
the author, the op becomes an approval request rather than a direct
apply. The owner sees the request arrive over the sync stream, chooses to
accept / reject / comment, and the resolution is itself ops on the wire.

### Approval flow as ops

Approvals are not a subsystem — they are two first-class, content-
addressed, syncable op variants:

- `CreateApprovalRequest(authorId, targetLocation, op)`
- `ApprovalDecided(requestId, decision: Approved | Rejected | ChangesRequested, comment)`

Same replication machinery as any other op. No separate approval system.

## Wire protocol

### Stance: lean on Tailscale, don't build a network stack

Let Tailscale handle peer addressing, identity, TLS, and auth. The wire
protocol then reduces to an HTTP-over-Tailscale exchange of ops.

The substrate does not *require* Tailscale. Over the open internet the
same HTTP endpoints work with ordinary TLS plus an auth header. Tailscale
is the convenient default; the protocol design is network-agnostic.

### Default model: client/server

The default deployment is plain client/server, where the server is an
always-on desktop on the Tailscale network. Peers configure their
remotes through config in the CLI-adjacent `.darklang` directory — not
through environment variables.

### Endpoints

```
GET  /sync/snapshot
  → returns the canonical snapshot (current branch state)
  Used for: bootstrap
  Auth: Tailscale-User-Login header OR Bearer token

GET  /sync/snapshot/hash
  → returns the SHA256 of the current snapshot
  Used for: install-time + upgrade-time cache validation

GET  /sync/events?since=<seq>&branch=<id>
  → returns ops with sequence > since for the given branch
  Auth: same

POST /sync/events
  Body: the ops the client is pushing
  Response: { accepted, assigned_sequences, conflicts }
  Auth: same
  Note: never blocks on conflicts — accepts the ops and returns any
        conflicts for the client to surface separately.

GET  /sync/branches
  → list branches the peer can access (filtered by identity)

GET  /sync/whoami
  → echo which Tailscale-User-Login the server saw and which Darklang
    account it maps to; used for sanity checks

WS   /sync/live
  → optional WebSocket for low-latency push of new ops as they apply;
    falls back to polling /sync/events on an interval
```

About six endpoints. Most are simple queries against the op tables.

### Authentication

Two modes, both resolving to an `account_id` that goes into op
authorship. The server does not care which mode the client used.

- **Tailnet mode** (default between trusted peers): Tailscale terminates
  TLS and injects `Tailscale-User-Login` / `Tailscale-User-Name` headers.
  The server maps the login to an account, with a one-time onboarding
  step that binds the tailnet login to a Darklang account.
- **Token mode** (for non-Tailscale peers or the public web): a per-
  account, scope-limited bearer token in `Authorization: Bearer <token>`.
  Same account lookup, different identity source.

### Sync semantics

- **Append-only and idempotent.** Receiving the same op twice is a no-op;
  the content-hash op ID plus an applied flag handle de-dupe.
- **Per-source ordering.** Each source carries a monotonic sequence
  number. Cross-source ordering breaks ties by `(timestamp, author_id)`.
- **Never blocks on conflicts.** Ops apply in the order received; any
  disagreements are surfaced through the conflict-resolution dispatch
  (see `conflicts.md`).
- **Carries no derived data — only ops and commits.** Receivers
  regenerate all projections locally. This is the whole point: the wire
  is a transport for intent, not for state.

### Replication topology

```
            matter.darklang.com   (always-on; just another peer)
                    ↑↓
   ┌────────────────┼────────────────┐
   │                │                │
stachu/desktop  stachu/laptop   feriel/laptop
   └─── optional direct p2p over Tailscale ───┘
```

Default: every peer syncs to and from the always-on server. Optionally,
peers sync directly to each other over Tailscale (e.g. Stachu's desktop
pushes straight to his laptop). The protocol is symmetric —
`matter.darklang.com` is just another peer, distinguished only by the
convention of being always-on.

## Interactions with the rest of the system

### Event bus

Sync is just another producer on the event bus (see `event-bus.md`).
Arriving ops flow onto the bus exactly as locally authored ops do.

### Conflicts

Arriving ops that disagree with local state generate conflicts, handled
by the conflict dispatch (see `conflicts.md`). `POST /sync/events` never
blocks: it applies what it can and returns the conflicts for the client
to dispatch locally, after sync, on whatever per-session policy applies.

### Capabilities

A remote peer can invoke only the capabilities the local user granted it.
Per-peer grants live alongside per-session grants: a trusted peer (e.g.
my own laptop) may receive all of my caps; an untrusted peer gets the
minimum (read-only).

## Open decisions

- **Default sync target.** Auto-sync to the server on install, or opt-in?
  Leaning local-first: the user adds remotes explicitly, mirroring git's
  `remote add`.
- **Sync granularity.** Per-branch with namespace as a filter on top is
  the likely shape.
- **Public exposure.** Initial deployments are tailnet-only; opening to
  non-tailnet members (via Tailscale funnel or a real public ingress,
  with rate-limiting and abuse defense) is a deliberate later step.
- **Conflict dispatch timing.** When does the receiver dispatch arriving
  conflicts — immediately, or batched and surfaced on the next CLI
  interaction? A per-session policy.
- **WIP across instances.** Does WIP on one machine sync to another?
  Leaning hybrid: WIP stays local by default, opt-in to share it.

## Persistence: ops in SQLite, no sidecars

The wire carries only ops and commits — and locally those ops and commits should
have exactly **one** durable home: the SQLite DB. Today PDD scatters state across
`rundir/pdd-cache/*.jsonl`, `promoted.jsonl`, `promoted_hashes.jsonl`, and
friends. Those JSONL sidecars are a second source of truth living next to the op
tables, and they have to be killed.

The ops-vs-projections lens makes the rule sharp: the **op stream is the only
durable thing**, and it lives in SQLite. Everything currently in a sidecar is
either an op (so it belongs in the op tables) or a projection (so it should be
*regenerated*, never persisted as a separate file). A JSONL cache is the worst of
both — durable enough to drift, derived enough to be wrong.

Concretely:

- **Everything in SQLite.** Traces, WIP, promoted bodies, the materialization
  cache — all move into the DB. Either raw tables or, more likely, a `UserDB`-like
  persistence primitive we build out: a typed, content-addressable store usable
  from Dark code, so PDD persistence is not bespoke F# but an ordinary Dark
  consumer of the same store.
- **One source of truth.** With ops in SQLite, the snapshot/bootstrap path and
  the wire protocol both read from one place. No file has to be reconciled against
  the tables.
- **Content-addressable store.** This is the same content-addressed-store framing
  that lets the wire carry only ops and commits: if bodies and traces are
  addressed by hash in the DB, replication is idempotent and projections rebuild
  locally for free. The sidecars are exactly the non-content-addressed state that
  blocks that, which is why they go.

So the storage stance and the wire stance are the same stance seen at two scales:
on disk, ops in SQLite with no sidecars; on the wire, ops and commits with no
derived data.
