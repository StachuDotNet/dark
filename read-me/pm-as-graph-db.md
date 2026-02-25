# The PM as a Graph DB: Analysis

## What It Already Is

The PM is an event-sourced, branch-aware, content-addressed dependency graph. Stripped to its graph-DB essentials:

**Nodes** (3 kinds, stored in projection tables):
- `PackageType` — type declarations (records, enums, aliases)
- `PackageFn` — functions (AST body + signature)
- `PackageValue` — top-level values (AST body)

Each node is identified by a UUID. Content is immutable — "updating" a node creates a new UUID.

**Edges** (1 kind, stored in `package_dependencies`):
- `item_id → depends_on_id` — extracted from ASTs by `DependencyExtractor.fs`

All edge types (fn-calls-fn, type-references-type, fn-uses-type-in-signature, deprecation-chains) are collapsed into the same bare UUID relationship. The *kind* of reference is lost.

**Naming layer** (the `locations` table):
- Bidirectional mapping: `PackageLocation ↔ UUID`, scoped by branch
- A node can have multiple names over time; a name can point to different nodes across branches

**Traversal operations already implemented:**
- Forward: "what does X depend on?" (`getDependencies`)
- Reverse: "what depends on X?" (`getDependents`)
- Transitive reverse: "what transitively depends on X?" (`discoverDependents` — BFS with cycle detection)
- Batch reverse: `getDependentsBatch` — chunked for SQLite limits

**Graph mutations are event-sourced** via `PackageOp`. Everything else — projection tables, dependency edges, locations — is derived from replaying ops.

## What a "Real" Graph DB Would Add

Graph databases (Neo4j, Dgraph, etc.) provide things the PM doesn't have:

1. **General query language** (Cypher, SPARQL, Gremlin) — path queries, pattern matching, subgraph extraction, aggregation
2. **Typed/labeled edges** — "calls", "uses-type", "deprecated-by" rather than a single "depends-on"
3. **Path queries** — shortest path from A to B, all paths of length N
4. **Pattern matching** — "find all functions that call X and also reference type Y"
5. **Aggregation** — most-depended-on nodes, dependency depth distribution, cluster detection

## Where There's Genuine Value

### 1. Richer edge types (HIGH value, moderate effort)

`DependencyExtractor.fs` line 11–16 already has a TODO for this:

```fsharp
// TODO: track the _type_ of the thing we're dependant on.
// Likely `type PackageItem = | Type of ID | Value of ID | Fn of ID`
// and `type Dependency = | PackageItem of PackageItem`.
```

Currently every reference — function call, type annotation, enum construction, deprecation pointer — becomes the same `uuid`. Enriching this unlocks questions the system can't currently answer:

- "What functions *call* this function?" (vs "what functions *mention* this function's type in a signature?")
- "What types *structurally contain* this type?" (vs "what functions *return* this type?")
- "What's the deprecation chain for this name?" (follow `RenamedTo`/`ReplacedBy` edges specifically)

**Concrete change:** Extend `package_dependencies` with an `edge_type` column (`'calls_fn' | 'uses_type_in_body' | 'uses_type_in_sig' | 'deprecated_by' | ...`). `DependencyExtractor` already walks the AST with enough context to classify edges — it just throws the classification away.

This costs a schema migration + extending the extractor output type. The propagation system doesn't need to change (it only cares about existence of edges, not kinds).

### 2. Exposing graph queries as Dark builtins (HIGH value for tooling)

The PM already has `getDependents`/`getDependencies` wired through to builtins (`pmGetDependents` etc.), but there's no way to ask:

- "Show me the dependency tree of X, N levels deep" (transitive but bounded)
- "What's the shortest path from A to B?" (does A transitively depend on B, and how?)
- "What are the leaf nodes?" (items with no dependencies — primitive types, leaf functions)
- "What are the root nodes?" (items nothing depends on — likely top-level entry points)
- "What's the full subgraph reachable from X?" (for extraction/packaging)

These are all implementable with recursive CTEs in SQLite. No new storage needed — just new query functions exposed as builtins, callable from CLI/IDE tooling written in Dark.

### 3. The temporal/versioned dimension (MEDIUM value, unique to your system)

