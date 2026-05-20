# Cohabitation — The Main Thing

A response to: *"The main thing of Darklang is the nature of
agents and people (with intent) co-habiting a recursive / nested
app thing. The CLI is one app; the PM is another sub-app; both
are built on top of (or connect to) a distributed app around. The
ops/updates/conflicts/views/resolutions thing is universal, and
apps communicate using Elmish-magical ways using event streams.
Permission systems are built around this idea."*

This is the unifying vision. Every substrate doc we've written is
one slice of it. Worth naming and stating clearly so future
design decisions can be checked against it.

## Built on the existing ethos

This isn't a new direction — it's the **realization** of the ethos
already documented in `~/vaults/Darklang Dev/04.Ethos/`. Seven
pillars, plus a coherent top-level vision. Cohabitation is what
all seven point at when taken together.

| Pillar | Ethos says | What cohabitation makes concrete |
|---|---|---|
| **Local-First** | Software runs on your machine; you own your data. Personal, private+secure. | Each inhabitant's substrate runs locally. Sync is *additive* — agents and people can collaborate, but the local store is yours. BYODs concurrently (per `Ethos.md`). |
| **Accessible** | Voice, low-floor, non-traditional inputs. Everyone-can-understand-PT. | Apps render to many surfaces (TUI, web, voice, reMarkable). Agents become a *primary input device* for users who can't / don't want to type code. |
| **Open** | Transparency, evaluating-ideas-on-merit, no walled garden. | The substrate is FOSS-shaped. Anyone can write an app or run an agent on the substrate. The system shows you what's happening, by default. |
| **Immediate** | Deployless. No compile-wait. Direct, universal access. | Hot-reload everywhere (per `HOT-RELOAD.md`). Materializations land mid-eval. Agents see your edits as they happen. Re-eval-until-feels-good (per FRONTIER). |
| **Malleable** | Alternative clients (greasemonkey-style). User-customizable. Reshape the tools. | Every app is a Dark program you can fork and refine. Default views per type are *overridable* (per `COMPOSABLE-MVU.md`). Your viewer doesn't have to look like mine. |
| **Composable** | Apps share fns. Sub-apps in big apps. MVU everywhere. | Composable MVU is the *only* surface every app + agent uses. Models compose by product; Msgs by sum; effects interleave. Recursion through composition. |
| **Simple** | PT-comprehensible. Small base language. Deployless. | The substrate's five primitives (ops/updates/conflicts/views/resolutions) are the entire vocabulary. Everything else is built from them. |

And the top-level `Ethos.md` lines that land hardest here:

> "Darklang is the whole thing, while entirely composable,
> extensible, and malleable."
>
> "A Distributed personal OS and sidekick, across all of your devices."
>
> "I want direct, immediate, universal access to my software and
> data and computers."
>
> "Remove as much glue code as possible."
>
> "If we can host such eternal elmish apps, the PM is just one of
> them." (from `MVU Everywhere.md`)

Cohabitation is the operating model that lets *all of these* be
true simultaneously. Without it, "PM is just one of them" remains
an aspiration; with it, it's mechanically achievable.

## The reframing

Darklang isn't *a language with apps built on it*. Darklang is
**a substrate for cohabitation** — where agents and people with
intent live together in nested, recursive apps, communicating
through a universal vocabulary of ops, events, conflicts, and
resolutions.

The language is the medium. The apps are the inhabitants. The
substrate is the city.

This shifts how to think about everything:

- **The PM** isn't infrastructure; it's an app — one nested under
  whatever app you're in.
- **The CLI** isn't a tool; it's an app — one of several
  co-running ones.
- **The viewer** (`VIEW-SKETCHES.md`), the SCM UI (`flows/`), the
  refactor pane, the prompt-daemon — all apps.
- **The materializer** isn't a special subsystem; it's an agent
  app running inside the substrate. Same vocabulary as you.

Critically: agents and people are the same kind of inhabitant.
Both have *intent*. Both produce ops. Both observe events. Both
get permission-checked. The substrate doesn't distinguish.

