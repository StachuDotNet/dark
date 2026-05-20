# Graph Projection — Internal Store as a Graph

A response to: *"sometimes I think our internal data store (at
least one projection of it) should be represented as a graph, in
some gradually-typed managed distributed graph DB."*

My honest take + how it threads through the substrate work just
sketched.

## TL;DR

**The idea is strongly aligned.** Most of what we just sketched
(conflicts, sync, capabilities, events, materialization) has a
natural graph formulation. The system is already graph-shaped;
the SQLite tables are flat projections of relations that
*fundamentally* form a graph.

**"At least one projection" is exactly the right framing.** Don't
make it the store. Make it a *view* over the ops-as-truth model
that LibMatter already has. Use a graph DB for what graph DBs are
good at (reachability, pattern matching, traversal); keep
relational + content-addressed for what those are good at
(immutability, transactions, simple persistence).

**"Gradually-typed" maps directly onto our PDD framing.** A node
starts as an idea, accretes properties (sig, body, tests, hash)
over time. Same shape as `CLAIMS.md` §1's "source often starts as
lazy."

**"Managed distributed" is more aspirational.** Solves the sync
problem cleanly. Adds operational complexity. Worth it
eventually; not v1.

**Trap to avoid:** graph DBs are an attractive nuisance. Many
teams reach for one and discover they were just doing joins.
Validate the queries before committing.

## What's already graph-shaped

Almost everything we've sketched:

| Node kinds | Edge kinds (sample) |
|---|---|
| Functions | calls, returns, depends-on-type, uses-capability |
| Types | contains-field-of-type, extends, parameterized-by |
| Values | typed-as, defined-by-fn |
| Locations | points-to-content-hash, in-branch |
| Hashes | embodies-content, parent-of |
| Patches | contains-op |
| Ops | targets-location, produces-content |
| Branches | contains-patch, derived-from |
| Traces | references-fn, references-value, contains-event |
| Events | produced-by, observed-by |
| Conflicts | between-X-and-Y, resolved-by-Z |
| Prompts | grounds-against-type, refines-into-fn |
| Pendings | name-of, materialized-into |

Half of those edges are already in the SQLite schema as foreign
keys; the other half live implicitly (in code that walks one
table to find related rows in another). Making them *explicit*
in a graph view doesn't change semantics — it changes
accessibility.

## What the gradually-typed framing adds

A graph DB that supports gradual typing lets nodes start with
*loose* shape and accrete required properties over time. Matches
PDD's lifecycle exactly:

```
Pending node
  required: { name }
  optional: { sig, body, tests, hash, description, ... }

→ as it accretes properties, the type narrows
→ when it has { name, sig, body, hash }, it's a Package(hash)
→ when it has { name, sig, body, tests, description }, it's "done"
```

The "doneness tracking" idea from FRONTIER becomes free: a node's
doneness is "what fraction of the expected properties are
filled." No special table; the graph schema *is* the doneness
schema.

Similarly for types: a type starts as "this name exists,
somewhere"; eventually has fields, then a default value, then
codecs, then docs. Same gradual-typing pattern.

This is also how the system absorbs partial knowledge — an LLM
that materializes a body but not tests, a user who writes tests
but not docs, a sync that ships an incomplete patch. All
representable.

## What "managed distributed" adds

Two real wins:

1. **Cross-instance sync becomes graph diff.** What changed
   between my graph and the remote? Standard problem; most
   graph DBs have it built in. Maps directly onto
   `SYNC-AND-STABILITY.md`'s event-sourced sync.
2. **Multi-tenant collaboration falls out.** Two users editing
   the same shared subgraph is just two writers on the same
   nodes; the graph DB's conflict-detection feeds into our
   conflict dispatch.

Cost: distributed graph DBs are more operationally complex than
SQLite-on-disk. Managed services help (Dgraph Cloud, TigerGraph,
TerminusDB Cloud, Neo4j Aura) but they're real services with
real bills.

Probably v2. For v1: ops-as-truth in SQLite (as LibMatter
specs), expose a graph view in-process, sync via the existing
event stream.

