# Package System Layers — beyond content

## Why this doc exists

The package system keeps sliding into adjacent questions: what about
docstrings, purity, comments, tests, approvals, relationships, saved
traces, perf, trust, sync, AI provenance? Not all of it is "annotation"
— some is graph edges, some is derived-from-observations, some is
content that happens to be commentary, some is cross-cutting policy over
what crosses the sync boundary. Cataloguing the territory up front keeps
each follow-up from quietly becoming a general-metadata rewrite, and
gives us an ordered map.

Deprecation recently landed as the first inhabitant of the Annotations
layer, which paved the way for the patterns documented below — shared
table shape, hash-keyed semantics, branch-chain visibility, runtime-
query plumbing. Descriptions, stability, purity, and the rest follow the
same template.


---

## The layered model

Seven categories once you spread out everything beyond raw content, plus
two cross-cutting adjacencies.

**In-system layers:**

1. **Content** — global, hash-keyed, immutable. `package_functions/types/values`. Narrows toward pure structural definition over time.
2. **Bindings** — branch-scoped, layered. `locations`. Names → hashes.
3. **Annotations** — branch-scoped, layered, hash-keyed. *Author-declared* facts/opinions about an item. `deprecations` is the first inhabitant.
4. **Derived / computed** — item-keyed, aggregated from content analysis or from ephemera. *Not* author-declared. Things like perf characterizations, inferred purity, usage signals.
5. **Relationships** — branch-scoped, typed edges between items. Current: extracted `package_dependencies`. Future: declared edges (`tests`, `example-of`, `sample-for`, `see-also`, `implements`).
6. **Commentary** — spans content + annotations. Per-item threaded comments in annotations; long-form named commentary (RFCs, design notes, tutorials) as first-class package items.
7. **Ephemera** — raw, high-volume, time-series. Runtime traces, call counts, error rates, benchmark runs. Probably a separate store entirely; derived/computed layer rolls this up.

**Cross-cutting adjacencies** (not strictly "in" the package system, but interact closely):

- **Sync, distribution, and trust** — policy over what crosses machines. Visibility in the public/private sense. Signatures, attestations, trust rings. Gates the sync protocol itself.
- **Runtime / canvas / deployment state** — `DB.T`, `Handler.T`, canvases, secrets. References into the package tree, lives outside it.

### What changes when

| Layer         | Changes by                       | Churn        | Keyed on                          |
| ------------- | -------------------------------- | ------------ | --------------------------------- |
| Content       | Rehashing (new content)          | Low          | Hash                              |
| Bindings      | Author (rename/add/delete)       | Medium       | (Branch, name)                    |
| Annotations   | Author (opinion change)          | Medium       | (Branch, hash)                    |
| Derived       | Background compute / ingestion   | Medium–High  | (Branch?, hash)                   |
| Relationships | Author or extraction             | Medium       | (Branch, from_ref, to_ref, type)  |
| Commentary    | Author                           | Varies       | (Branch, hash) or as content      |
| Ephemera      | Runtime / CI / observers         | Continuous   | Out of band                       |
| Sync/trust    | Author / admin                   | Low          | (Branch, scope) or (hash, signer) |

Each has its own churn rate and blast radius. Today most non-content
state is smushed into layers 1–2, which is why editing a docstring
rehashes the world.


---

## What authors and agents reach for

Concrete questions by role, mapped to where the answer lives.

### When authoring code

| Question                                          | Lives in                            |
| ------------------------------------------------- | ----------------------------------- |
| Show me an example execution of this fn           | Saved traces (content + relation)   |
| Give me a realistic input                         | Sample values + `sample-for` rel    |
| How fast is this on real data?                    | Derived perf characterization       |
| What changed about this since v1?                 | Op log + commentary-as-item         |
| Any gotchas people hit?                           | Community-notes annotation          |
| Who reviewed this?                                | Approvals annotation                |
| What tests cover this?                            | `tests` relationship (reverse)      |
| Is this deprecated?                               | Deprecation annotation              |
| What does the name resolve to right now?          | Bindings                            |
| What depends on this if I change it?              | Extracted dependencies (reverse)    |

