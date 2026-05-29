# Iter 03 — per-stream ACL, grants, and the hard revocation question

The networking doc has a `share_grants` table at the hub with
`scope ∈ {'branch:<id>', 'namespace:<owner.module>', 'app:<id>'}` and
`permission ∈ {'read', 'write', 'admin'}`. That's the right surface
on day one but it leaves the *semantic* questions unanswered. In
particular:

- **Who's allowed to append a given op?** ("origin instance had write
  permission for this (stream, key)")
- **What's the apply-time check at a peer?** (signature valid + origin
  was authorized at op time + isn't retroactively revoked now)
- **What happens to existing ops when a grant is revoked?** (delete? keep?
  ignore in projection?)
- **What's the default `since` for a freshly-granted read?** (history vs
  forward-only, per stream)

This iteration tries to draw a line through all four.

## Ownership, per stream

Streams don't share an ownership model. Putting them all in one
`share_grants` table is fine; pretending they have one ownership
semantics is wrong. Per-stream:

| Stream | Owner | Default visibility | Sharing primitive |
|---|---|---|---|
| `packages` | the branch creator (rooted in the `CreateBranch` op's `origin_instance.user_id`) | private to user; default-shared with all the user's instances | `share branch <id> with <user>` |
| `branches` | same — branches are owned by their creator | same | grant follows the branch |
| `traces` | the originating instance's user; key = trace_id | private | `share trace <id> with <user>` (rare) or `share session <id>` to share its attached traces |
| `sessions` | session creator | private | `share session <id> with <user>` |
| `app:<id>` | app creator | private; default-shared with creator's instances | `share app <id> with <user>` |
| `account` | the user themselves | only their own instances | never granted out (different mechanism: account = identity) |
| `presence` (hub-only) | hub | hub-mediated | n/a |
| `instances` (per-user) | user | only user's instances | never granted out |

The pattern: most streams have a per-key owner derived from the first
op on that key. The hub stores the grant, but the *semantics* of
"who owns key X in stream S" is computed by walking the ops, not
configured separately. Grants are layered on top.

## Grant data model (refined)

Building on the networking doc's `share_grants`, with the additions
the unified model implies:

```sql
-- Stored at the hub; replicated to instances via the share_ops stream.
CREATE TABLE share_grants (
  id              UUID PRIMARY KEY,
  granter_user_id UUID NOT NULL,
  grantee_user_id UUID NOT NULL,
  stream          TEXT NOT NULL,         -- 'packages' | 'traces' | …
  key_glob        TEXT NOT NULL,         -- '*' = whole stream, 'feat-*' = prefix, exact = single key
  permission      TEXT NOT NULL,         -- 'read' | 'write' | 'admin'
  default_since   TEXT NOT NULL DEFAULT 'forward',  -- 'genesis' | 'forward'
  granted_at      INTEGER NOT NULL,
  expires_at      INTEGER,
  revoked_at      INTEGER,
  revoke_mode     TEXT                    -- NULL until revoked. 'soft' | 'recompute' | 'fork'
);
CREATE INDEX idx_grants_grantee ON share_grants(grantee_user_id, stream);
CREATE INDEX idx_grants_granter ON share_grants(granter_user_id, stream);
```

Three new fields beyond the networking doc's draft:

- **`key_glob`** — `*` for the whole stream, prefix-glob for namespaces
  (e.g., `Darklang.Stdlib.*`), exact key for "share this trace." Keeps
  the hub's filter cheap.

- **`default_since`** — `'genesis'` for streams where the projection
  doesn't make sense without history (packages, branches);
  `'forward'` for streams where you only want new (traces, sessions,
  app data unless the app declares otherwise).

- **`revoke_mode`** — see below.

Grants are themselves ops. The `share_ops` stream lives at the hub
and replicates to instances; each grant create / modify / revoke is
an op signed by the granter. This means: grants sync, audit trail
is automatic, instances can compute "what's currently granted" by
folding the share_ops stream against current time.

## Append authority — what the writer checks

Before any instance writes an op to its local `ops.db`, the daemon
checks:

```fsharp
let canAppend (stream, key, originInstance) : Task<bool> = task {
  let userId = originInstance |> instanceOwner
  // Default: a user can always write to streams they own.
  if isOwnedKey (stream, key) userId then return true
  // Cross-user write requires an active write-or-admin grant.
  let! grants = grantsFor stream key userId
  return grants
    |> Seq.exists (fun g ->
        (g.permission = "write" || g.permission = "admin")
        && g.revokedAt = None
        && g.expiresAt |> Option.forall (fun e -> e > now()))
}
```

If `canAppend` returns false, the daemon rejects the op locally —
the op never lands in this instance's `ops.db`. There is no "queue
and retry"; the user's UI surfaces "you can't write to this branch."

## Apply-time check at a peer

When instance B receives an op signed by instance A (via sync), B
checks four things before applying:

1. **Hash matches envelope bytes.** Standard integrity check.
2. **Signature valid against A's public key, at the time of A's
   signing window.**
3. **A was authorized to write to (stream, key) at op.created_at.**
   Look up grants in B's local replica of `share_ops`.
4. **The grant hasn't been retroactively revoked AND `revoke_mode`
   means we should drop this op.**

(1) and (2) are signature stuff — the existing crypto plumbing.
(3) and (4) are the interesting ones.

Note: "at op.created_at" — this is why grants are time-windowed.
If A had the grant from t1 to t2 and op was signed at t2-ε, that's
valid even if the grant was revoked at t2.

## The revocation question

Concrete scenario: Stachu shares `feat-bug` branch with Feriel.
Feriel makes 50 ops over a week. Then Stachu revokes the grant.
What happens?

Three modes (`revoke_mode` column above), user picks at revoke time:

### `'soft'` (default, no surprise)

Feriel's 50 ops stay in everyone's `ops.db`. Stachu's `feat-bug`
projection includes them (they're integrated). Going forward,
Feriel can't push more.

This is what most people mean by "revoke." It's polite — past
contributions stay; future is closed. Same as removing a
collaborator from a GitHub repo: their old PRs and commits don't
disappear.

### `'recompute'` (surgical, opt-in)

Stachu wants Feriel's 50 ops out of his projection but doesn't
want to re-do all his own work that built on top of them. Trigger:
the daemon walks the projection, drops every op whose origin is
Feriel, and re-applies what's left. For ops Feriel made that
Stachu hasn't extended, this just removes them. For ops Stachu
extended (e.g., he renamed a thing Feriel introduced), the
projection surfaces a conflict: "this name was set by Feriel and
removed; your subsequent op references a hash she introduced."
Stachu sees the conflicts list and resolves each.

This is heavier — basically a partial branch fork — but precise.
Cost is proportional to # of Feriel ops.

### `'fork'` (nuclear, opt-in)

The `feat-bug` branch as it exists is preserved as
`feat-bug-pre-revoke-<ts>` (read-only). A new branch `feat-bug` is
recreated with only Stachu's ops on it. Stachu re-does anything he
wants; the old work is archived.

Brutal but unambiguous. Equivalent of "I'm starting over."

### Why three modes, not one

I considered making the default `recompute` and not offering `soft`.
Tempting because it's what users expect from "revoke" — the
collaborator is GONE. But:

- Most revocations are amicable ("the project's done"). Stripping
  past work is unnecessary.
- `recompute` has a non-trivial UX (conflict resolution). Forcing it
  on every revoke means revoke is high-friction.
- `soft` matches GitHub's mental model. We don't need to invent
  worse semantics for nothing.

So: `soft` default, `recompute` and `fork` as opt-ins for
"actually I'm worried about what they did."

### Audit trail

Every revocation is itself an op on `share_ops`. `dark grants log
<user>` shows: when grant was made, what level, when revoked, what
mode, by whom. Same ops machinery — we get audit for free.

## Default `since` per stream

When user X grants user Y read access to a stream, Y's first sync
pull asks: "from when?"

| Stream | Default | Rationale |
|---|---|---|
| `packages`, `branches` | genesis | The projection (pkg.db) doesn't make sense without history. You'd be reading partial state. |
| `traces` | forward | Trace volume is high; old traces are usually noise. User opts in to history with `--with-history`. |
| `sessions` | forward | Same — sessions are time-bound. |
| `app:<id>` | per-app declared | App author chooses. CRDT-y apps want history (counters, sets); transactional apps usually want forward. |
| `account` | n/a | Never shared cross-user. |

The grant carries `default_since`; sync respects it. User can
override on first pull (`dark sync clone --since=genesis`) but the
hub enforces what was granted.

## Pattern grants — `key_glob`

Some real cases:

- "Share my entire `Darklang.Stdlib.*` namespace with Feriel
  read-only" → `(stream='packages', key_glob='Darklang.Stdlib.*',
  permission='read')`. The hub matches glob on key (which here is
  the package location string).
- "Share this one app" → `key_glob = '<app-uuid>'` exact match.
- "Share everything in branch `feat-bug`" → `(stream='packages',
  key_glob='@branch:<id>:*')`. (This requires `packages`'s key
  scheme to encode branch. In current design, the per-branch
  projection IS the per-branch container; the op's key on
  `packages` is the location. So "branch X's ops" needs a
  separate filter. **Note: this might be a flaw in the unified
  model.** Worth thinking about — the granularity of share-by-
  branch versus share-by-package-namespace is a UX decision that
  needs the storage layer to support both. Coming back to this.)

For v1, support exact and prefix only, no full regex. ~95% of
real grants are namespace-prefix or specific-app shapes.

## Hub's role in filtering

When the hub receives a `SyncRequest` from instance B asking for
ops from instance A's stream `packages`:

```
1. Hub knows A.user_id and B.user_id.
2. Hub queries grants where granter=A.user_id AND grantee=B.user_id
   AND stream='packages' AND not revoked AND not expired.
3. For each op A would have shipped, hub checks key_glob match
   against the granted patterns.
4. Hub forwards only the matching ops.
```

This is per-frame work — for a 1000-op sync, we're doing 1000
glob matches. With ~10 grants typical, that's 10K matches per
sync, fast.

The hub never deserializes payloads. It routes on `(stream, key)`
in the envelope. This is privacy-preserving even on the hub: the
hub knows what's being shared at coarse granularity (which streams,
which keys) but not the contents.

For E2E privacy of the contents themselves, see future iteration on
encryption (per the unified-model `stream_config.encryption_key`).

## Self-grant: a user's own instances

A user has many instances. Stachu-laptop and Stachu-major both
belong to Stachu. They're peers and need to sync everything.

Implementation: a user's instances have an *implicit grant* to
each other on every stream the user owns. This isn't a row in
`share_grants` — it's the rule "instances of the same user always
sync." The hub enforces it (when relaying, "is grantee.user_id ==
granter.user_id? → forward all").

Revoking a *device* (`dark revoke device stachu-laptop`) is
different from revoking a grant. Device revoke = mark the
instance's token as revoked at the hub; revoked instances can't
push or pull. Existing ops they wrote stay (soft revoke is
default for own-device cleanup too). For a stolen-laptop scenario,
add `revoke_mode='recompute'` to strip the laptop's ops from the
user's other instances after they've replayed them.

## What this changes about the unified model

The unified-model doc § 3 has a `stream_config` table with
`sync_enabled`, `retention_days`, `encryption_key`. Add:

```sql
ALTER TABLE stream_config ADD COLUMN
  default_share_since TEXT NOT NULL DEFAULT 'forward';
ALTER TABLE stream_config ADD COLUMN
  ownership_model TEXT NOT NULL;          -- 'per-key-first-writer' | 'user-private'
```

`ownership_model = 'per-key-first-writer'` covers packages,
branches, app:<id>; `'user-private'` covers traces, sessions,
account.

The `share_ops` stream itself has `ownership_model =
'hub-managed'` — only the hub appends to it (after a user makes a
grant change), and instances treat the hub's signature as
authoritative.

## What this changes about the daemon

`Builtins.Auth.canAppend` and `Builtins.Auth.canApply` are two
new builtins (or Dark fns, per iter 02 — I'd write these in Dark).
The daemon calls them at every op-write and every op-apply. If
they're slow they're hot-path bottlenecks.

Cache them aggressively:
- Per-(grantee_user, stream) cache of "currently active grants
  with patterns": a Map<UserId, List<(stream, glob, permission,
  expires)>>.
- Invalidated when a `share_ops` op lands.

Cold lookup: query grants table. Hot lookup: dict access.
Invalidation: any `share_ops` apply triggers the relevant entries
to drop.

## Open questions

1. **What if a user wants to retroactively *grant* history?** "I
   gave Feriel 'forward'-since access; now I want her to see my
   history too." Just modify the grant: increase scope. Sync:
   on next pull, hub serves the older ops. Instances detect the
   "newly-eligible" ops at apply-time and integrate them.

2. **How do we handle the migration of existing grants from today
   into this model?** There aren't any today (sync isn't shipped).
   First user is the test case.

3. **Does the hub need to verify each op's signature, or only
   route?** Both — verifying lets the hub reject obviously
   forged ops before they land in the recipient's `ops.db`.
   Performance cost: ~1ms per sig on commodity hardware. With
   batch syncs of 1000 ops, that's 1s of hub CPU, acceptable.
   Or: hub verifies at receive-from-A, not at forward-to-B,
   amortizing.

4. **What about anonymous/unauthenticated reads of public apps?**
   `*.dark.run/<app>` lets anyone hit the app. The HTTP request
   is forwarded to the app's instance via the hub's WS. This is
   a *transport* permission, not an op-level one — the public
   user isn't pulling ops, just sending HTTP. So
   `share_grants` doesn't model it; instead `public_apps` (per
   networking doc) does.

5. **When ops conflict because of overlapping grants** (User X and
   User Y both have write to namespace N, both rename `Foo` at
   the same wall-clock moment): standard conflict resolution
   (unified-model § 8). The grant model doesn't change anything;
   conflict on key, LWW or human resolves.

## Tying back

The base unified-model doc treats grants almost as an
afterthought. They're not — they're the entire trust model. But
folding them into the same primitives (ops, projections,
conflicts, resolutions) keeps the door open: every grant change
is an op, sync replicates them, conflicts on grants (rare!) are
resolved like any other conflict, the audit trail is the stream
itself.

Three modes of revoke (`soft` / `recompute` / `fork`) covers the
common case ("amicable end of collaboration") and the worst case
("compromised account") with a single parameter.

The hardest edge — what the apply-time check actually does — is
load-bearing for security and is the right thing to nail down
*before* any sync code ships. Once that's wrong it's hard to
fix.