## How this lands against each substrate sketch

### vs CONFLICTS-AND-RESOLUTIONS

A conflict is a graph constraint violation: "two edges from one
location-node should not point to different content-nodes" is a
graph invariant. Detection becomes a query, not bespoke code.

Resolution becomes a graph mutation: "delete one edge, redirect
the other." Auditable as graph changes.

This makes the conflicts-as-shared-primitive story stronger —
the same query engine drives SCM conflict detection, PDD
unresolved-name detection, and capability-denial routing.

### vs SYNC-AND-STABILITY

Sync becomes incremental graph replication. The event stream is
the change-log; the graph is the materialized view. Bootstrap is
"clone the subgraph rooted at the public surface."

Removing `.dark` files: today's source is a flat textual graph
(modules contain fns contain expressions). The graph
representation is the post-parse form. If the canonical form is
the graph, the textual form is just one rendering.

### vs EVENT-STREAMS-AND-PARKING

Events are graph mutations. Subscriptions are graph queries with
change notification (most graph DBs have this; "subscribe to
nodes matching pattern P, notify when matches change"). The
event-stream substrate could be implemented on top of the
graph's native pub/sub.

But: this is also where I'd be careful. Event streams are
performance-sensitive (high-frequency materialization events,
trace recording). The graph DB needs to be the right tool for
this throughput. Some are; most aren't.

### vs CAPABILITIES

Effective caps for a fn is a graph reachability query: "walk the
call-graph reachable from this fn; union the cap-sets at each
node." Materialize as a cached node property; invalidate on graph
mutation.

Same for: dependency analysis, dead-code detection, refactor
impact analysis, "what depends on this hash if I change it."
All standard graph queries.

### vs HOT-RELOAD

`BodyChanged` is a graph mutation. Frames depending on the changed
node are graph-neighbors. Reload-fanout = walk the dependents
edge. Could be O(reads-of-the-fn) instead of "broadcast and let
everyone check."

### vs COMPOSABLE-MVU

The Model in a complex MVU app *is* a subgraph (user's selection,
focused fn, expanded panels, dive-in stack). Storing it in the
graph makes time-travel debugging cheaper — save graph snapshots
at each Msg.

### vs the PDD viewer (`VIEW-SKETCHES.md`)

The viewer is a graph traversal: start at the in-focus fn,
render outwards. Status, dive-in, sessions strip — all graph
queries. The viewer's MVU app becomes a thin layer over graph
subscriptions.

## A concrete proposal

**Ops are the source of truth.** No change to the LibMatter
model. Patches contain ops; ops mutate the canonical state. This
is content-addressable, durable, syncable.

**The graph is a derived view.** A background process (or
synchronously, per op) projects ops into a graph in-process.
Queries hit the graph view; mutations go through ops; the graph
follows.

```
        User edit / PDD materialization / Sync arrival
                          │
                          ▼
                      [Op produced]
                          │
                ┌─────────┴─────────┐
                ▼                   ▼
        [SQLite tables       [Graph projection
         — canonical]         — derived view]
                ▼                   ▼
      [LibMatter exec]        [Query engine]
                                    │
                                    ▼
                         [Viewer, agent, capability
                          analyzer, sync, ...]
```

The graph projection is rebuildable from ops (it's just a cache).
If we change schema, we replay ops to rebuild.

**Tech choices** (worth a deeper look, this is a placeholder):

- **TerminusDB** — content-addressed graphs, immutable, git-like
  model, gradual schema (via "schema documents"). Closest to
  our intuitions. Smaller community.
- **Dgraph** — distributed, GraphQL-native, mature. Less of a
  natural fit for content-addressing but proven at scale.
- **Neo4j** — most mature, big ecosystem, heavy. Not really
  "gradually typed" but flexible enough.
- **In-process / DuckDB-with-graph-extension / SQLite recursive
  CTEs** — start here. Many "graph queries" are 2-3 layer joins.
  Don't reach for a graph DB until the queries demand it.

My instinct: **start with SQLite + materialized-view tables for
the most-traversed paths**. Add a real graph DB only when query
shapes demand it.

## What's hard

- **Multiple projections.** Relational for SCM ops, document for
  traces, graph for cross-references. Each has its strength. The
  "graph projection" is *additional*, not replacement.
- **Authoritativeness.** Ops are authoritative; graph is derived.
  When in doubt, replay ops.
- **Gradual typing in graph DBs is rare.** Most are
  fully-untyped (RDF, Property Graph) or fully-typed (TypeQL,
  Dgraph schema). Gradual is something we'd build.
- **Distributed query consistency.** Cross-instance graph
  queries have to handle stale views. Often easier to query
  the local graph + the event stream of arriving changes than
  to make every query distributed.

## The trap

Graph DBs are an *attractive nuisance*. They feel right. They
suggest elegant solutions. They're often slower than just doing
the joins you needed.

The honest test: **what graph queries does the system actually
need that are hard in SQL today?**

Off the top of my head:

- Effective-caps reachability (3-5 hop walk; doable in recursive
  CTE but graph syntax is nicer)
- Dependency impact ("if I change this hash, what fns are
  affected?") — recursive CTE works fine
- Conflict-detection ("two locations pointing at different
  hashes") — single join in SQL
- Search-by-partial-type ("functions returning `Result<Csv,
  _>`") — graph pattern matching is genuinely nicer here
- Trace-replay-with-dependency-tracking — could go either way

So: maybe 2-3 genuinely-graph-shaped queries. That's not a
"replace the DB" mandate; it's "expose a graph view for those
queries."

## Honest take

The graph framing is **the right mental model**, but the
implementation should be lazy:

1. **Mental model**: design new substrate work as if the data is
   a graph. Use graph vocabulary in docs. Sketch queries in
   graph notation.
2. **Implementation v1**: SQLite + materialized views + recursive
   CTEs. Cheap. Familiar.
3. **Implementation v2**: in-process graph index over the SQLite
   ops-as-truth, exposed as a queryable API.
4. **Implementation v3** (when sync scale demands it): managed
   distributed graph DB as the canonical projection store;
   ops still ship as events.

This is "graph-shaped thinking on a relational foundation, with
escape hatches to a real graph DB when warranted." Avoids the
attractive-nuisance trap; preserves the win.

The 2025-11-12 ux-thinking note already does most of this — it's
event-sourced, content-addressable, with append-only ops. The
graph is *implicit* in those ops; making it *explicit as a view*
is a modest extension.

## How this shapes the work ahead

Concretely, going forward, I'd:

- Add a section to `SYNC-AND-STABILITY.md` (or here) framing the
  ops-as-edges-and-nodes equivalence
- When sketching the event stream substrate, note that
  subscribers are essentially graph-pattern subscribers
- When implementing conflicts, frame detection as a graph
  invariant
- When building the viewer, design it as a graph traversal UI
  from the start — even if implemented on SQLite for v1
- Track "what queries are we actually doing?" — if 80% of them
  are recursive CTEs in disguise, drop in a graph DB; if not,
  stay relational

## Open questions

- **Is the canonical "thing" a node or an edge?** Probably nodes
  (locations, fns, types, ops) with edges as relationships. But
  some things are arguably edges (a *call* is an edge between
  caller and callee). Worth pinning.
- **How does the graph handle WIP vs committed?** Probably:
  WIP nodes exist in the graph; their "committed" property is
  false; sync filters by it. Branches are subgraphs.
- **Time?** Does the graph have a notion of "the graph as of
  ts/op-N"? Time-travel queries are valuable; bitemporal graphs
  are real.
- **Schema evolution.** When a type grows a new property, do
  existing nodes get the property as null? Migration policy
  matters.
- **What's a Trace, as a graph?** Probably a path through the
  graph annotated with values. Replay = re-walk the path
  applying values.

---

## One-line summary

The graph framing is right and load-bearing for thinking; build
it as a view over the ops-as-truth model first, escalate to a
managed distributed graph DB only when query patterns demand it.
