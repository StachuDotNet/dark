# Package system layers — Apps and projections over one op stream

Read alongside [distributed-event-sourcing.md](distributed-event-sourcing.md)
(the keystone: ops, projections, the thin `App`) and
[event-bus.md](event-bus.md) (the substrate that carries and replays ops). This
doc is the package-system facet of that one frame. The old version of this doc
catalogued "everything beyond raw content" as a fixed stack of seven layers with
a shared table shape. That framing is retired. The territory it mapped —
descriptions, deprecation, purity, tests, traces, dependencies, trust — is still
the right territory. What changes is the model underneath it.

## The reframe: there is no stack of layers

The old doc's mistake was to treat "content," "annotations," "derived,"
"relationships," and the rest as **structural tiers** of the package system,
each with its own canonical table and a privileged place in the architecture.
That is the wrong altitude. Under the ops/projections lens there is exactly one
durable thing — **a timestamped stream of ops** — and everything the old doc
called a "layer" is either:

- an **op kind** that some App appends to that stream (a deprecation, a rename,
  a comment, a body edit), or
- a **projection** that folds the stream into something you can look at (the
  dependency graph, the deprecation set for a branch, the search index, the
  "what tests this?" reverse index), or
- an **App** — a small `App<'state,'op>` (see the keystone) that bundles an op
  type, its conflict/resolve rules, and the projections it renders.

So the question "which layer does docstrings live in?" dissolves. Docstrings are
an op kind (`SetDescription`) and one or more projections (the rendered doc, a
search index). Deprecation is an op kind and a projection. Dependencies are *only*
a projection. None of them is more foundational than another; they are all the
same two primitives — ops and projections — wearing different clothes.

This is the same move [distributed-event-sourcing.md](distributed-event-sourcing.md)
makes for the system as a whole. The package system is not special. It is one big
App (or a composition of small ones) over the shared op stream, exactly like the
SCM, the CLI, and crons.

## Content is the first projection, not the bottom layer

In the old model, `package_functions/types/values` were "layer 1 — content,
global, hash-keyed, immutable," and everything else sat on top. Re-read it: the
content-addressed store is **a projection** too. The durable thing is the op
stream of `AddFunction body`, `SetName loc hash`, `SetDescription`, and so on.
The hash-keyed content table is what you get by folding the body-defining ops; it
is regenerable from the stream and shippable over sync precisely *because* it is
a deterministic fold.

The old doc's "end state" — that the content hash becomes a true content hash,
same bits = same hash = same behavior, with descriptions/deprecation/purity
pulled out — still holds, and is even cleaner here. The body-defining ops produce
the content projection; the description ops produce the description projection;
they no longer fight over one hash because they were never one table to begin
with. "Get the docstring out of the hash" stops being a migration and becomes the
default: a description is just a different op against the same item.

## Each thing gets its own projection considerations — no shared table shape

The old doc proposed a single `CREATE TABLE <annotation>` template that every
annotation (deprecation, description, stability, purity, approvals, tags, …) would
instantiate, plus a shared `visible_at_branch` SQL helper. **Drop this entirely.**
The shape-sharing was a false economy. It forced genuinely different data into one
mold and then had to claw back per-concern columns, blob payloads, and per-concern
semantics tables (latest-wins vs append vs threaded) anyway — at which point the
"shared shape" earned nothing but a misleading sense of uniformity.

The honest model:

- **Each thing decides its own projection.** A deprecation projection wants
  latest-wins-per-item. A comment projection wants an append-only threaded tree.
  A dependency projection wants a doubly-indexed edge set for forward and reverse
  lookup. A perf projection wants time-windowed aggregates with a `computed_at`
  staleness marker. These are not variations on one schema; they are different
  data structures that happen to fold the same op stream. Let each be what it is.

- **A consumer that uses several things must know all their shapes.** This is the
  replacement for the shared-shape promise. If a view (or an agent, or a runner)
  reads deprecation *and* purity *and* dependencies to make a decision, it depends
  on all three projections' types and shapes — and that is fine and expected.
  There is no uniform `annotation_blob` to paper over the differences, and we do
  not want one. The coupling is real; name it rather than hide it. The
  cross-cutting reader is itself just another projection (or App) that subscribes
  to the op kinds it cares about.

- **No SQL in this doc.** Whether a projection lives in SQLite, an in-memory
  index, or a regenerated cache is an implementation detail of that projection,
  decided when it is built. The op stream is the contract; the projection's
  storage is private to it. (The keystone's rule of thumb: if losing it costs only
  CPU to rebuild, it is a projection — and almost all of this is.)

## Ops travel through an instance even when the consumer is inactive

