# Identity — Humans + Agents

Loop TODOs T11-T13. Designs the identity model that gates
capabilities (B5), sharing (B7-B10), and cohabitation.

## Headline finding

Main has the **bones** of an identity model: `accounts_v0` table
with 4 seeded humans (Darklang, Stachu, Paul, Feriel), an
`AppState.accountID` carried through the CLI, every commit
attributed via `commits.account_id`, every trace optionally
attributed via `traces.account_id`. 53 backend references. The
shape is real and useful.

What's missing:

- **No authentication.** `login Stachu` just trusts you are
  Stachu. Fine on a single-user dev machine; not OK once peers
  enter the picture.
- **No agent identities.** Only human accounts.
- **No delegation.** A human can't say "this agent acts as me
  with bounded caps."
- **No external identity binding.** No mapping to Tailscale
  logins, OAuth, etc.
- **No per-account permission sets.** Caps are not yet a thing
  on main (per CAPABILITIES.md, that's pre-PDD work).
- **No revocation.**

This doc designs the gap. Most of it stays as Dark-side code
sitting atop a slightly-extended `accounts_v0` schema.

---

## Identity model (T11)

### Core types

Following the cohabitation framing — agents and humans are both
*inhabitants*, attribution-equal at the substrate level, but the
defaults and the auth path differ.

```fsharp
type IdentityKind =
  | Human                       // a person
  | Agent of owner: AccountId   // a non-human acting under a human's authority

type Account = {
  id           : Guid           // primary key (matches today's accounts_v0)
  name         : string         // display name (already exists)
  kind         : IdentityKind   // new
  ownerId      : Guid option    // for agents: who owns this; None for humans
  createdAt    : DateTime       // existing
  // -- new for identity work --
  trustProfile : TrustProfile   // default cap-set granted to this account
  archivedAt   : DateTime option   // soft-deletion / revocation marker
}

type TrustProfile =
  | Untrusted        // can read public namespaces, otherwise denied
  | Basic            // can do common work; impure caps require per-grant approval
  | Trusted          // owner-level; can do anything in own namespace
  | System           // privileged; reserved for the substrate itself
```

The `kind` field is the key cohabitation move: same primary key,
same attribution shape, different defaults.

### How accounts are created

- **Humans** — `dark account create <name>` plus an external
  identity binding (next subsection). Without binding, accounts
  remain `Untrusted`.
- **Agents** — `dark agent spawn --name <n> --owner <human-id>
  --caps <list>`. Agents are created *by* humans, *for* a task,
  with explicit caps. They inherit Basic trust unless escalated
  per-cap.

### External identity binding

Per `SYNC-AND-STABILITY.md` (T7-T10), peers identify via Tailscale
or token. Need to bind those external identities to Darklang
accounts.

```sql
-- Already on main:
-- CREATE TABLE accounts_v0 (id, name, created_at)

-- New table:
CREATE TABLE IF NOT EXISTS account_identities (
  account_id      TEXT NOT NULL REFERENCES accounts_v0(id),
  identity_kind   TEXT NOT NULL,   -- 'tailscale' | 'oauth-google' | 'token' | ...
  identity_value  TEXT NOT NULL,   -- e.g. 'stachu@stachu.net' for tailscale
  added_at        TIMESTAMP NOT NULL DEFAULT (datetime('now')),
  revoked_at      TIMESTAMP,
  PRIMARY KEY (account_id, identity_kind, identity_value)
);
CREATE INDEX IF NOT EXISTS idx_account_identities_lookup
  ON account_identities(identity_kind, identity_value) WHERE revoked_at IS NULL;
```

Lookup flow: an HTTP request arrives with `Tailscale-User-Login:
stachu@stachu.net` → server queries
`account_identities(identity_kind='tailscale', identity_value='stachu@stachu.net')`
→ returns `account_id` → uses for op authorship + cap check.

Multiple identities can bind to one account (Stachu has a
Tailscale login *and* a token *and* an OAuth-Google login —
all map to the same `account_id`).

### Agents — explicit shape

The cohabitation doc said agents carry: id, identity, currentGoal,
plan, permissionSet, traceHead. Concretely:

```fsharp
type Agent = {
  account     : Account            // points at the account this agent IS
                                   // (account.kind = Agent of ownerId)

  // Runtime / session state:
  currentGoal : Option<Goal>       // what they're trying to do
  plan        : Option<Plan>       // current decomposition
  traceHead   : Option<TraceId>    // their current execution trace
  status      : AgentStatus        // Running | Paused | Done | Failed | Revoked
  spawnedAt   : DateTime
}

type AgentStatus =
  | Running
  | Paused
  | Done
  | Failed of reason: string
  | Revoked of by: AccountId * at: DateTime
```

`Goal` + `Plan` are stub shapes — design later (T22 agent
runtime). The point is they're first-class state on the agent,
not buried in some service.

### Agents and the substrate

When an agent runs:

- Every op it produces carries `account_id = <agent's account>`.
- The trace records `account_id` for every fn call.
- Other apps see "agent X did this" — same UI shape as "human Y
  did this," just with `kind=Agent` displayed differently.
- The substrate doesn't distinguish. Cap checks, conflict
  dispatch, sync, hot-reload — all run the same code paths.

### What about anonymous?

`traces.account_id` already supports NULL ("anonymous runs").
Anonymous is for scripts that don't have a logged-in account —
e.g., a CI build of a public Stdlib fn. They get the minimum cap
set (essentially `Untrusted`). No write access to any namespace.

---

## Delegation (T12)

Permission delegation is **the bridge from "I trust this human"
to "I trust the agent this human is running."**

### The delegation contract

When Stachu spawns an agent:

```
dark agent spawn \
  --name "csv-helper" \
  --caps CapInvokeLLM,CapReadFile \
  --scope "User.Stachu.CSV.*" \
  --expires "2026-05-21T00:00:00Z"
```

This creates:

```fsharp
type Delegation = {
  id          : Guid
  ownerId     : AccountId          // Stachu
  agentId     : AccountId          // the new agent
  caps        : Set<Capability>    // exactly what the agent gets
  scope       : NamespaceScope     // where it can write
  expiresAt   : Option<DateTime>   // when the delegation auto-revokes
  revokedAt   : Option<DateTime>   // explicit revoke
  createdAt   : DateTime
  parentDelegation : Option<DelegationId>   // for sub-delegations
}

type NamespaceScope =
  | OwnerNamespaceOnly                    // can only write to ownerId's namespace
  | SpecificNamespaces of Set<Namespace>  // explicit list
  | ReadOnly                              // can't write at all
```

### What the agent can actually do

The agent's effective cap-set per op is:

```
effective_caps = agent.account.trustProfile.caps
              ∩ delegation.caps           -- bounded by what was delegated
              ∩ user's session cap-set    -- bounded by what owner has
```

Triple-intersection. An agent can never exceed what its delegation
permits, and a delegation can never exceed what the owner has at
the time of the agent's action.

### What gets recorded

Every op produced by an agent is recorded with both the
**author** (the agent's account_id) and the **delegation_id**
under which they acted:

```sql
-- Add to package_ops:
ALTER TABLE package_ops ADD COLUMN delegation_id TEXT
  REFERENCES delegations(id);
```

This is auditable: "Show me every op produced by my agents in
the last week" → query by `delegation_id WHERE owner = me`. "Why
did this fn change?" → look at the delegation; see what task the
agent was on.

### Revocation

Two paths:

- **Explicit:** `dark agent revoke <agentId>` sets
  `delegations.revoked_at`. Future cap-checks fail; in-flight
  agents see `Revoked` and stop on next call boundary.
- **Implicit (expiry):** `delegation.expires_at < now` →
  treated as revoked. Same effect.

In-flight semantics: an agent's current op finishes (it's not
killed mid-op), but no new ops are authorized.

### Sub-delegation

An agent can spawn sub-agents (think "agent decomposes task into
sub-tasks"). Sub-delegations:

- Must be a strict subset of the parent delegation's caps.
- Must be within the parent's scope.
- Can't outlive the parent.
- Are revoked transitively if the parent is revoked.

`parentDelegation` makes the chain explicit. Audit walks up the
chain.

### Delegation is itself an op

Creating, modifying, revoking a delegation are all **ops on the
wire**:

```
type DelegationOp =
  | CreateDelegation of Delegation
  | RevokeDelegation of DelegationId * by: AccountId * at: DateTime
  | UpdateDelegationCaps of DelegationId * newCaps: Set<Capability>
```

They sync just like package ops. A delegation revoked on one
instance propagates to all peers within sync latency. Same
machinery, no special case.

### Capability denials surface to the human

When an agent hits a cap it doesn't have, the conflict-resolution
dispatch (per `CONFLICTS-AND-RESOLUTIONS.md`) routes to its owner:

```
agent X tries: HttpClient.post "https://anywhere.com" body
agent X has: CapInvokeLLM, CapReadFile  (not CapWriteNet)
→ emit Conflict.CapabilityDenied(CapWriteNet, agent X, site)
→ dispatch: owner is Stachu; surface to Stachu via the event bus
→ Stachu sees: "csv-helper wants CapWriteNet for this call; allow once / always / deny"
→ resolution flows back as an op; agent resumes (or fails)
```

The owner decides. The substrate makes the decision visible and
auditable.

---

## Phase decision (T13)

**Identity ships in Phase 2.**

Specifically:

- **Phase 2 — "Identity + capabilities + conflicts."** Lands the
  schema additions (`kind` column on accounts_v0,
  `account_identities` table, `delegations` table, `delegation_id`
  column on package_ops), the Dark-side CLI (`dark account
  create`, `dark agent spawn`, `dark agent revoke`,
  `dark agent list`), the identity-binding flow for Tailscale
  (`dark link --tailscale`), and the cap-check integration.
- **share-1 from STABILITY-AND-SHARING.md is part of this phase**
  — it's the moment identity becomes *useful* for sync.

### Why Phase 2

- Bootstrap (Phase 1) doesn't need identity changes; it uses the
  existing accounts_v0 and the implicit-Darklang-account default.
- Sharing (Phase 3) **requires** identity (T11-T12) for op
  attribution + auth + delegation.
- Capabilities (T15 design / B5 ship) similarly require identity
  to scope per-account.
- Conflicts (T14) need identity to record "who's authoring the
  ops in conflict."

Identity is the **bridging chunk** between Phase 1 (local) and
Phase 3 (networked). It is also the unlock for cohabitation —
without identity, there are no agents to cohabit *with*.

### Sub-phasing if Phase 2 is too big

If Phase 2 ends up too packed, split:

- **Phase 2a:** human identity (`kind=Human`, `account_identities`,
  Tailscale binding, login flow). Enough to support sharing
  between humans.
- **Phase 2b:** agent identity (`kind=Agent`, delegations, sub-
  delegation, cap-deny routing). Enough to support PDD agents
  as opt-in inhabitants.

2a is sharing-blocking. 2b is PDD-blocking. They can ship as
sequential PRs within Phase 2.

---

## Cross-cutting

### vs CAPABILITIES (B5)

Identity is the *who*; capabilities are the *what they can do*.
Caps depend on identity (different accounts → different
cap-sets); identity doesn't depend on caps. Identity lands first
or in parallel.

### vs CONFLICTS-AND-RESOLUTIONS (B2)

Cap denials route through the conflict dispatch. Identity is
referenced in the audit log (who authored the conflicting op).

### vs SYNC-AND-STABILITY (B3/T7-T10)

Sharing is the moment identity becomes load-bearing. The
account-identity binding (share-1) is identity work.

### vs COHABITATION

This is the doc that makes cohabitation concrete: agents and
humans are typed sums in the same table; delegations make trust
explicit; revocation is real.

### vs AI-opt-in constraint

Critical: **agent identities are opt-in.** Creating an agent
requires explicit `dark agent spawn`. No instance has agents by
default. Reverting Phase 2b (removing all agent rows from
`accounts_v0`) leaves a fully-functional human-only Darklang.

---

## Open decisions

- **(Q-id-1) Token format.** What does a Bearer token look like?
  Random opaque string? JWT? Probably random opaque string with
  a separate revocation table. JWT-shaped is overkill for a
  small-team system.
- **(Q-id-2) Multiple agents from one delegation.** Can one
  delegation underwrite multiple agent instances (e.g., parallel
  workers)? Probably no — keep it 1-to-1; spawn N agents if you
  need N.
- **(Q-id-3) Cross-instance agent identity.** If Stachu's agent
  runs on his desktop and pushes ops to matter.darklang.com,
  does matter trust the agent? It trusts the *delegation*, which
  is content-addressed + signed by Stachu's account. Matter
  verifies the chain.
- **(Q-id-4) What about "AI Mode" — no agent identity needed.**
  If a user just runs a single LLM materialization synchronously
  (no spawn), is there an implicit ad-hoc agent identity? Or is
  the op attributed to the human with a "via LLM" tag? Probably
  the latter — single LLM calls are tools the human uses, not
  separate inhabitants.
- **(Q-id-5) Long-running agents on `matter.darklang.com`.**
  Can an agent live on the server, executing 24/7 against
  Stachu's namespace? Probably yes; it's just an agent with a
  long-lived delegation. But this needs a story for "what if the
  owner is offline?" — probably the agent pauses on any
  cap-denial that would surface to a human.
- **(Q-id-6) Identity for the substrate itself.** Some ops are
  produced by the substrate (e.g., `bootstrap` ops apply on
  first install). Probably the Darklang seeded account
  (`00000000-...-00000001`) serves this role. Confirm.
