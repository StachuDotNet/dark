# Identity

Designs the identity model: who authors ops, and the *intent* behind them.

> **Punted to later — S&S needs only a thin slice.** To sync safely between *any* members of
> the tailnet (Stachu's machines, Ocean, coworkers — N of them), all that's required is a
> `Tailscale-User-Login` → account mapping so
> ops carry real authorship, plus a **stable, structured `Intent`** (below). The rest of this
> doc — agent identities, the owner-chain recursion, sub-agents — is later depth, not needed
> to reach print-md sync. The thin slice is effort 8 in the spine.

## Where main is

Main has the bones: an `accounts_v0` table with seeded humans, an
`AppState.accountID` carried through the CLI, every commit attributed via
`commits.account_id`, every trace optionally attributed via
`traces.account_id`. The attribution shape is real.

> **Prework-verified (schema).** `accounts_v0` is deliberately thin — `(id TEXT PK, name TEXT
> UNIQUE, created_at)` — which is exactly the `Account` record below, and the `UNIQUE name` is what
> a `Tailscale-User-Login` upserts against. Crucially, **attribution today is *commit*-grained, not
> *op*-grained**: `package_ops` has **no `account_id` column** (only `id, op_blob, branch_id,
> commit_hash, applied, propagation_id, created_at`). So "every op carries an `Intent`" (below) is
> genuinely *new* — it means embedding `Intent` *inside* the serialized `PackageOp` (a
> ProgramTypes/hash-affecting change), not adding a SQL column. Until then, authorship rides the
> commit's `account_id`. This is why the S&S thin slice attributes at commit granularity, not
> per-op (see [pr-sync-read-write.md](../stable-and-syncing/pr-sync-read-write.md)).

Missing: agent identities, and a model for the *intent* behind an op (the
reason + context a given identity is acting under).

## Core model

Humans and agents are both inhabitants — attribution-equal at the substrate
level. An agent always acts *under* some owning identity, which is itself
either a human or another agent (sub-agents). That recursion is the whole
point: trust chains back to a human.

```fsharp
type Identity =
  | Human of AccountID
  | Agent of id * owner: Identity
```

`Identity` is not stored as a field on the account record — it's derived from
the account graph (who owns whom). The account record stays thin:

```fsharp
type Account = {
  id        : Guid       // primary key (matches accounts_v0)
  name      : string     // display name
  createdAt : DateTime
}
```

The substrate doesn't branch on `Human` vs `Agent`: ops, traces, conflict
dispatch, sync, hot-reload all run the same paths. The distinction is for
display and for resolving intent back to a responsible human.

## Intent

Tracing cares about the *source of intent*, not the mechanics. An op is
produced by some `Identity` on some Dark instance, for some reason. Model that
directly:

```fsharp
type Intent = {
  identity : Identity   // who is acting (chains to a human via owner)
  instance : InstanceID // which Dark instance the action originated on
  reason   : string     // what this action is for
  context  : string     // surrounding situation / task
}
```

Every op carries an `Intent`. "Why did this fn change?" answers by reading the
intent — the identity, the instance it came from, and the reason. Walking the
`owner` chain on the identity always lands on a human.

**`Intent` is a ProgramTypes type and must stay stable.** Because every op carries it and ops
sync + replay, `Intent` is serialized and hash-affecting — so its PT shape needs to be settled
and rarely-changed (a PT change ripples to serialization + package-ref hashes). Keep it small
and structured (the four fields above), not a free-form bag, so it stays stable across the
fleet.

## Anonymous

`traces.account_id` already allows NULL — anonymous runs (e.g. a CI build of a
public Stdlib fn). No write access to any namespace.