A load-bearing consequence of "the op stream is the durable, synced thing": an
instance relays and stores ops for Apps and extensions it is **not itself
running**. If instance B has the deprecation App active but not the perf-tracking
extension, B still receives, stores, and forwards the perf-related ops it syncs
from A. They sit in B's op stream as inert, well-formed data. The moment someone
activates the perf extension on B — or syncs the stream onward to instance C,
which does run it — the projection materializes from ops that were there all
along.

This is why extensions can be **opt-in without being opt-in-to-receive**.
Participation in a feature is a local choice about which projections you
materialize and which runners you activate, *not* a gate on which ops cross your
boundary. The wire carries the full op stream (subject to
[sync.md](../stable-and-syncing/sync.md) visibility/trust policy); each instance projects the subset it
cares about. An extension that lands later still sees the complete history,
because the ops were never filtered out for lack of a local consumer.

This also means an App can be authored, shipped, and adopted incrementally across
a fleet: the ops it defines start flowing before every instance knows how to
render them, and no instance has to be upgraded in lockstep.

## Package dependencies are one projection — nothing more

The old doc gave `package_dependencies` a privileged spot as the prototype of a
whole "relationships" tier. Demote it. **Dependencies are one projection of the
body-defining ops** — you get the dependency graph by folding "what does this
body reference?" over the content projection. It is not a structure the author
maintains, not a tier, not foundational. It is exactly as derived as the search
index or the call-count rollup, and it earns no more architectural weight than
they do.

Stating this plainly matters because dependencies *feel* foundational —
everything points at them — and that feeling tempts you into building the rest of
the system around them. Resist it. If a different projection wants edges (a
`tests` reverse index, a `sample-for` lookup, a `see-also` browse graph), it is
its own projection folding its own ops, indexed however that consumer needs.
"Relationships" is not a layer; it is a description of *several independent
projections that happen to be edge-shaped.* Some edges are derived from bodies
(dependencies); some are declared by an op (`tests`, `sample-for`); the
derived-vs-declared distinction is just *which op stream the projection folds* —
the content ops or an author's declaration ops.

## "Harmful" and other warnings are an event-stream App

The old doc treated `Harmful` as a deprecation kind that the runtime checks
through bespoke `PackageManager` plumbing, a per-branch `ConcurrentDictionary`
cache, and an `allowHarmful` toggle on `ExecutionState`. Reframe the whole thing
as an **event stream** in the sense of [event-bus.md](event-bus.md).

"This function is harmful" is an op. Flagging, un-flagging, and changing the
reason are ops appended to a stream. From that stream you get:

- a **projection** — "the set of items currently flagged harmful on this branch"
  — which is what a viewer, a search filter, or an editor gutter renders; and
- an **event** on the bus when a flag is raised, which any subscriber can react
  to: a runner that refuses to invoke flagged code, an agent that routes around
  it, a notification that surfaces to the author, a budget/policy enforcer.

Crucially this is **opt-in by subscription, not by special-casing**. A runtime
that wants to halt on harmful code subscribes to the harmful-flag bus (or reads
the harmful projection at call sites) and parks/refuses accordingly; a runtime
that does not care simply never subscribes. There is no `allowHarmful` flag baked
into core `ExecutionState` and no harmful-specific query bolted onto
`PackageManager` — there is a general bus, and "halt on harmful" is one
subscriber written in Dark. The same machinery serves stability warnings,
deprecation notices, caveats, and known-issue alerts: each is a stream of flag
ops plus whatever subscribers an instance chooses to run. Built-in or
extension is then a packaging choice, not an architectural one — the default CLI
App can ship the harmful-halt subscriber, and a user can fork or disable it like
any other App behavior.

Per the previous section, the harmful-flag ops reach an instance even if it runs
no subscriber for them — so turning on enforcement later is just activating a
subscriber over a stream that is already complete.

## The catalog, re-cast as ops + projections

The territory the old doc listed is still useful; here it is, re-described in the
new terms. Each row is "the op kind(s) an App appends" and "the projection(s) you
fold out." None is a layer.

