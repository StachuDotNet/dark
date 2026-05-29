# Cohabitation — The Main Thing

A response to: *"The main thing of Darklang is the nature of
agents and people (with intent) co-habiting a recursive / nested
app thing. The CLI is one app; the PM is another sub-app; both
are built on top of (or connect to) a distributed app around. The
ops/updates/conflicts/views/resolutions thing is universal, and
apps communicate using Elmish-magical ways using event streams.
Permission systems are built around this idea."*

This is the unifying vision. Worth naming so future design
decisions can be checked against it.

## The reframing

Darklang isn't *a language with apps built on it*. Darklang is
**a substrate for cohabitation** — where agents and people with
intent live together in nested, recursive apps, communicating
through a universal vocabulary of ops, events, conflicts, and
resolutions.

The language is the medium. The apps are the inhabitants. The
substrate is the city.

This shifts how to think about everything:

- The **PM** isn't infrastructure; it's an app.
- The **CLI** isn't a tool; it's an app.
- The **viewer**, the SCM UI, the refactor pane, the prompt-daemon
  — all apps.
- The **materializer** isn't a special subsystem; it's an agent
  app running inside the substrate. Same vocabulary as you.

Critically: agents and people are the same *kind* of inhabitant —
both have intent, both produce ops, both observe events, both get
permission-checked. The substrate doesn't distinguish at the
mechanism level.

**But: AI agents are opt-in inhabitants, not foundational ones.**
A Darklang instance with no AI is still a complete instance —
human-only cohabitation (single user, or multiple humans, no
agents) is the *primary* configuration, not a degraded one. AI
agents enter only when the user explicitly grants `CapInvokeLLM`
(and any associated caps). The mechanism doesn't care; the
*defaults* do.

Cohabitation isn't "humans + AIs forced together"; it's
"*whoever is present* operates by the same rules." Sometimes
that's one human. Sometimes two humans pair-programming.
Sometimes a human with one or more agents they've opted into.
The substrate is the same in each case.

## What's actually universal

The same five concepts thread through every app:

- **Ops** — atomic state changes (semantic, not textual)
- **Updates** — model transitions (Msg → Model)
- **Conflicts** — when two ops want incompatible things
- **Views** — how state is rendered (text, JSON, HTML, ASCII)
- **Resolutions** — how conflicts get decided (auto / policy /
  human / fail)

PM, CLI, viewer, agent, SCM, materializer — each has its own ops,
updates, conflicts, views, resolutions. The substrate provides
the primitives once; apps consume them.

## How apps communicate

Two channels, both shared across the substrate:

- **Event streams.** Each app produces events; each app
  subscribes to whatever it cares about. Apps don't know each
  other; they know the bus. Add a new app — it subscribes to
  what's relevant; existing apps don't notice. (The
  *Elmish-magical* part: each app's update fn consumes a stream
  of Msgs; Msgs come from the bus; the rest is pure functions.
  No callback graphs. No RPC. Just streams.)
- **Ops on shared state.** When an app needs to mutate persistent
  state (not its own Model), it produces an op. The op goes
  through validation, conflict detection, execution. Other apps
  see the result via their event subscriptions. (The *single
  shared substrate* part: one canonical package store. Apps
  share the *ops* that mutate the world; they keep their own
  *Models* private.)

## Recursive nesting

A complex app contains sub-apps; sub-apps can contain sub-apps.
Composable MVU all the way down — Models compose by product,
Msgs by sum, views assemble, effects interleave.

Nothing stops a sub-app from being another instance of its
parent. The trace viewer can show a trace that includes traces.
The PM can show the PM (browsing itself). The prompt-daemon can
spawn child prompt-daemons.

When apps nest infinitely, you need the same controls at every
level — pause, dive in, zoom out. That's what makes "fractal
materialization" concrete.

## Intent as a first-class concept

*"Agents and people (with intent) co-habiting."* The "with
intent" part is load-bearing.

Intent is what makes someone an actor in the substrate, not just
a producer of ops. A bot mindlessly producing ops is not an agent
in this sense — it has no goal-state, no way to explain what it
wants.

An *agent* (whether human or AI) carries:

- A **goal** — what they're trying to achieve
- A **plan** — current decomposition of the goal
- **Permissions** — what they're allowed to do
- A **trace of their actions** — what they've done so far
- An **identity** — for audit, attribution, and trust

This isn't optional metadata. "Why is this op being produced?"
should be answerable by walking back to the agent's intent.

**Humans and AIs are both agents.** Same data shape. Same
permissioning. Different speed; different reliability; different
cost. The substrate doesn't care.

## What changes in product positioning

| Before | After |
|---|---|
| "A language for the cloud" | "A substrate where you and agents work together" |
| "PaaS that includes a language" | "A cohabitation environment, programmable" |
| "Like Heroku but typed" | "Like a city — laws (substrate), citizens (agents), buildings (apps)" |
| "Deploy faster" | "Work alongside agents on the same codebase, in real time" |

The pitch becomes about *who's in your editor with you*, not
*where your code runs*.

## What stays untouched

- The runtime / interpreter doesn't change shape just because
  we're framing things as cohabitation. It still runs Dark code.
- The package store is still ops + locations + content. Just
  shared across more inhabitants.
- The compiler / type checker — same.

The cohabitation reframing is **above** the runtime, not inside.
It changes how we *use* and *talk about* the system, not how
the language itself works.

## Open questions

- **Multi-tenant boundaries.** When agents from different orgs
  share a substrate, where's the wall?
- **Adversarial agents.** Cohabitation assumes good-faith
  participation. What if an agent (or human) goes rogue?
  Capability gating + ops-are-attributed + revocation paths.
  Permission system as immune system.
- **Identity at scale.** OAuth-style identity for users we know.
  What's the identity model for *agents*? A trusted agent run by
  user A is one thing; an autonomously-running agent is another.
- **What constitutes "an app"?** The boundary is fuzzy. Is a
  refactor an app or a one-shot agent action? Likely a continuum
  from session-scoped one-shot action through long-running
  daemon to always-on background app.
- **Synchronization granularity.** Real-time collaboration
  (multi-cursor-style) requires sub-second sync. Async (PR-style
  review) is fine with minutes. Per-app choice.

## Closing

The cohabitation framing isn't an extra layer of vocabulary —
it's the *unifying mental model* under which every substrate
piece makes coherent sense. Conflicts are how two intents
resolve. Sync is how intents propagate across machines. Events
are how apps + agents communicate. Capabilities are
intent-relative permissions. Hot-reload is the substrate
notifying participants. Composable MVU is the shape every app
takes.

Build for cohabitation, and the rest follows.

The reframing in one sentence: **Darklang is not a place where
you write code; it's a place where you (and, if you opt in, your
agents) work, together, on the same evolving thing.**