### When an AI agent works in the tree

| Question                                          | Lives in                                 |
| ------------------------------------------------- | ---------------------------------------- |
| Is this widely used?                              | Derived popularity/usage                 |
| Battle-tested?                                    | Stability annotation + coverage (derived)|
| Previous versions?                                | Op log via hash history                  |
| Who/what wrote this?                              | Provenance annotation                    |
| Is another agent touching this right now?         | Agent presence / WIP state               |
| Can I commit here?                                | Branch ownership + policy                |
| What's a typical input shape?                     | Sample values + types                    |
| What's the expected outcome?                      | Example traces + invariants              |

### When distributing / syncing

| Question                                           | Lives in                                 |
| -------------------------------------------------- | ---------------------------------------- |
| What should I push to that machine?                | Sync visibility policy                   |
| Do I trust this code to run?                       | Signatures / attestations                |
| Is this compatible with the version I have?       | Compat info (annotation/relationship)    |
| Was this approved by someone I trust?              | Approvals + trust ring                   |


---

## Annotations

Declared facts or opinions about a single item, by an author. Deprecation
is the template.

### Catalog

- **Deprecation.** Kinds: `SupersededBy <Reference>`, `Harmful`, `Obsolete`. The first inhabitant; shape details in the subsections below.
- **Descriptions / docstrings.** Biggest "get it out of the hash" win after deprecation. Keyed `(item_hash, element_path)` for top-level vs field/case/param-level. The field-path split is the one shape decision that's genuinely new — deprecation is item-level, descriptions are sub-item.
- **Stability.** stable / unstable / experimental / draft. Its own axis (not overloaded through deprecation kinds).
- **Purity / declared effects.** Flag set: reads-DB, writes-DB, HTTP, allocating, throws, unbounded, non-deterministic. Author-declared here; analysis-inferred in the Derived layer.
- **Approvals / attestations.** Append; multiple per item. Requires an identity model to mature.
- **Tags / aliases / search synonyms.** User labels; append with explicit remove.
- **Provenance.** hand-written / ported / imported / ai-generated / generated-by-tool-X. Single-value per current state; history accumulates through supersession.
- **Community notes / learnings / gotchas.** Free-form append annotations: "fails on empty input," "slow on strings > 1MB." Moderated over time.
- **Caveats / known-issues.** Sibling of learnings; more formal ("known issue: N=0 case throws"). Resolvable.
- **Per-item threaded comments.** See §Commentary.

Notably **not** here:
- **Visibility** (in the public/private sync sense). Moved to §Sync, distribution, and trust — it's sync-protocol policy, not author opinion about the item.

### Shared table shape

Each annotation gets its own projection table; the shape is the same:

```sql
CREATE TABLE <annotation> (
  <annotation>_id TEXT PRIMARY KEY,
  branch_id       TEXT NOT NULL REFERENCES branches(id),
  commit_hash     TEXT REFERENCES commits(hash),   -- NULL = WIP
  item_hash       TEXT NOT NULL,
  item_kind       TEXT NOT NULL,                   -- 'fn' | 'type' | 'value'
  state           TEXT NOT NULL,                   -- per-annotation: 'deprecated'|'undeprecated', 'described'|'undescribed', ...
  annotation_blob BLOB,                            -- serialized per-annotation payload; NULL for 'un*' marker rows
  created_at      TIMESTAMP NOT NULL DEFAULT (datetime('now')),
  created_by      TEXT,                            -- day-one requirement
  unlisted_at     TIMESTAMP                        -- supersession
);

CREATE INDEX idx_<annotation>_lookup
  ON <annotation>(branch_id, item_hash, item_kind) WHERE unlisted_at IS NULL;

CREATE INDEX idx_<annotation>_wip
  ON <annotation>(branch_id) WHERE commit_hash IS NULL;
```

