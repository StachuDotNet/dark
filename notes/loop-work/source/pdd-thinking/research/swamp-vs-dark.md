# Swamp vs Dark — How swamp.club's ideas could reshape Dark

*2026-05-26 · research note*

## What swamp.club is, fast

Swamp Club bills itself as **"deterministic automation for AI agents."** Five primitives, all local-first, stored under `.swamp/` in a git repo:

- **Models** — Zod-typed wrappers around external systems (cloud APIs, CLIs, services). Define metadata, args, methods, inputs.
- **Definitions** — YAML instantiations of models, with **CEL expressions** for dynamic values and cross-model references.
- **Workflows** — multi-step DAGs with parallel jobs, nested composition, dependency ordering, trigger conditions.
- **Data** — *"versioned, immutable artifacts (resources, logs, files) produced by method runs. Searchable by tags."*
- **Vaults** — encrypted secret storage, *"referenced in definitions via CEL expressions."*
- **Extensions** — agent-created capabilities that *"become full citizens immediately."*

Agents are **authors**, not just runners. `swamp repo init --tool cursor` (or `--tool claude`) writes skills into the tool's directory (`.claude/skills/`, `.cursor/rules/`) so the agent auto-discovers the framework. The user prompts something operational (*"manage my EC2 fleet — inventory every instance, flag anything without a cost-center tag"*), the agent **authors** the typed automation, the user reviews and runs.

The pitch in one line: shift agent work from *"one-off scripts that rot"* to *"typed, composable models that persist and improve."*

## What Dark currently is in the same neighborhood

Dark's pdd-thinking corpus (cohabitation + capabilities + identity + event streams + composable MVU) names its own five substrate primitives — **Ops · Updates · Conflicts · Views · Resolutions** — and a coherent vision: humans + agents as opt-in inhabitants of a shared, local-first substrate, every app built from the same vocabulary.

Concrete pieces already on paper:

- **Cohabitation** as the unifying frame: agents and humans both have intent, both produce ops, both get cap-checked, both attributed via `account_id` everywhere.
- **Capabilities** with per-builtin tags, layered grants (per-invocation → session → install → floor), audit log (`capability_log_v0`), AI-opt-in gating via `CapInvokeLLM`.
- **Identity + delegation**: humans + agents are typed sums in `accounts_v0`; agents bounded by a `Delegation { caps, scope, expiresAt }`; revocation is an op on the wire.
- **Event streams + parking** for inter-app communication, no callback graphs.
- **Composable MVU** as the only authoring surface.

## Where the visions converge

Both projects are betting on the same shape:

| Concern | Swamp | Dark (pdd-thinking) |
|---|---|---|
| Locus | Local-first, on your machine | Local-first (first ethos pillar) |
| Storage | `.swamp/` in git | SQLite package store; ops on the wire |
| Agent posture | Author + executor, not chat-driver | Inhabitant with intent, caps, delegation |
| Outputs | Immutable versioned artifacts, queryable by tag/CEL | Traces, ops, `ConflictResolved` events |
| Secrets | Vaults, CEL-referenced | (gap on main; `CapSendSecret` is the *gate*, vault unspecified) |
| Auth on tools | Implicit (agent writes Zod model) | Per-builtin cap tags + per-agent grants |
| Composition | Workflow DAGs, parallel, nested | Composable MVU, sub-apps all the way down |
| Onboarding ritual | `swamp repo init` writes per-tool skills | CLAUDE.md + `docs for-ai`; no `init` |

The shared bet: agents need **structured, durable, typed scaffolding** around them, not improvised chat-time scripts. Both projects are explicit that *"deterministic"* and *"agentic"* are not opposed — the agent writes the deterministic part once, then the deterministic part runs the same way every time.

## What swamp could reshape in Dark — five ideas worth borrowing

### 1. Workflow-as-first-class-artifact

The COHABITATION doc gestures at *"intent + plan + traceHead"* on every agent, but **the workflow itself is not yet a thing**. Swamp's bet is that the *plan* deserves to be a typed, versioned, named, sharable artifact — not a transient agent thought living in chat history.

In Dark terms: a workflow would be a Dark value (a composable-MVU `update` shape, or a sequence of typed Msgs), stored in the package store, runnable, forkable, observable, owned by an account, gated by caps. Today's pdd-thinking treats agents as actors; swamp suggests the **recipes** they execute should also be inhabitants — first-class package-store entities parallel to `fn`/`type`/`value`.

