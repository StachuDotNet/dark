# Cohabitation

The unifying vision, named so future design decisions can be checked against it.

> **Overlap note.** The collaboration *mechanics* — self/pair/public sharing,
> namespace ownership, approval-as-ops — live in [sync.md](../stable-and-syncing/sync.md). This doc is
> the mental model above that: who inhabits the substrate, what vocabulary they
> share, and why agents and humans are the same kind of inhabitant. Where this
> doc touches synchronization, it defers to [sync.md](../stable-and-syncing/sync.md).

## The reframing

Darklang isn't *a language with apps built on it*. Darklang is **a substrate for
cohabitation** — where agents and people with intent live together in nested,
recursive apps, communicating through a universal vocabulary of ops, events,
conflicts, and resolutions.

The language is the medium. The apps are the inhabitants. The substrate is the
city.

This shifts how to think about everything:

- The **PM** isn't infrastructure; it's an app.
- The **CLI** isn't a tool; it's an app.
- The **viewer**, the SCM UI, the refactor pane, the prompt-daemon — all apps.
- The **materializer** isn't a special subsystem; it's an agent app running
  inside the substrate, using the same vocabulary as everyone else.

Agents and people are the same *kind* of inhabitant: both have intent, both
produce ops, both observe events, both get permission-checked. The substrate
doesn't distinguish at the mechanism level.

**But AI agents are opt-in inhabitants, not foundational ones.** A Darklang
instance with no AI is still a complete instance — human-only cohabitation
(single user, or several humans, no agents) is the *primary* configuration, not
a degraded one. AI agents enter only when the user explicitly grants
`CapInvokeLLM` and any associated caps (see [capabilities.md](../pre-s-and-s/capabilities.md)).
The mechanism doesn't care who is present; the *defaults* do.

Cohabitation isn't "humans and AIs forced together"; it's "*whoever is present*
operates by the same rules." Sometimes that's one human. Sometimes two humans
pairing. Sometimes a human with one or more agents they've opted into. The
substrate is the same in each case.

## What's actually universal

Five concepts thread through every app:

- **Ops** — atomic, semantic (not textual) state changes.
- **Updates** — model transitions (Msg → Model).
- **Conflicts** — when two ops want incompatible things.
- **Views** — how state is rendered (text, JSON, HTML, ASCII).
- **Resolutions** — how conflicts get decided (auto / policy / human / fail).

PM, CLI, viewer, agent, SCM, materializer — each has its own ops, updates,
conflicts, views, resolutions. The substrate provides the primitives once; apps
consume them.

This is the ops-vs-projections lens stated as product vocabulary: ops and their
conflicts/resolutions are the durable, syncable record; updates and views are
projections of that record. An app's private Model is a projection it folds from
the events it subscribes to; the shared world is the op stream.

## How apps communicate

Two channels, both shared across the substrate:

- **Event streams.** Each app produces events; each app subscribes to whatever
  it cares about. Apps don't know each other; they know the bus. Add a new app —
  it subscribes to what's relevant, and existing apps don't notice. (The
  *Elmish-magical* part: each app's update fn consumes a stream of Msgs; Msgs
  come from the bus; the rest is pure functions. No callback graphs, no RPC,
  just streams.) See [event-bus.md](../pre-s-and-s/event-bus.md).
- **Ops on shared state.** When an app needs to mutate persistent state (not its
  own Model), it produces an op. The op goes through validation, conflict
  detection, execution. Other apps see the result via their event
  subscriptions. (The *single shared substrate* part: one canonical package
  store. Apps share the *ops* that mutate the world; they keep their own
  *Models* private.)

## Recursive nesting

A complex app contains sub-apps; sub-apps can contain sub-apps. Composable MVU
all the way down — apps compose, Msgs route, views assemble, effects interleave
(see [composable-mvu.md](../pre-s-and-s/composable-mvu.md)).

Nothing stops a sub-app from being another instance of its parent. The trace
viewer can show a trace that includes traces. The PM can show the PM (browsing
itself). The prompt-daemon can spawn child prompt-daemons.

When apps nest arbitrarily deep, you need the same controls at every level —
pause, dive in, zoom out. That's what makes "fractal materialization" concrete.

## Intent as a first-class concept

The "with intent" part of "agents and people co-habiting" is load-bearing.
Intent is what makes someone an actor in the substrate, not just a producer of
ops. A bot mindlessly producing ops is not an agent in this sense — it has no
goal-state, no way to explain what it wants.

An *agent* (human or AI) carries:

- A **goal** — what they're trying to achieve.
- A **plan** — current decomposition of the goal.
- **Permissions** — what they're allowed to do.
- A **trace of their actions** — what they've done so far.
- An **identity** — for audit, attribution, and trust.

This isn't optional metadata. "Why is this op being produced?" should be
answerable by walking back to the agent's intent. This is exactly the `Intent`
shape that rides on every op: identity (chaining to a responsible human),
originating instance, reason, context. (The fuller identity model is a later concern.)

**Humans and AIs are both agents.** Same data shape, same permissioning.
Different speed, reliability, and cost. The substrate doesn't care.

## What changes in product positioning

| Before | After |
|---|---|
| "A language for the cloud" | "A substrate where you and agents work together" |
| "PaaS that includes a language" | "A cohabitation environment, programmable" |
| "Like Heroku but typed" | "Like a city — laws (substrate), citizens (agents), buildings (apps)" |
| "Deploy faster" | "Work alongside agents on the same codebase, in real time" |

The pitch becomes about *who's in your editor with you*, not *where your code
runs*.

## What stays untouched

- The runtime / interpreter doesn't change shape because we frame things as
  cohabitation. It still runs Dark code.
- The package store is still ops + locations + content — just shared across more
  inhabitants.
- The compiler / type checker — same.

The cohabitation framing is **above** the runtime, not inside it. It changes how
we use and talk about the system, not how the language itself works.

## Open questions

- **Multi-tenant boundaries.** When agents from different orgs share a
  substrate, where's the wall?
- **Adversarial agents.** Cohabitation assumes good-faith participation. What if
  an agent (or human) goes rogue? Capability gating, attributed ops, and
  revocation paths make the permission system the immune system (see
  [capabilities.md](../pre-s-and-s/capabilities.md)).
- **Identity at scale.** A trusted agent run by user A is one thing; an
  autonomously-running agent is another. The identity model for agents is in
  its own (later) concern; scale is still open.
- **What constitutes "an app"?** The boundary is fuzzy — likely a continuum from
  a session-scoped one-shot action, through a long-running daemon, to an
  always-on background app.
- **Synchronization granularity.** Real-time collaboration (multi-cursor-style)
  wants sub-second sync; async PR-style review tolerates minutes. A per-app
  choice; the mechanics are in [sync.md](../stable-and-syncing/sync.md).

## Closing

The cohabitation framing isn't an extra layer of vocabulary — it's the unifying
mental model under which the rest of the design coheres. Conflicts are how two
intents resolve. Sync is how intents propagate across machines. Events are how
apps and agents communicate. Capabilities are intent-relative permissions.
Hot-reload is the substrate notifying participants that a body moved. Composable
MVU is the shape every app takes.

Build for cohabitation, and the rest follows.

In one sentence: **Darklang is not a place where you write code; it's a place
where you — and, if you opt in, your agents — work together on the same evolving
thing.**