| Thing | Ops appended | Projections folded out |
|---|---|---|
| Body / definition | `AddFunction`, `AddType`, `AddValue` | content-addressed store; the dependency graph |
| Names / bindings | `SetName loc hash`, `Unbind` | name→hash resolution per branch |
| Deprecation | `Deprecate item kind`, `Undeprecate` | latest-wins flagged set; an event on flag |
| Harmful / warnings | `Flag item reason`, `Unflag` | flagged set + bus event (subscriber-driven) |
| Descriptions / docs | `SetDescription item path text` | rendered docs; a search index |
| Stability | `SetStability item level` | per-item current level |
| Declared purity / effects | `DeclareEffects item flags` | per-item effect set; conflict vs inferred |
| Inferred purity / perf / usage | (no author op — folded from runtime/trace streams) | aggregates with `computed_at`; staleness-aware |
| Tests / examples / samples | `DeclareTest x y`, `DeclareSample v fn` | reverse indexes ("what tests X?", "samples for X") |
| Saved / named traces | `PinTrace id`, `NameTrace id label` | example-of edges into the trace subsystem |
| Comments / commentary | `Comment item body`, `Reply`, `Resolve` | threaded tree per item; long-form docs as items |
| Approvals / provenance | `Approve item by`, `SetProvenance item src` | append log; cross-boundary attestations (sync) |
| Trust / signatures / visibility | sync-policy ops + signature ops | what crosses a boundary (see [sync.md](../stable-and-syncing/sync.md)) |

Notes that survive the reframe:

- **Declared vs inferred** is no longer two tiers ("annotations" vs "derived").
  It is two **op sources** feeding two projections that a consumer may read
  together: an author's `DeclareEffects` op stream and a background analysis op
  stream. When they disagree, that disagreement is itself a projection — a
  conflict surfaced as data, exactly as [conflicts.md](../stable-and-syncing/conflicts-and-resolutions.md) and the
  keystone's "most conflicts are OK" stance prescribe — not an overwrite and not a
  thing the storage layer adjudicates.

- **Traces** still are not content. A saved trace is a `PinTrace` op plus an
  example-of edge projection pointing into the trace subsystem; the high-volume
  blobs live in that subsystem with their own retention, and the package side
  holds only the small authored-intent edge. Same split as before, restated as
  ops + projection.

- **Inferred/derived facts** carry `computed_at` and staleness because they fold a
  high-churn op source (runtime traces, analysis runs); author-declared facts do
  not, because their op stream only moves on human action. This is a property of
  *which stream a projection folds*, not of a tier the data was filed under.

## Why this is strictly less machinery

- **One durable thing** (the op stream) instead of seven-plus canonical tables
  with cross-cutting visibility logic. Projections are regenerable caches; lose
  one and re-fold.
- **No shared-shape coupling** to maintain or escape from. Each projection is
  shaped for its own reads; cross-cutting consumers depend on the specific shapes
  they read, openly.
- **Sync is free.** The package system rides the same op stream + projection split
  as everything else, so syncing deprecations, descriptions, and dependencies is
  not new plumbing — it is [sync.md](../stable-and-syncing/sync.md) replicating ops, with each instance
  projecting what it runs.
- **Runtime checks are subscribers, not core toggles.** Harmful-halt, effect
  gating, and the like are Dark-side subscribers over buses, removing the bespoke
  `PackageManager`/`ExecutionState` plumbing the old doc had to design per
  runtime-visible annotation.
- **Extensions adopt incrementally** because ops flow through instances that do
  not yet run the consumer.

## What still needs a design round

- **Which Apps to carve.** Is "deprecation + harmful + stability" one
  flag-oriented App, or three? Is "comments + long-form commentary" one App? The
  carving is a composition choice; settle it when the first real package App is
  built, per the keystone's "decide when carrying vs blind" open question.
- **Projection storage boundaries.** The keystone leaves open how cleanly to split
  the canonical op DB from regenerable projection stores. The package projections
  (content store, dep graph, search index) are the concrete stress test for that
  split — they are the bulk of "regenerable from ops."
- **Conflict shapes per op kind.** Latest-wins (deprecation), append (comments),
  edge-set (dependencies) each imply different `conflict`/`resolve` rules on their
  App. The old "semantics differ per annotation" table was right about the
  *differences*; it now lives as each App's `conflict`/`resolve` members rather
  than as a column in a shared table.
- **Trust and visibility** stay a [sync.md](../stable-and-syncing/sync.md) concern, not a package
  concern: which ops cross a boundary and who signed them is policy over the
  stream, layered the same way the keystone layers everything else.

## Related docs

- [distributed-event-sourcing.md](distributed-event-sourcing.md) — the keystone;
  the thin `App`, ops vs projections, the four primitives this doc specializes.
- [event-bus.md](event-bus.md) — the substrate; how harmful-flags and other
  warnings become subscriber-driven streams rather than core toggles.
- [conflicts.md](../stable-and-syncing/conflicts-and-resolutions.md) — how each package App's `conflict`/`resolve`
  reconciles concurrent ops (rename races, declared-vs-inferred disagreements).
- [sync.md](../stable-and-syncing/sync.md) — how the op stream crosses machines and what trust/visibility
  policy gates it.