## Vault material this builds on

Several Darklang ethos docs already point in this direction:

- **MVU Everywhere** (`04.Ethos/Composable/MVU everywhere/`)
  states it directly: *"if we can host such eternal elmish apps,
  the PM is just one of them. MVU baked into the platform."*
  This is the same idea — but the vault note focuses on
  hot-reload + distribution; the bigger framing is **cohabitation
  through MVU**.
- **Composable.md** asks the right question: should apps share
  fns? Or is the lower fn the real shared thing? Resolves nicely
  in this framing: **fns live in the substrate** (a single
  package store); **apps are projections that use them**. Same
  fn, multiple uses, no copying.
- **AI Agent Concept** (`specs/ai-agent/AI_AGENT_CONCEPT.md`)
  treats the agent as an MCP-server-backed actor producing
  PackageOps within Branches. That's one application of the
  cohabitation model — the agent is an app like any other,
  producing the same ops a human would.
- **Agent Next Steps** identifies the gap: current agent gets a
  raw CLI string and has to guess. The fix maps onto cohabitation
  — give the agent *typed tools* (apps interact via typed
  protocols, not strings) and *context injection* (apps see each
  other's state via the same projection mechanism humans use).

## What's actually universal

The same five concepts thread through every app:

```
            ┌─ Ops           — atomic state changes (semantic, not textual)
            ├─ Updates       — model transitions (Msg → Model)
            ├─ Conflicts     — when two ops want incompatible things
            ├─ Views         — how state is rendered (text, JSON, HTML, ASCII)
            └─ Resolutions   — how conflicts get decided (auto / policy / human / fail)
```

Every app — PM, CLI, viewer, agent, SCM, materializer, the
darklang.com browser — uses these same five primitives.

- The **PM** has ops (`AddFunction`, `MoveItem`); updates (catalog
  refresh); conflicts (name collisions); views (search, tree);
  resolutions (namespace owner decides).
- The **CLI** has ops (commands invoked); updates (terminal model
  per Msg); conflicts (your edit vs. arriving sync); views
  (terminal rendering); resolutions (whatever the CLI app
  chooses).
- The **agent app** has ops (it produces them — same shape as
  human-produced ones); updates (its model: current task, plan,
  in-flight materializations); conflicts (its proposed body
  collides with existing code); resolutions (asks the human,
  retries, fails).
- The **viewer** has ops (user clicked, dove in, accepted refine);
  updates (Model evolution); conflicts (rare — UI ops mostly
  don't conflict); views (the visible UI); resolutions (n/a).
- The **SCM app** has ops (you know the drill).

The substrate provides these primitives once. Apps consume them.

## How apps communicate

The user's intuition: "Elmish-magical ways using event streams."

Concretely, two channels:

### Event streams (B4: `EVENT-STREAMS-AND-PARKING.md`)

Each app produces events; each app subscribes to whatever it
cares about. Subscription is *not* a function call — it's a
pattern-match against the bus.

```
PM app produces:                 Viewer app subscribes:
  PackageFnAdded                    Materialized name body
  PackageFnDeprecated   ───►        BodyChanged name newHash
  LocationMoved                     ConflictResolved id

Agent app produces:              Capability app subscribes:
  PromptReceived                    CapabilityRequested cap
  MaterializeAttempted  ───►        CapabilityGranted cap
  RefineRequested                   CapabilityDenied cap
```

Apps don't know each other; they know the bus. Add a new app —
it subscribes to what's relevant; existing apps don't notice.

This is the *Elmish-magical* part. The Elmish loop in each app
consumes a stream of Msgs; Msgs come from the bus; the rest is
pure functions. No callback graphs. No event observers. No
RPC. Just streams.

### Ops on shared state (B3 + LibMatter)

When an app needs to mutate persistent state (not its own Model),
it produces an op. The op goes through validation + conflict
detection + execution. Other apps see the result via their event
subscriptions.

This is the *single shared substrate* part. There's one canonical
package store. Apps don't have private package stores. They
have private *Models* (their UI state, their plans, their
caches); they share the *ops* that mutate the world.

## Recursive nesting — apps inside apps

A complex app contains sub-apps. The CLI contains:

- A prompt input sub-app
- A trace-display sub-app
- A search results sub-app
- An agent-conversation sub-app

The viewer (`VIEW-SKETCHES.md`) contains:

- The focus panel (one sub-app per zoom level — fn, expression,
  value)
- The dive-in stack (each dove-into thing is itself a sub-app)
- The event timeline (a sub-app)
- The sessions strip (a sub-app)

This is just composable MVU (`COMPOSABLE-MVU.md`) all the way
down. Models compose by product, Msgs by sum, views assemble,
effects interleave.

**Recursion**: nothing stops a sub-app from being another
instance of its parent. The trace viewer can show a trace that
includes traces. The prompt-daemon can spawn child prompt-daemons
(reasoning about sub-tasks). The PM can show the PM (browsing
itself).

When apps nest infinitely, you need the same controls at every
level — pause, dive in, zoom out. That's what makes the "fractal
materialization" idea concrete; the user can drill into any
materialization, see its sub-materializations, etc.

## Intent as a first-class concept

The user's phrasing: *"agents and people (with intent)
co-habiting."* This deserves a sentence.

Intent is what makes someone an actor in the substrate, not just
a producer of ops. A bot mindlessly producing ops is not an
agent in this sense — it has no intent, no goal-state, no way to
explain what it wants.

An *agent* (whether human or AI) carries:

- **A goal** — what they're trying to achieve
- **A plan** — current decomposition of the goal
- **Permissions** — what they're allowed to do
- **A trace of their actions** — what they've done so far
- **An identity** — for audit, attribution, and trust

This isn't optional metadata — it's the substrate's primary key
for understanding the system's state. "Why is this op being
produced?" is answerable by walking back to the agent's intent.

The substrate stores intent as first-class: every op is tagged
with the agent that produced it; every agent has a current goal +
plan as a Model; the viewer can render the goal-tree.

**Humans and AIs are both agents.** Same data shape. Same
permissioning. Different speed; different reliability;
different cost. The substrate doesn't care.

## How this lands against each substrate doc

### vs CLAIMS

The fifth claim — "the human is a materializer" — generalizes.
Replace with: **agents are participants**. Humans, LLMs, future
formal-verifiers, future test-synthesizers — all participate
through the same substrate, all with intent, all producing ops
through the conflict-resolution flow.

### vs CONFLICTS-AND-RESOLUTIONS

A conflict is now "two agents (with their respective intents)
want incompatible state changes." The resolution can be informed
by intent: "agent A is the human owner of this namespace; agent B
is an LLM doing speculative materialization; the human wins by
default." Permissions are intent-aware.

### vs SYNC-AND-STABILITY

Sync ops carry author identity (agent ID). Cross-instance, this
becomes "agent A on instance X did Y; replicate to instance Y."
Standard distributed-system stuff with the agent-identity making
permissions across instances workable.

### vs EVENT-STREAMS-AND-PARKING

Events carry producer identity. Subscribers can filter by
producer ("show me only ops by agent A"). Parking an agent's
frame doesn't park the agent — the agent's plan can continue;
the frame is just a single in-flight op.

### vs CAPABILITIES

Permission systems are *built around this idea*. Per the user's
explicit framing:

- Each **agent** has a permission set (granted caps)
- Each **app** declares its needed caps
- The substrate checks: agent ∩ app at each call site
- Different agents in the same app have different effective
  permissions — *the human can post to network; the LLM can't*

This makes the capability model agent-relative, not session-
relative. Stronger guarantees. The PM app is the same app whether
a human or an LLM is in it; what they can *do* in it differs by
agent identity.

### vs HOT-RELOAD

Hot-reload is the substrate notifying apps that the world
changed. Apps decide whether to react. Same channel
(BodyChanged event); subscribers' update fns decide.

### vs COMPOSABLE-MVU

Composable MVU is the *only* surface developers (human or
agent) need to learn. Every app — old, new, third-party —
follows the same shape. Onboarding a new app developer means
"learn MVU; the rest is library calls." Onboarding a new
*agent* means the same thing — read the API in the same way.

### vs VIEW-SKETCHES

The viewer is one app. Other apps exist alongside. The
"sessions strip" idea from VIEW-SKETCHES generalizes: it's an
app-switcher, not just a PDD-session-switcher. You see all your
running apps, switch focus, dive in.

### vs GRAPH-PROJECTION

Apps + agents + intents + ops fit naturally as a graph. Agents
are nodes; ops are edges; goal-trees are subgraphs; cross-agent
communication is edges; permissions are edge-labels. The graph
projection becomes the *universal app surface*.

## What changes in product positioning

If "the main thing is cohabitation," then Darklang's pitch shifts:

| Before | After |
|---|---|
| "A language for the cloud" | "A substrate where you and agents work together" |
| "PaaS that includes a language" | "A cohabitation environment, programmable" |
| "Like Heroku but typed" | "Like a city — laws (substrate), citizens (agents), buildings (apps)" |
| "Deploy faster" | "Work alongside agents on the same codebase, in real time" |

The pitch becomes about *who's in your editor with you*, not
*where your code runs*.

## How this shapes the work ahead

Concretely:

1. **The agent app is just an app.** Don't build it as a special
   subsystem; build it as one of several first-class apps,
   sharing the substrate with the CLI, viewer, SCM. Same ops,
   same events, same caps.
2. **The PM is just an app too.** The current "PM as infra"
   framing should become "PM as the canonical reference
   implementation of a substrate-app." Other apps learn from it.
3. **Permissions are per-agent, not per-session.** Refactor toward
   this. Today's session-scoped grants get re-scoped to
   agent-scoped.
4. **The event bus is shared across apps.** Not per-app. Apps
   subscribe to a substrate-wide bus; the substrate routes.
5. **Intent gets a first-class data model.** Probably:
   `Agent { id, identity, currentGoal, plan, permissionSet, traceHead }`.
6. **The viewer shows agents.** Not just "what's being
   materialized" but "who's doing what." Multi-user / multi-agent
   visibility is core, not bolted on.

## What stays untouched

- The runtime / interpreter doesn't change shape just because
  we're framing things as cohabitation. It still runs Dark code.
- The package store is still ops + locations + content. Just
  shared across more inhabitants.
- The compiler / type checker / etc. — same.

The cohabitation reframing is **above** the runtime, not inside.
It changes how we *use* and *talk about* the system, not how
the language itself works.

## Open questions

- **Multi-tenant boundaries.** When agents from different orgs
  share a substrate, where's the wall? Probably: namespaces +
  the existing permission model + an explicit "join this
  substrate" flow.
- **Adversarial agents.** Cohabitation assumes good-faith
  participation. What if an agent (or human) goes rogue?
  Capability gating + ops-are-attributed + revocation paths.
  Permission system as immune system.
- **Identity at scale.** OAuth-style identity for users we know
  how to do; what's the identity model for *agents*? A trusted
  agent run by user A is one thing; an autonomously-running
  agent has different needs. Probably: agents are first-class
  citizens with their own identity, owned-by + delegated-by
  relationships to humans.
- **What constitutes "an app"?** The boundary is fuzzy. Is a
  refactor an app or a one-shot agent action? Is a long-running
  search an app? Likely a continuum from "session-scoped one-shot
  action" through "long-running daemon" to "always-on background
  app."
- **Synchronization granularity.** Real-time collaboration
  (multi-cursor-style) requires sub-second sync. Async
  collaboration (PR-style review) is fine with minutes. Mixed?
  Per-app choice; the substrate supports both via the event
  stream.
- **What the substrate is *itself written in*.** Probably some
  in Dark, some in F#. The F# part is the irreducible
  bootstrap; everything above is Dark code on the substrate.
  The line moves over time (per `FRONTIER.md`'s "what F# should
  stop knowing").

## Vault landscape — where this work already has homes

A quick glance at `~/vaults/Darklang Dev/` shows substrate work
is *already organized by exactly the right concerns*. Each
substrate doc maps to existing vault territory:

| Substrate concept (this dir) | Vault home (preexisting) |
|---|---|
| CONFLICTS-AND-RESOLUTIONS | `05.Implementation/Ops and Playback/`, `WIP/specs/LibMatter/`, `WIP/specs/flows/conflict-resolution.md` |
| SYNC-AND-STABILITY | `05.Implementation/Sync and Distribution/`, `05.Implementation/CRDTs/`, `05.Implementation/Package Bootstrapping/` |
| EVENT-STREAMS-AND-PARKING | `05.Implementation/Execution/`, `05.Implementation/Queues, Workers, Feeds/` |
| CAPABILITIES | `05.Implementation/Purity, Effects, and Sandboxing/`, `05.Implementation/Accounts and Auth/` (identity side) |
| HOT-RELOAD | `04.Ethos/Composable/MVU everywhere/`, `05.Implementation/CLI/Apps/Hot-reloading.md` |
| COMPOSABLE-MVU | `04.Ethos/Composable/` (whole subtree), `05.Implementation/CLI/Apps/` |
| VIEW-SKETCHES | `05.Implementation/Editing/`, `05.Implementation/VS Code/`, `05.Implementation/Web/` |
| GRAPH-PROJECTION | `05.Implementation/Hashing and References/`, `05.Implementation/Matter Analysis/`, `05.Implementation/Matter Organization/` |
| COHABITATION (this doc) | `04.Ethos/` (the 7 pillars), `05.Implementation/AI/`, `05.Implementation/Remote Access and Control/` |
| ALGORITHM | `05.Implementation/AI/`, `05.Implementation/WIP/specs/ai-agent/` |
| CLAIMS | `04.Ethos/Ethos.md`, `04.Ethos/dl-ethos-vision.md`, `04.Ethos/Phrases, Slogans, etc..md` |
| (PDD work generally) | `05.Implementation/Metaprogramming and Reflection/`, `05.Implementation/Malleable and Mutable/` |

The Dev vault is **already organized around the substrate** —
sync, caps, MVU, hot-reload, ops/conflicts, AI/agents,
malleability all have dedicated folders. The sketches in
`pdd-thinking/` aren't introducing new concerns; they're stating
how the concerns connect. That convergence is itself evidence the
framing is right.

Folders relevant to cohabitation specifically that we haven't yet
threaded through: `Accounts and Auth` (identity model for
agents + humans), `Remote Access and Control` (cross-device
participation), `Crons / Queues, Workers, Feeds` (long-running
agents, scheduled apps), `Networking and Internet` (the
cross-instance fabric), `Future Environments` (Canvas UIs,
reMarkable, WASM — surfaces apps can render onto). All on the
roadmap implicitly; each becomes its own sketch when needed.

## Closing

The cohabitation framing isn't an extra layer of vocabulary —
it's the *unifying mental model* under which every substrate
piece we've sketched makes coherent sense:

- Conflicts (B2) are how two intents resolve.
- Sync (B3) is how intents propagate across machines.
- Events + parking (B4) are how apps + agents communicate.
- Capabilities (B5) are intent-relative permissions.
- Hot-reload (B6) is the substrate notifying participants.
- Composable MVU (B7) is the shape every app and agent takes.
- View sketches (B8) is what cohabitation looks like to the
  human.
- Graph projection (GRAPH-PROJECTION) is the unifying data
  shape.

Build for cohabitation, and the rest follows. Each of the seven
ethos pillars (`04.Ethos/`) is consistent with — and accelerated
by — this framing: local-first inhabitants on each machine,
accessible to anyone (humans + agents alike), open about who's
doing what, immediate in feedback, malleable in every surface,
composable to arbitrary nesting, simple in its five core
primitives.

The reframing in one sentence: **Darklang is not a place where
you write code; it's a place where you and your agents work,
together, on the same evolving thing — locally-first, accessibly,
openly, immediately, malleably, composably, simply.**