Branch-chain visibility (WIP overrides committed, children inherit
ancestor commits, supersession via `unlisted_at`) is shared across
annotations. Once the second inhabitant lands, extract a SQL helper view
(`visible_at_branch(item_hash, item_kind, branch_id)`) so the logic is
written once.

The `annotation_blob` is a serialized per-annotation payload. Cheaper to
evolve than typed columns, at the cost of no SQL-level filtering on the
payload's internals — if a concrete annotation needs indexable filters
on a subfield, promote that subfield to its own column.

### Runtime access shape

Annotations the runtime acts on (a `Harmful` deprecation halts
invocation; an `unsafe` purity might gate something later) follow a
consistent plumbing pattern:

- The F#-side query lives on `PackageManager` next to `getFn`/`getType`/
  `getValue`, not on `ExecutionState`. The data lives in the package
  ecosystem; the query should too. Signature:
  `<check> : BranchId -> <FQN.Package> -> Ply<bool>` (or `-> Ply<'Kind>`
  when the check needs the kind).
- Back it with a per-branch cache (`ConcurrentDictionary<BranchId,
  Set<Hash>>` or similar) populated lazily on first query — avoids the
  per-call SQL round-trip inside the interpreter loop.
- `ExecutionState` holds only per-call policy toggles (e.g.
  `allowHarmful : bool`) plus a branch-baked closure on `Functions` so
  the interpreter's call site stays terse.
- Invalidate on branch change. Short-lived CLIs get this for free
  (rebuilt per invocation); long-lived processes need an explicit hook.

### Builtin-boundary conventions

Two conventions worth holding across annotations:

- **One builtin per "thing the caller asks about at once."** If two
  query shapes are always called together from Dark code (e.g.
  "all-deprecated hashes" + "hidden-by-default subset"), expose them as
  one builtin that returns a tuple. Avoids duplicate backend queries
  and round-trips.
- **Stringify in Dark, not F#.** Builtins should return structured Dark
  values — enums, records — not pre-rendered strings or tag codes like
  `"superseded-by:<hash>"`. Formatting for display is a Dark-side
  concern.

### Semantics differ per annotation

| Annotation       | Rule                                       |
| ---------------- | ------------------------------------------ |
| Deprecation      | Latest-wins per item                       |
| Description      | Latest-wins per (item, element_path)       |
| Stability        | Latest-wins per item                       |
| Purity           | Latest-wins per item                       |
| Approvals        | Append                                     |
| Tags             | Append, explicit remove                    |
| Provenance       | Latest-wins + history                      |
| Community notes  | Append; moderated                          |
| Caveats          | Append; resolvable                         |
| Comments         | Append, threaded                           |

Per-concern tables beat a generic `item_annotations(type, blob)` — a
generic table can't enforce latest-wins vs append vs threaded, and
doesn't index well on annotation-specific predicates. *Annotation* is
about the *shape of storage*, not about the values being strings;
visibilities and effect sets and approvals are all differently-
structured, and that's fine.


---

## Derived / computed

Data that's *computed*, not declared. Aggregated from ephemera or from
content analysis, rolled up to item-keyed rows, surfaced at authoring
time. Different shape from annotations: not author-editable, updated by
background processes, potentially stale with a `computed_at` timestamp.

### What belongs

- **Inferred purity / effect set.** Verify or populate what the author declared. Same flag set as the annotation.
- **Perf characterization.** "This fn averages 3.2s on inputs of size ~1M; p99 at 8s." Aggregated from traces over some window. Keyed per input-size class or per-dataset, not a single scalar.
- **Usage / popularity signals.** Call counts (recent / total), breadth (how many distinct callers), hotness. Drives search ranking and agent decisions.
- **Test coverage.** Computed from test runs that reached this item. Branch-scoped (different branches have different tests).
- **Inferred complexity.** "Looks O(n²) on real data." Speculative; depends on analysis maturity.
- **Inferred type signatures / shapes.** When the author omits, fill in.
- **Extracted dependencies** *(already exists — `package_dependencies`, this is the prototype for the layer)*.

