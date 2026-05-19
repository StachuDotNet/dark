# PDD — Frontier

The unbuilt + speculative items, framed as design problems for after
the spike. Read with `ALGORITHM.md` (the high-level dispatch sketch)
and `CLAIMS.md` (what we're trying to prove). This is the "what to
think about next" doc.

Two big framings up front:

- **The real point isn't just fns.** It's recursive live
  development, powered by and integrated with AI, notes, types,
  values, traces, capabilities. We want to generate everything we
  need as we need it, integrate it into a holistic experience
  full-stack in/for Darklang.
- **Implementation lives in Dark.** The materializer + orchestration
  + UI should be Dark code on top of a small, principled F#
  substrate. The substrate provides the primitives (events,
  capabilities, conflicts). Dark provides the policy.

---

## Pre-PDD: capabilities-first

Builtin permissions / capabilities must land **before** real PDD
work. LLM-generated code will try `File.delete "*"` or
`HttpClient.post` to random places. Cap tags gate this.

- Each `BuiltInFn` gains `capabilities : Set<Capability>`
- Cap decisions live on `ExecutionState.capabilityCheck` —
  `Granted | Denied of reason | DeniedAsk`
- Cap grant is interactive (install-time / per-invocation / `--ask`)
- The generate-prompt only lists builtins the session has granted —
  belt-and-suspenders with the runtime gate
- PDD hooks into the cap-request flow throughout: as the agent
  decides to materialize something that needs new caps, it pauses
  and surfaces a grant request

Once this lands, PDD layers on top of it cleanly.

---

## Low-level substrate (what F# should grow)

### Conflicts + resolutions, as a base concept

Today an unresolved call mostly fails. We need a richer system:

- Per-call/per-RTE policy: substitute default, park-and-wait, ask
  human, retry with new strategy, fail loudly
- `RecoveryPolicy` is the right *shape* but needs to be a
  first-class LibExecution concept, not a side door
- Resolutions are pluggable — Dark-level code installs handlers
- Same machinery serves PDD (unresolved name) **and** SCM (merge
  conflict, mismatch between WIP and committed) — both are
  "something is unresolved, decide what to do"

The conflicts+resolutions system should thread through all of
LibExecution as a low-level primitive, used by both PDD and SCM.

### Event streams (and graphs)

`EventSink` today is a single `PDDEvent -> unit` mutable global.
That's a starting shape; the real thing wants to be:

- Multiple streams (materialization, capability, SCM, user-input,
  trace-replay, ...)
- Subscribers register interest by kind + filter
- Events can compose into **graphs**: waiters, joins, fan-out
- Both the agent infra and SCM sync sit on top
- 404 events from "unresolved/unfound name was about to be
  evaluated" plug in directly — some higher-level listener
  catches them, decides whether to materialize, park, or fail

Event-graphs-with-waiters might be a thing. Composable MVU-style.

### Tracing: less surface, more primitive

`Tracing.fs` got changed quite wildly during the spike. The
right move is **fewer F# changes, more exposure via builtins**.
Reduce the F# surface area; expose what Dark needs to read/write
traces from inside Dark code. Treat traces like values: they live
in the DB, they're queryable, they can be replayed.

### Storage: kill the JSONL sidecar

Everything currently in `rundir/pdd-cache/*.jsonl` and friends
should live in the SQLite DB. Either raw tables, or — more likely —
a `UserDB`-like construct (a new persistence primitive we'd build
out). One source of truth. No sidecars.

### Composable MVU apps infra

This system should sit on some composable MVU apps infrastructure.
We have relevant notes elsewhere on this machine. The PDD viewer,
the trace inspector, the SCM UI — these are all MVU apps. The
infra is the shared primitive.

---

## Low-level substrate: what F# should *stop* knowing

- **LibExecution shouldn't know about PDD.** All PDD logic moves
  to Dark. The F# substrate provides primitives (events,
  conflicts, capabilities, parking) that *any* agent infrastructure
  can use — PDD is one consumer.