**Action:** spec a `Workflow` package-store entity. Make it Dark code rather than YAML (Dark owns its host language; swamp doesn't).

### 2. Tag-and-query for trace data

Swamp tags every data artifact at creation, lets you query by CEL. Dark already records everything (traces, ops, `ConflictResolved` events, planned `MaterializeAttempted` / `RefineRequested` events) but the **query surface** is the gap — there's nowhere to ask *"show me every `MaterializeAttempted` for this fn in this branch over the last week."*

The `capability_log_v0` table is essentially the right shape, scaled to one concern. Generalize: every substrate event gets `tags : Map<String,String>`; the viewer (per VIEW-SKETCHES) gets a CEL-ish filter bar. Trace-as-log becomes trace-as-database.

**Action:** add a `tags` column to traces + events; ship a query-by-tag view in the viewer. CAPABILITIES already lists this style of query in its audit-pivots section — generalize it.

### 3. The `init` ritual

`swamp repo init --tool cursor` writes skills into the tool-specific directory so the agent discovers the framework automatically. Dark has CLAUDE.md and `./scripts/run-cli docs for-ai`, but no equivalent *"drop me in and the agent learns what's here"* ritual.

A `dark init --agent claude|cursor|generic` that writes per-tool agent skills derived from the same source as `docs for-ai` is small, concrete, ships in days, and lowers the activation energy for the first agent session by an order of magnitude. It also pairs naturally with the AI-opt-in story — `dark init` is the moment the user *decides* to involve an agent at all.

**Action:** ship `dark init --agent <tool>`. Tiny PR; high leverage on first-run experience.

### 4. Models as agent-authored typed wrappers

Swamp's Models are typed wrappers around external systems, **written by the agent**, with the schema (Zod) being the contract. Dark's Builtins serve this purpose for the language, but they're F#-authored, gated by the `Builtin.X` namespace, and not extensible from inside the substrate.

With caps + delegation in place, an agent should be able to **author Dark packages that wrap external APIs**, declare the cap-set those packages require (per CAPABILITIES §"User-defined fns — effective caps"), and have the substrate gate them on grant. The mechanism is already implied — what's missing is foregrounding the workflow: agent proposes a typed external-tool package, declares effective caps, owner approves the cap set, the package becomes usable system-wide.

**Action:** an explicit "agent authors a typed-tool package, declares caps, owner approves, package is now a substrate citizen" flow. Effective-cap computation for user-defined fns is the enabler.

### 5. Vaults as a substrate primitive

Classic Dark had secrets. The substrate roadmap doesn't yet name a vault primitive — `CapSendSecret` exists as a *gate*, but where the secret *lives* is unspecified. Swamp's Vaults (encrypted, referenced by expression, scoped to a definition) suggest a substrate-level `vault_v0` table + `CapReadVault(name)` cap. Cleanly composable with the existing cap model; gives `CapSendSecret` something concrete to gate.

**Action:** spec a vault primitive in CAPABILITIES.md alongside `CapSendSecret`. Probably a few-table addition; mostly Dark-side surface.

## Where Dark should *not* follow

- **YAML + Zod + CEL.** Dark's bet is that the language *is* the substrate — workflows, schemas, and expressions all become Dark code. Swamp's tri-lingual stack (YAML config + Zod schema + CEL expression) is a sign of a tool that doesn't own its host language; Dark does, and would lose the bet by importing that shape.
- **Repo-bound `.swamp/`.** Dark's substrate is the package store + (eventually) matter.darklang.com, not a git directory. The "in a git repo" framing is a swamp constraint Dark doesn't have to inherit.
- **The "extensions are full citizens immediately" framing.** This is already Dark's strongest position — cohabitation makes it structural, not aspirational. Dark doesn't need to copy the framing; it should ship the substrate that makes the framing real.

## The headline

Swamp and Dark are converging on the same insight from different starting points. Swamp arrived by **instrumenting an existing agent pipeline**; Dark is designing the substrate **from scratch** around cohabitation.

The two ideas Dark should steal hardest:

1. **Workflows as first-class artifacts.** Dark has the actor; not yet the recipe.
2. **Tag-and-query as the default data surface.** Dark records everything; queries little.

Both fit cleanly into the existing substrate vocabulary; both shorten the path from *"agent works most of the time"* to *"agent's work is observable, reviewable, and sharable."*

---

**Sources**

- https://swamp.club/
- https://github.com/systeminit/swamp (README)
- `pdd-thinking/COHABITATION.md`
- `pdd-thinking/CAPABILITIES.md`
- `pdd-thinking/IDENTITY.md`
- `pdd-thinking/EVENT-STREAMS-AND-PARKING.md` (referenced)
- `pdd-thinking/VIEW-SKETCHES.md` (referenced)
