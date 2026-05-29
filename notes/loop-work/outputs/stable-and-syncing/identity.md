# Identity

Designs the identity model: who authors ops, and the *intent* behind them.

## Where main is

Main has the bones: an `accounts_v0` table with seeded humans, an
`AppState.accountID` carried through the CLI, every commit attributed via
`commits.account_id`, every trace optionally attributed via
`traces.account_id`. The attribution shape is real.

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

## Anonymous

`traces.account_id` already allows NULL — anonymous runs (e.g. a CI build of a
public Stdlib fn). No write access to any namespace.