- **Materializer in Dark.** `Stdlib.PDD.materialize` (or whatever
  it's called) is a Dark function. Users can refine it. The F#
  side is just the dispatch hook + the lookup cache.
- **Prompts are a pinned Dark type.** Not F# strings. A
  first-class `Prompt` type, special-cased so it's queryable and
  refinable.
- **HTML view served by Dark.** The current F# HTML view becomes
  a Dark HTTP handler — `dark prompt` starts a daemon that *is*
  the viewer. Beautiful, minimal, user-customizable.
- **Dark interpreter in Dark.** Long-term: a default Dark
  interpreter written in Dark itself (fails on missing) and a
  fancy expanding one (this one — materializes on demand).
  Bootstrapping is hard; worth thinking through "from first
  principles."

---

## The recursive live-development experience

The user prompts for some software. A daemon spawns to build it,
returns a thread id. The user attaches to a viewer.

In the viewer they see the **highest-level function in focus** —
the one they prompted for — with parts of it being filled in:
which sub-fns are resolved, which are materializing, which are
parked. They can dive into any of it:

- Which AI threads are running, on what
- What traces have run
- What tests are being added
- What code is being materialized
- What capabilities are being requested

Beautiful sketches needed at multiple zoom levels. We don't need
anything real yet — high-level visual designs of what this looks
like across time.

**Re-eval until results feel good.** Keep faking implementations
and "continuing" traces (or starting fresh) as needed. The user
isn't waiting on a build; they're steering a process.

**Each eval is separately debuggable as it goes.** Traces can be
replayed and debugged like a movie scrub. This is a real
differentiator from "wait for codegen, run, iterate."

### Coordinator

The coordinator is super-core (see ALGORITHM.md). It's the
single dispatch point that decides which strategy to run, in what
order, and how to combine results. Sketches needed.

### CSV example: smarter defaults

Our CSV example (`longestRow`, `parseRows`) currently passes the
csv string through several pendings. After the prompt, the system
should likely *know implicitly* to extract the csv as a value or
file early on — bind it to a name, type it as `Csv` or
`List<List<String>>`, and operate from there. The agent's
"feature engineering" should be part of the materialization
pipeline.

---

## Done-ness tracking

We should track the "done-ness" of a fn — but differently than
we have been.

At first a fn is just **an idea, with a name**. Eventually it
acquires:

- a signature
- a body
- tests
- connected code (callers, callees, integration)
- a description

We iterate on all of it until it feels good. "Done-ness" is a
gradient across these dimensions, not a binary flag.

This isn't `PackageID` vs `Package(hash)` exactly — it's more
nuanced. It's also worth asking whether we need a new ID concept
at all: maybe WIP just refers to other things **by location**,
and when WIP gets committed we update those refs to be by hash for
long-term stability. Simpler.

---

## WIP and SCM

Today: WIP lives in `promoted.jsonl`; committed bodies in
`promoted_hashes.jsonl`. Both are sidecars.

The bigger question: how does WIP fit into branch ops and package
ops?

**Open tension on syncing:**

- (a) **WIP doesn't get synced.** Cleaner — keeps WIP local,
  sidesteps a lot of op-semantics questions, *as long as* WIP is
  stored separately from ops.
- (b) **WIP needs some sync story.** What if you want to share
  your WIP with yourself on another machine, or with a coworker?
  Then either:
  - Lightweight: gist-like snapshots, no full op semantics
  - Heavyweight: WIP gets full op treatment, sync just like
    committed package items
- We haven't picked (a) vs (b). The answer shapes a lot of
  downstream design.

**Whenever WIP becomes real/committed**, references to it should
update to be by hash, so they're stable long-term.

We should separate WIP from committed cleanly — but it's not just
a flag on a row; it's about which ops apply, which sync, which
show up in `dark search`, etc.

---

## Speed

Two operations must be **very** fast:

- **Searching for dark matter** (existing functions, types,
  values, prompts). Sub-100ms ideally.
- **Drafting v0 of any code.** From prompt-typed to executable
  sketch in under a second.

Neither is fast enough today. We need benchmarks targeting these
two paths specifically; they're the load-bearing UX moments.

---

## Prompts as a pinned type

Prompts shouldn't be raw F# strings or even raw Dark strings.
They should be a **first-class pinned type** in the language —
`Prompt` — with structure:

- The prompt text itself
- Metadata (model, params)
- Linked code/types it grounds against
- Version + provenance
- Usable in dark code: `let p : Prompt = ...`

Pinned types are special: the language recognizes them, the SCM
treats them as first-class objects, queries can target them.

---

## Search-by-type (and other agent helpers)

Supporting the agent likely needs a richer query surface:

- Search values by type
- Find package items matching a partial signature
- Find traces that touched a given function
- Find functions called by X
- Find functions that satisfy a predicate

Sketches needed for the surrounding stuff. Whatever the agent
needs at runtime, it needs at sub-100ms.

---

## Refactors as a language primitive

The agent will need to do refactors: rename, move, extract, inline,
change signature. These should be **built into the language**, not
hand-coded each time. A refactor is an op, like SCM ops are. The
agent invokes refactors as part of normal materialization. The
human approves or refines.

---

## darklang.com/gradual

A public-facing page that explains the vision. Sketch the content
somewhere — possibly in a vault note. Should communicate:

- The lazy/gradual source model
- The recursive coding-agent
- Live-development with AI as collaborator
- How it differs from Copilot, Cursor, agentic coders

---

## Hot reloading — from first principles

The spike has hot-reload via `pddRefreshHook` + mtime polling on
JSONL files. Cute but not principled. We need to think this
through:

- What changes trigger reload?
- What's the granularity (per-fn, per-module, per-DB)?
- How does it interact with parked frames mid-execution?
- How does it relate to SCM branch ops?
- Does the viewer get hot-reload too?
- What's the contract: bodies updated atomically? Tests re-run?

Add a tight `HOT-RELOAD.md` later, or just an expanded section
here. For now: open.

---

## Risks to watch

(Carried forward from the deleted EMPIRICAL.md red-team.)

- **Traces aren't actually replayable in practice.** If too much
  context isn't captured (LLM nondeterminism, network, time),
  replays diverge and the "trace as artifact" framing breaks.
  Test: take a trace, replay 10×, count matches. <9/10 → re-think.
- **Tolerance hides logic bugs.** Loose-mode-by-default lets
  divide-by-zero return 0 and your program looks fine but is
  silently wrong. Mitigation: run tests in strict periodically;
  don't let `EmptyBody` cover non-materialization errors.
- **Sig consensus thrashes.** If LLM-generated sigs disagree with
  find-result sigs ≥30%, first-wins floods the trace with noise.
  Need constraint-driven consensus.
- **The system feels wrong even though it works.** PDD ships
  end-to-end, you use it on a real task, it works — but
  uncomfortable, unpredictable, hard to debug. Most valuable
  failure mode. Notice, write up *why*, pivot.
- **LLM returns invalid Dark.** Already seen for string ops.
  Mitigation: retry-with-AST-feedback. 2× cost, halves failure
  rate.
- **`defaultFor` is wrong for custom types.** Records need
  field-by-field defaulting. Today returns DUnit for
  `TCustomType`. Fix when it hurts.

---

## What's deliberately out of scope (until the foundation exists)

- Multi-user cloud cache (auth/billing/trust — defer until
  single-user value proven)
- PDD-LSP IDE integration (wait for Dark LSP to land)
- Speculative materialization on keypress (wasteful tokens —
  experiment, don't productize)
- Fine-tuned per-user models (real value but not a Darklang-team
  capability — partner with inference vendor)
- Mid-program fn iteration when downstream constraint fails
  (important but design space is murky — own spike)
