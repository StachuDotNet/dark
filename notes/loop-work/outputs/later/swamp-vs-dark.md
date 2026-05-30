# Swamp vs Dark

*research note · swamp.club*

## What swamp.club is, fast

Swamp bills itself as "deterministic automation for AI agents." Five local-first primitives under `.swamp/` in a git repo:

- **Models** — Zod-typed wrappers around external systems (APIs, CLIs).
- **Definitions** — YAML instantiations, with CEL expressions for dynamic values.
- **Workflows** — multi-step DAGs: parallel jobs, nested composition, triggers.
- **Data** — versioned immutable artifacts, queryable by tag.
- **Vaults** — encrypted secrets, referenced via CEL.

The shift: agents are **authors**, not just runners. `swamp repo init --tool claude` writes skills into the agent's directory so it auto-discovers the framework; the user prompts something operational, the agent authors the typed automation, the user reviews and runs. One line: move agent work from "one-off scripts that rot" to "typed, composable models that persist."

## Where the two converge

Both bet on the same shape — agents need structured, durable, typed scaffolding, not chat-time scripts — and both insist "deterministic" and "agentic" aren't opposed: the agent writes the deterministic part once, it runs the same way every time.

| Concern | Swamp | Dark |
|---|---|---|
| Locus | Local-first git repo | Local-first substrate |
| Storage | `.swamp/` files | content-addressed values; ops on the event-bus |
| Agent posture | author + executor | inhabitant with intent, caps, delegation |
| Outputs | immutable artifacts, queryable by tag | ops + projections; typed events |
| Auth on tools | implicit (agent writes the model) | per-builtin cap tags + per-agent grants |
| Composition | workflow DAGs | composable MVU, sub-apps down |

The ops-vs-projections lens sharpens the difference. Swamp's "Data" conflates the two: an artifact is both the record of what ran and the thing you query. Dark separates them — **ops** are the durable log (what happened), **projections** are the queryable views built from replaying them. Swamp queries artifacts directly; Dark queries projections over an op stream.

## Three ideas worth borrowing

### 1. The recipe as a first-class inhabitant

Dark treats the agent as an actor with intent; the **plan it executes is not yet a thing**. Swamp's bet: the plan deserves to be a typed, named, versioned, forkable artifact — not a transient chat thought. In Dark terms a workflow is just Dark code (a sequence of ops, or a composable-MVU shape) emitted onto the event-bus, replayable like any other op stream, owned by an account, cap-gated. Spec it as Dark, not YAML — Dark owns its host language. See [cohabitation.md](../pdd/cohabitation.md), [composable-mvu.md](../pre-s-and-s/composable-mvu.md).

### 2. Tag-and-query as the default projection

Swamp tags every artifact and queries by CEL. Dark records everything as ops but the query surface is thin. The move is native to the ops/projections model: a tagged-event projection is just another view folded from the op stream. Generalize the capability audit log ([capabilities.md](../pre-s-and-s/capabilities.md)) — every substrate event carries `tags`, the viewer ([view-sketches.md](../pdd/view-sketches.md)) gets a filter bar. Trace-as-log becomes trace-as-projection.

### 3. The `init` ritual

`swamp repo init --tool cursor` writes per-tool skills so the agent discovers the framework. Dark has `docs for-ai` but no "drop me in and the agent learns what's here" step. A `dark init --agent claude|cursor|generic` deriving per-tool skills from that same source is small and high-leverage on first-run, and pairs with the AI-opt-in story: `init` is the moment the user decides to involve an agent at all.

A fourth, softer one: agent-authored typed-tool packages. With caps + delegation in place, an agent should be able to author a Dark package wrapping an external API, declare the cap-set it needs, and have the owner approve it into the substrate — Swamp's "Models," but as cap-gated Dark code rather than F#-frozen builtins.

## Where Dark should not follow

- **YAML + Zod + CEL.** A tri-lingual stack signals a tool that doesn't own its host language. Dark's bet is the language *is* the substrate — workflows, schemas, expressions all become Dark.
- **Repo-bound `.swamp/`.** Dark's substrate is the op stream + sync, not a git directory.
- **Vaults as a borrowed primitive — but the gap is real.** Dark has `CapSendSecret` as a gate with no place the secret lives. A `vault` projection + `CapReadVault(name)` fits the cap model cleanly; the *primitive* is worth specing, the *framing* needn't be copied.

## Headline

Swamp arrived by instrumenting an existing agent pipeline; Dark is designing the substrate from scratch around cohabitation. The two to steal hardest:

1. **The recipe as a first-class inhabitant** — Dark has the actor, not yet the plan.
2. **Tag-and-query as the default projection** — Dark records everything, queries little.

Both drop cleanly into the ops/projections model and shorten the path from "agent works most of the time" to "agent's work is observable, reviewable, sharable."

---

**Sources**

- https://swamp.club/
- https://github.com/systeminit/swamp
- [cohabitation.md](../pdd/cohabitation.md), [capabilities.md](../pre-s-and-s/capabilities.md), [identity.md](identity.md), [event-bus.md](../pre-s-and-s/event-bus.md), [view-sketches.md](../pdd/view-sketches.md)