### Shape

Similar shell to annotations, with different conventions:

```sql
CREATE TABLE derived_<kind> (
  id              TEXT PRIMARY KEY,

  -- often branch-scoped, sometimes global (depends on whether compute is branch-aware)
  branch_id       TEXT REFERENCES branches(id),

  item_hash       TEXT NOT NULL,
  item_kind       TEXT NOT NULL,
  <kind-specific columns>,
  computed_at     TIMESTAMP NOT NULL,
  source          TEXT,        -- 'analysis' | 'ephemera-aggregation' | 'test-runs' | ...
  confidence      REAL         -- optional; for inferred stuff
);
```

### Why separate from annotations

- **Source of truth differs.** Annotations are author intent; derived is observation/inference. Mixing them loses the provenance of *where this claim came from.*
- **Churn rate differs.** Annotations change on human action; derived churns as new ephemera arrives.
- **Conflict resolution differs.** Author-declared "pure" vs inferred "has side effects" should surface as a *conflict to investigate*, not overwrite each other. Only possible with separate tables.
- **Staleness is inherent.** `computed_at` matters; annotations don't need it.


---

## Relationships

Directed, typed edges between items. Where annotations are *about* one
item, relationships are *between* items.

Today: `package_dependencies(item_hash, depends_on_hash)` — extracted,
not declared, content-level.

Future declared relationships:

- **`tests`** — X is a test of Y
- **`example-of`** — X demonstrates usage of Y
- **`sample-for`** — X is a sample input for Y (pairs with §Sample values)
- **`example-trace-of`** — saved trace X shows Y being executed (pairs with §Saved traces)
- **`see-also`** — X and Y are related (browse)
- **`implements` / `satisfies`** — if interfaces/traits land
- **`derived-from`** — X was generated/transformed from Y (ports, codegen, AI generation)
- **`supersedes` / `superseded-by`** — partially covered by deprecation (`SupersededBy` kind carries a `Reference`); a dedicated relationship table would give us a reverse-index and cross-kind lookup without round-tripping through `deprecations`

Shape: one table per relationship type, branch-scoped. Same argument as
annotations. Indexed on both endpoints for symmetric lookup (`what tests
X?` + `what does X test?`).

