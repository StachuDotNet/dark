# Sync

The sync wire protocol. The one-line frame: **the protocol carries only ops and
commits.** Receivers regenerate every projection locally from those ops; nothing
derived travels the wire.

## Sharing, kept simple

**Sharing** is an op authored on instance X becoming observable on instance Y, by
idempotent op replication: the wire carries the op plus its attribution, and the
receiver applies it through the same op-playback path it uses for a locally authored
op. A remote op and a local op are the same kind of thing — there is no separate
"import" path.

**For now we simply trust the other parties on the Tailscale network.** No sharing
"modes", no namespace-ownership rules, no approval flow. If a peer is on the tailnet
and we sync with it, its ops apply. (Cross-org trust, approvals, and public exposure
are a deliberate later concern — punted to a later bucket.)

## Wire protocol

### Stance: lean on Tailscale, don't build a network stack

Let Tailscale handle peer addressing, identity, TLS, and auth. The wire protocol then
reduces to an HTTP-over-Tailscale exchange of ops. (The substrate is not *bound* to
Tailscale — over the open internet the same endpoints work with ordinary TLS — but the
tailnet is the only target we design for now.)

### Default model: client/server

Plain client/server, where the server is an always-on desktop on the Tailscale
network. Peers configure their remotes through config in the CLI-adjacent `.darklang`
directory — not environment variables.

### Endpoints

```
GET  /sync/snapshot           → the canonical snapshot (current branch state); for bootstrap
GET  /sync/snapshot/hash      → SHA256 of the snapshot; for install/upgrade cache validation
GET  /sync/events?since=<seq>&branch=<id>
                              → ops with sequence > since for the branch
POST /sync/events             → push ops; body = ops; resp = { accepted, assigned_sequences, conflicts }
                                never blocks on conflicts — accepts the ops, returns conflicts to surface
GET  /sync/branches           → branches the peer can access
GET  /sync/whoami             → which Tailscale-User-Login the server saw + its Dark account; a sanity check
WS   /sync/live               → optional low-latency push of new ops; falls back to polling /sync/events
```

About six endpoints, mostly simple queries against the op tables. **Auth on every one
is the `Tailscale-User-Login` header** that Tailscale injects after terminating TLS;
the server maps that login to an `account_id` (a one-time onboarding binds a tailnet
login to a Dark account) and that account_id goes into op authorship. No bearer
tokens, no second identity source — the tailnet *is* the trust boundary for now.

### Sync semantics

- **Append-only and idempotent.** Receiving the same op twice is a no-op (content-hash
  op ID + an applied flag de-dupe).
- **Per-source ordering.** Each source carries a monotonic sequence number;
  cross-source ties break by `(timestamp, author_id)`.
- **Never blocks on conflicts.** Ops apply in order received; disagreements surface
  through the conflict dispatch (see [conflicts.md](conflicts-and-resolutions.md)).
- **Carries no derived data — only ops and commits.** Receivers regenerate all
  projections locally. The wire transports intent, not state.

### Replication topology

A small star: every peer syncs to and from the always-on server (Stachu's desktop, or
a hosted `matter.darklang.com` later). **Direct peer-to-peer sync is punted to
the later bucket** — the star is enough for "my machines + a couple of coworkers."

## Ops vs. projections — the separation that makes concurrent sync work

This was previously open; settling it: **ops are stored separately from the
projections built from them.** The op stream (and commits — see below) is the one
synced, durable thing; each projection is a local, regenerable read of it.

Why it matters for sync specifically:

- **Performance.** A projection (the name→hash table, package-item bodies, a file
  view) is rebuilt by folding ops; keeping it separate lets it be cached, dropped, and
  rebuilt without touching the canonical stream.
- **Concurrent AI sessions.** Multiple agents/branches can run at once because **each
  projection ignores the ops irrelevant to its branch/session** — it folds only what it
  scopes to. The shared op stream is the only contention point, and it's append-only.

So a projection declares what it folds and its scope (branch / session); arriving sync
ops simply append to the stream, and the relevant projections pick them up. The DB
shape this implies — a core ops/sync DB plus per-branch/per-session/per-app projection
stores — is worked in [distributed-event-sourcing.md](../pre-s-and-s/distributed-event-sourcing.md).

## Persistence: ops in SQLite, no sidecars

Locally, ops and commits should have exactly **one** durable home: the SQLite DB.
**Commits and branches are themselves managed by ops** (a different, "above" layer of
op kinds than package edits, but ops all the same) — so there is no separate
commit/branch store to reconcile against the op tables.

The ops-vs-projections lens makes the rule sharp: the **op stream is the only durable
thing**, and it lives in SQLite. Anything else is either an op (belongs in the op
tables) or a projection (regenerated, never persisted as a separate file). Avoid JSONL
sidecars — they are the worst of both, durable enough to drift and derived enough to be
wrong. (The PDD spike scattered state across `*.jsonl` caches; that pattern must not
return when the real thing is built.)

## Interactions

- **Event bus** — sync is just another producer; arriving ops flow onto the bus exactly
  as locally authored ops do (see [event-bus.md](../pre-s-and-s/event-bus.md)).
- **Conflicts** — arriving ops that disagree generate conflicts handled by the dispatch
  (see [conflicts.md](conflicts-and-resolutions.md)); `POST /sync/events`
  applies what it can and returns the rest.
- **Capabilities** — what a synced/remote action may *do* is gated by capabilities,
  which are per-instance settings, not ops (see [capabilities.md](../pre-s-and-s/capabilities.md)).

## Open decisions

- **Default sync target.** Explicit, opt-in autosync — the user adds remotes
  deliberately (mirroring git's `remote add`), and autosync, once on, is likely managed
  by a **sync daemon** (see [cli-daemon.md](../pre-s-and-s/cli-daemon.md)). Not auto-on at install.
- **Sync granularity.** Per-branch is the likely unit.
- **WIP across instances.** Ideally WIP syncs too, but we don't yet know how to do it
  safely — **punted**. WIP stays local by default for now.