Most graph DBs don't have branches. The PM does. This means the dependency graph is not a single static graph — it's a *family* of graphs indexed by branch, where each branch sees a different projection of names→UUIDs.

Leaning into this unlocks:
- "How did the dependency graph of module X change between branch A and branch B?"
- "What new dependencies were introduced on this branch?"
- "What items on this branch have diverged from main?" (i.e., have different UUIDs at the same locations)

This is essentially a temporal property graph — and it's something traditional graph DBs don't do well.

### 4. Impact analysis UI (MEDIUM value)

Before propagation runs, `discoverDependents` already finds everything that will be affected. Exposing this as a preview — "changing `Stdlib.List.map` will cascade to 347 items across 12 modules" — gives users actionable information. If you had richer edge types, you could even say "47 direct callers, 300 transitive".

### 5. Garbage collection / integrity queries (LOW-MEDIUM value)

- Find unreachable nodes: UUIDs in content tables with no active location entry and no dependents
- Find broken references: edges pointing to UUIDs that don't exist in any content table
- Find orphaned locations: location entries pointing to nonexistent content

These are graph-integrity queries. Simple SQL joins, but framing them as graph operations makes the intent clearer.

## What's Probably NOT Worth It

### Switching to an actual graph DB engine

SQLite is doing the job. The graph has 3 node types and (currently) 1 edge type. The most complex operation is a BFS that's already fast enough. Adding Neo4j/Dgraph would mean:
- A second data store to keep in sync with the event-sourced ops
- Operational complexity (especially for the "same binary runs as client and server" plan)
- Breaking the beautiful "one `.db` file" portability
- Solving a problem you don't have — query performance isn't the bottleneck

SQLite's recursive CTEs can express any graph query you'd realistically need at the current scale. If scale becomes a problem later, you can always add a graph index as another projection table.

### Making ProgramTypes.fs explicitly graph-shaped

Refactoring the core types to look like `Node`/`Edge`/`Graph` abstractions would obscure the domain. `PackageFn` with a `body : Expr` is clearer than `Node<FnPayload>` with `edges : List<Edge>`. The graph structure is implicit in the AST and extracted when needed — that's the right level of abstraction.

### Building a custom query language

Domain-specific functions (`getDependencyTree depth item`, `getImpactRadius item`, `findPath from to`) are better than a general-purpose graph query language. You'd spend forever on the query language and users would still just want the 5 common operations.

## The Interesting Framing

The PM isn't accidentally a graph DB — it's something more specific and arguably more interesting. It's an **AST-level dependency graph with event-sourced history, branch-scoped naming, and automatic cascade propagation**.

The closest analogy isn't Neo4j — it's closer to:
- **Unison's codebase** (content-addressed code with a naming layer on top), plus
- **Git's branching model** (branch chains, commits, merge/rebase), plus
- **A reactive system** (propagation = cascade triggers when a node changes)

The graph-DB framing is useful for thinking about what *queries* to support, but the architecture shouldn't change to look more like a generic graph DB. Instead, the value is in:

1. **Enriching the existing dependency extraction** (typed edges — the TODO that's already there)
2. **Building domain-specific graph queries** as Dark builtins (tree views, impact analysis, path finding)
3. **Leveraging the temporal dimension** that branches give you (diff graphs across branches)

None of these require new infrastructure — they're all extensions to what exists, using SQLite and the current architecture.

## Concrete Next Steps (If Pursuing)

Ranked by value/effort:

1. **Extend `DependencyExtractor` to classify edge types** — schema migration for `package_dependencies.edge_type`, update extractor to return `(uuid, EdgeType)` instead of `uuid`. Moderate effort, high payoff for all downstream tooling.

2. **Add `pmGetDependencyTree` builtin** — bounded transitive query, returns a tree structure. Small effort (recursive CTE + new builtin), immediately useful in CLI/IDE.

3. **Add `pmGetImpactPreview` builtin** — wraps `discoverDependents` as a read-only preview before propagation. Small effort, high UX value.

4. **Add branch-diff graph query** — "what items have different UUIDs on branch A vs branch B at the same locations?" Useful for code review, merge preview. Moderate effort.

5. **Visualization** — expose the graph in a format (DOT, JSON) that can be rendered. The existing `search` API already returns located items — extending it with edges gives you a renderable subgraph.