The annotation-vs-relationship line: primary question "what is X?" →
annotation; "how does X relate to Y?" → relationship. When a pointer is
a supporting detail (deprecation's replacement), it's annotation-with-
reference; when the pointer *is* the fact, it's a relationship.


---

## Commentary — spans content and annotations

### Per-item threaded comments (annotations)

Short, authored, threaded discussion on a specific item.
`item_comments(id, branch_id, commit_hash, item_hash, item_kind, parent_id, author_id, body, created_at, unlisted_at)`.
Edits supersede; deletions mark.

CLI:
```
comment fn Darklang.Foo.bar "why not pattern match here?"
comments fn Darklang.Foo.bar
comment --reply <id> "because X"
comment --resolve <id>
```

### Long-form commentary as package items (content)

Named, referenceable: design docs, RFCs, tutorials, migration guides,
module overviews. Content-addressed, versioned, searchable.

Two implementations:
- **A. New entity kind `PackageComment`.** Adds `ItemKind.Comment`,
  `Reference.PackageComment`, `AddComment` op, `package_comments` content
  table.
- **B. Reuse `PackageValue` with a known type** (`Darklang.Docs.Comment`).
  Cheaper, less native.

Start with B for experiments; graduate to A when used seriously.

Examples: `Darklang.Stdlib.List.readme`, `Darklang.Team.Notes.migrationFromV1`, `Darklang.SCM.RFCs.branchModel`.


---

## Saved traces and example executions

Dark's traces are first-class at runtime. Saving and *naming* them makes
them shareable artifacts — the single most distinctive "reach for" in a
Dark-shaped system.

Use cases:
- "Show me an execution of `parseJson` on a nested structure."
- "Here's the trace where the bug reproduces."
- "Canonical example of happy-path usage."

**Traces aren't content**, even though it's tempting to treat them that
way. They come in very high volume (one per handler invocation), they
need their own garbage-collection policy (rolling window, sampling,
explicit pinning), and the tables for them are already sketched and
in-progress as a separate subsystem. Treating them like a `PackageValue`
would put high-churn, GC-managed blobs in the middle of the
content-addressed package store — wrong fit for both shapes.

The right split:

- **Trace storage** lives in its own subsystem, outside the package
  tree. Own tables, own retention policy, own sampling. This is where
  the volume lives. Sits roughly alongside Ephemera in the layered
  model, though a "saved / pinned trace" is authored-intent-ish and
  shouldn't be garbage-collected the same way as raw runtime traces.
- **"Example trace of X" is a relationship** in the package tree. A
  small, branch-scoped `example_trace_of(item_hash, item_kind, trace_id)`
  table that points from a package item to a stable trace-id in the
  trace subsystem. The authored intent lives here; the trace blob lives
  there.
- **Pinning a trace** moves it from the sampled/expiring tier to the
  retained tier in the trace subsystem. The `example_trace_of`
  relationship implicitly pins (or at least prevents GC).

What that gets us:
- Traces can be shared, referenced, and named *without* being content-
  addressed package items.
- The package system doesn't have to care about trace volume or
  retention semantics.
- A sample input + a pinned trace of running a fn on that input = an
  executable, referenceable worked example, same "pairing" pattern tests
  want.

Open design questions:
- What's the stable identifier for a trace (trace-id, trace-hash, both)?
  Depends on whether we want cross-canvas referenceability.
- Does the trace subsystem itself want branch-awareness, or does the
  relationship handle that? Probably the latter — traces are per-run,
  branches are per-code-version; the relationship carries the code
  context.
- GC interaction with the pinning relationship: dropping the
  relationship row has to unpin the trace (or schedule it for GC).
  Spelling that coupling out before shipping.


---

## Sample values / fixtures

"Give me a realistic input for this fn" is a constant author-time
question. Paired with saved traces and with the test layer.

Shape is lightweight:
- Samples are just `PackageValue`s — they already exist.
- Add a `sample-for` **relationship** pointing the value at the fn (or
  type) it's a sample for.
- Tooling looks up "samples for `parseJson`" via the reverse index.

Natural pairing: a sample input + a saved trace of running the fn on
that input = an executable, referenceable worked example. Same pattern
the test layer wants.


---

## Tests and verification

Tests are content (you run them) + relationship (of something specific)
+ annotations (flaky, slow, quarantined).

Likely shape:
- Tests are `PackageFn`s returning a test-outcome type.
- `tests` relationship from test → item under test.
- Annotations: `flaky`, `slow`, `quarantined`.
- Golden / expected outputs: content-addressed records, possibly a new
  entity kind.
- Properties / invariants: might be a new entity kind (`PackageProperty`)
  or annotations.
- Benchmarks: `PackageFn` with a `benchmark` tag + `sample-for` pointing
  at their input.

Test runs themselves are ephemera; rolled-up coverage is derived/computed.

Not urgent; calling out so the model has room for it.


---

## Sync, distribution, and trust

Cross-cutting policy over what crosses machine boundaries. This is *not*
annotation — it's sync-protocol configuration, and it's central enough
to Dark's multi-machine vision to deserve its own subsystem.

### Visibility (in the public/private sense)

- **Private** — local only; never syncs.
- **Shared** — syncs to specific peers / teams.
- **Public** — syncs to anyone on the sync network.

Scoped at branch, module, or item level. Not about "is this API internal"
(that's stability + tags); about "does this cross my machine boundary."

Likely a policy table keyed by branch and optionally by path prefix,
independent of the in-branch annotation tables. Informs what `sync push`
/ `sync pull` will transfer.

### Signatures and attestations

- Author signatures over content hashes.
- Reviewer signatures over item + review state ("I approved hash H for
  purpose P").
- Builder/provider signatures (chain of custody).

Crypto adds real weight — not in scope to design here, but noting the
shape: a `signatures` table keyed by `(item_hash, signer_id)` with
signature blobs and an attestation type. Trust layer consumes these.

### Trust rings

Each user/instance maintains a trust graph: who do I trust, at what
level, for what purpose. A sync pull from peer X will only import items
signed by entities X trusts *that I also trust*. Enables pulling code
without implicit equivalence to running it.

### Connection with approvals annotation

Approvals in the annotation layer are *in-branch* ("this was reviewed by
Alice on branch feat-x"). Signatures here are *cross-boundary* ("Alice's
public key signed this hash"). Same event, different projections — the
annotation is the record, the signature is the exportable attestation.


---

## Governance & workflow (brief)

Reviews, proposals, ownership, policy. Much of it is annotations
(approvals, ownership-as-annotation). Branch-level workflow (proposal
state, merge readiness) lives alongside `branches`/`commits`, not items.
Punt seriously until identity/ACLs land; deprecation annotations with
authorship give us the building blocks for free.


---

## AI / agent adjacency

Dark's AI-first design pulls a few specific things into view:

- **Provenance for AI-generated items.** `provenance = ai-generated`
  annotation, ideally with a reference to the conversation/session.
  Useful for audit, trust, and agent learning.
- **Agent presence / realtime state.** "Agent X has WIP ops on branch Y,
  last active 2m ago." Prevents multiple agents stomping each other or
  the human. Probably branch-scoped state, not item-scoped. New
  lightweight table, similar to a session tracker.
- **Task decomposition artifacts.** As agents break work into steps,
  intermediate plans and partial results might want to live somewhere.
  Probably commentary-as-item or scripts, reusing existing machinery.
- **Consent/authority scopes.** "Agent X can edit these modules, but not
  those." Overlaps with governance; lands together.


---

## External references

`Reference` grows variants for things outside the Dark-authored package
system:

- **`Builtin of BuiltinName`** — builtins are first-class pointers (keep
  their inline `Deprecation<'name>` since they have no op log).
- **`External of Uri`** — HTTP endpoints, external APIs, foreign-language bindings.
- **`Concept`** (speculative) — abstract ideas rather than items.

Naming note: the DU is `Reference`, so cases are `Reference.Builtin` /
`Reference.External` / `Reference.Concept` — no `Ref` prefix needed, the
DU name already carries it.

External refs let annotations and relationships describe integration
points, not just Dark-internal edges.


---

## Organization / grouping

Module re-exports, canonical aliases, public-API surface. Mostly handled
by visibility (now in Sync) + tags + `alias-of` relationship. Probably
doesn't need its own layer; call out when a concrete use forces a design
round.


---

## Runtime / deployment (adjacent)

Canvases, DBs, handlers, secrets. Reference into the package tree but
live outside it. The package tree is the *code*; runtime/deployment is
the *where/how it executes*. Already partially modeled; not this doc's
focus.

Observation: as the package system grows layers (annotations, derived,
commentary), the canvas/runtime side will want parallel patterns
(deployment metadata, environment configs, rollout state). Not one
unified system, but cross-pollinating patterns will save reinventing.


---

## Consequence for content tables

Every annotation/derived/relationship extraction moves
`PackageFn/Type/Value` toward **pure structural definition**:

- Out: `deprecated` (done — first annotation extracted), `description`
  next, eventually declared purity, stability.
- In (stays): `body`, `typeParams`, `parameters`, `returnType`,
  `declaration`.

End state: the content hash is a *true content hash* — same bits = same
hash = same behavior. `Reference` is the universal pointer. Extraction
is staged, one axis at a time, each a hash-churn moment folded into
`HashStabilization`.


---

## Rollout / ordering

1. **Deprecation** — done (first annotation; proved the pattern).
2. **Description extraction** — highest remaining payoff. Gets docstrings
   out of the hash. Reuses the annotation-table shape; the new wrinkle
   is the `(item_hash, element_path)` key for field/case/param-level
   descriptions.
3. **Shared visibility helper.** SQL view/helper for the branch-chain
   slice. Before the third annotation lands, extract
   `visible_at_branch(item_hash, item_kind, branch_id)` so the JOIN
   pattern isn't copy-pasted into every projection table's read path.
4. **Per-item comments.**
5. **Saved traces + `sample-for` / `example-trace-of` relationships.**
   Dark-distinctive; enables the "example execution" / "realistic input"
   flows.
6. **Derived perf characterization.** Background aggregation from
   ephemera; surfaced in `view` and call-sites.
7. **Declared relationships** (`tests` first).
8. **Stability / declared purity / declared effects.**
9. **Commentary as package item.**
10. **Sync-visibility + signatures + trust rings.** Big, its own design round.
11. **Approvals, tags, provenance, community notes.** As needs arise.
12. **Agent presence / AI-specific layers.** When multi-agent workflows become common.
13. **External refs.** When concrete use forces them.


---

## Open questions

1. **Policy configuration.** Consumer-side policy maps
   (`DeprecationKind → loudness`, derived-data freshness thresholds, tag
   importance) need a home. User-space dark value? CLI config file?
   `Darklang.CLI.Config` package item? Decide before LSP integration.
2. **Annotation stickiness on rebind.** When `SetName(loc, ref)` rebinds
   a location to a new hash, what carries forward? Deprecation doesn't
   (by design — it's hash-keyed, so the new hash starts clean).
   Description probably *should*. Per-annotation policy flag.
3. **Universal authorship.** Every annotation table should carry
   `created_by` from day one; hard to retrofit. `deprecations` currently
   doesn't — add it before the second annotation lands and backfill.
4. **Annotation vs relationship vs derived.** Fuzzy edges. Rough rules:
   "what is X?" → annotation; "how do X and Y relate?" → relationship;
   "what did we observe/compute about X?" → derived. Revisit when a
   concrete case doesn't fit.
5. **Derived branch-scoping.** Is perf per-branch or global? Depends on
   whether branches diverge enough for perf to differ meaningfully.
   Probably global with a branch-override table for rare cases.
6. **Conflict between declared and inferred.** Author says pure; analysis
   says not. Surface as a visible disagreement, don't auto-resolve.
7. **Cross-branch references.** `Reference` is hash-only. Do we ever need
   name-based branch-specific refs (e.g., "replacement is whatever
   `Foo.bar` resolves to on main")? Punt.
8. **Commentary visibility.** Public to anyone who sees the item? Scoped?
   Starts simple (inherits item visibility).
9. **Garbage collection.** Superseded annotation rows, stale derived
   rows, old traces. Not urgent but plan before it matters.
10. **Test / verification model.** Needs its own design round.
11. **Saved-trace storage.** Traces are large. Content-addressed works
    but storage cost is real. Consider compression, pruning, or
    offloading.
12. **Trust model specifics.** Signature schemes, key management,
    revocation. Whole design round on its own.
13. **Cache invalidation for runtime-visible annotations.** The
    per-branch `ConcurrentDictionary` cache behind `isHarmful` works for
    CLI invocations (short-lived, rebuilt each run) but long-lived
    processes (LSP, bwdserver) need branch-change eviction. Figure out
    the invalidation hook before relying on this pattern for the second
    runtime-visible annotation.
