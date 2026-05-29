# Distributed event sourcing + branched MVU

This is the keystone. It names the single idea the rest of the design docs are
facets of: **Dark is a distributed event-sourced system with a branched
model-view-update loop.** State is a timestamped stream of **ops**; everything
you see is a **projection** of that stream; the unit that syncs and replays is
one thin `App`.

Read it alongside [event-bus.md](event-bus.md) (the substrate that carries ops),
[conflicts.md](conflicts.md) (how concurrent ops reconcile), [sync.md](sync.md)
(how ops cross machines), [composable-mvu.md](composable-mvu.md) (the UI layer on
top), and [async.md](async.md) (how a frame waits on an op without blocking).

## Simplify Darklang greatly

The ambition is reductive, not additive. For now the system needs to support
exactly four things:

1. **A timestamped set of ops** — the only way state ever changes.
2. **A model of their conflicts (or constraints)** — when two ops disagree, and
   what to do about it.
3. **A way to sync all of it** — ops move between instances; nothing else has to.
4. **Some projections** — derived views of the folded op stream (package items,
   branches, the file view, dependency graphs, the conflict list).

That is the smallest, most composable system for both **data** and **apps**.
Everything richer — the package manager, the SCM, the CLI, crons, a structural
editor — is built *on top* of these four primitives, in Dark, as an App.

## The one thin `App` type

The sync primitive is a single, deliberately **low and thin** generic shape. It
does **not** hold a *list* of ops; it declares the **op type** and **how to play
one op back**. Think Elm/Elmish, but where the update unit (`'op`) is the thing
that syncs and replays across instances.

```fsharp
type App<'state, 'op> =
  { name       : String
    empty      : 'state                     // starting state
    apply      : 'op -> 'state -> 'state     // play ONE op back — the only way state moves
    conflict   : 'op -> 'op -> Bool          // do two concurrent ops clash?
    resolve    : 'op * 'op -> List<'op>      // reconcile a clash (auto where it can)
    views      : 'state -> List<View>        // projections to render (each by id/hash)
    invariants : 'state -> List<Violation> } // at-rest / runtime constraints
```

A counter, in full:

```fsharp
type CounterOp = | Inc | Dec | SetTo of Int64

let counter : App<Int64, CounterOp> =
  { name = "counter"
    empty = 0L
    apply = fun op n ->
      match op with
      | Inc -> n + 1L
      | Dec -> n - 1L
      | SetTo v -> v
    conflict = fun _ _ -> false          // increments commute — never clash
    resolve  = fun (a, _) -> [a]
    views    = fun n -> [ Text $"count: {n}" ]
    invariants = fun _ -> [] }
```

The counter is deliberately the *minimum* — its `conflict`/`resolve`/`invariants` are
trivial. For a worked App where those members do real work (concurrent same-key
clashes, surface-as-data resolution, a hard invariant), see
[example-app.md](example-app.md).

### How it distributes

- Ops arrive locally or stream in from a peer over the event bus. The **op
  stream — not the `App` value — is the durable, synced thing.**
- State is **derived**: fold `apply` over the ordered op stream. Replay = re-fold.
- When two instances produce concurrent ops, `conflict` flags the pair and
  `resolve` emits reconciling ops. Most clashes auto-resolve; the rest are just
  data we fix later (see "Most conflicts are OK").
- `views` are projections of state for the CLI/UI; `invariants` are the
  runtime/at-rest constraints (the runtime-tests / at-rest-tests special types;
  Scriptorium is a reference).
- A msg/cmd-style UI loop (the Elmish part) is a *layer on top*: UI intent → ops.
  It is kept out of the thin core and lives in [composable-mvu.md](composable-mvu.md).

### Versus Elmish

`apply` ≈ `update` (an op instead of a msg; effects handled by the substrate plus
[capabilities](capabilities.md), not a `Cmd` in the core); `views` ≈ `view`. The
additions that make it work across instances are the distributed reconciliation
(`conflict`/`resolve`) and `invariants`. We rebuild the current CLI experience as
one such App, and users build their own on top.

### Open: conflict-blind vs. conflict-carrying

Does generic op-playback stay **conflict-blind** (each projection owns its own
conflict handling) or does the `App` **carry** `conflict`/`resolve` as above?
Leaning toward carrying it — it is more ergonomic and keeps reconciliation next
to the op definition that needs it. Settle this when the first real App is built;
the counter above assumes carrying.

There is a real pull the other way, worth stating: **op-playback itself should be
hot-swappable, and the generic playback engine arguably shouldn't have to think
about conflicts at all** — each *projection* deals with the conflicts it cares
about. That argues for a conflict-blind core with `conflict`/`resolve` living on
the projection rather than the App. The counter keeps them on the App for
ergonomics; a projection-heavy App (say, the package manager) might push them down.
This is the same tension, seen from the playback side.

### The fuller field set (and where each piece lives)

Earlier sketches of `App` carried more fields — `msg`, `cmd`, `autoResolutions`,
`constraints`, explicit `projections`/`DBs`. They are not dropped; they are *placed*:

| Sketch field | Where it lives in the thin model |
|---|---|
| `data` / `state` | `'state` — the fold target |
| `ops` | `'op` — the op type; the stream is external (synced), not a field |
| `msg`, `cmd` | the MVU layer *on top* (see [composable-mvu.md](composable-mvu.md)); UI intent → ops, effects via [capabilities](capabilities.md) — kept out of the thin core |
| `conflicts` | `conflict` (or pushed to the projection, per above) |
| `resolutions` + `autoResolutions` | `resolve` — auto-resolution is just the subset of `resolve` that needs no human; "most conflicts are OK" makes this the common path |
| `views` | `views` — projections to render |
| `projections`, `DBs` | the storage split below — regenerable projection caches, not core fields |
| `constraints` | `invariants` — runtime + at-rest constraints |

The thin core stays `name/empty/apply/conflict/resolve/views/invariants`; the
richer vocabulary is these same ideas at their proper layer.

## Ops vs. projections — the split that has to stay clean

Model and view are distributed, but a **projection of an update very likely
happens on a specific instance**. That asymmetry drives the storage split:

- **The core sync DB** (the ordered, content-addressed op stream) is one thing —
  small, durable, shared. It is what [sync.md](sync.md) replicates.
- **Branch- and session-specific projections** (materialized package items,
  the file view, dependency graphs, the conflict list) are a *different* thing —
  regenerated locally, never shipped on the wire.

We need recovery for distribution races — e.g. two branches on different
instances pointing the same name to different hashes. That is resolved through
the [conflicts](conflicts.md) system, not by trying to keep projections in lockstep.

### How to split it cleanly

The rule of thumb decides membership: **if losing it costs only CPU to rebuild, it
is a projection.** What follows from taking that seriously:

**Separate stores, and the boundary is physical.** Keep these in *separate SQLite
files* (or the config dir), not just separate tables:

- **`ops.db`** — append-only, content-addressed ops + commits. Global, durable,
  the only thing sync touches. This is the canonical *shared* state.
- **`projections.db`** — every materialized view: the `name → hash` resolution
  table, package-item bodies by location, dependency graphs, the file view, the
  conflict list, search indexes. Branch- and session-scoped, `DROP`-able,
  rebuilt by folding `ops.db`.
- **`local.db` / the `.darklang` config** — local-authoritative state that is
  *neither synced nor derived*: capability grants (instance-specific —
  [capabilities.md](capabilities.md)), sync-remote config and the tailnet-login →
  account binding ([sync.md](sync.md)), and WIP *if* the "WIP stays local" option
  wins ([conflicts.md](conflicts.md)). This is the category the two-store split
  would otherwise have no home for: it is authoritative (losing it loses
  information, so not a projection) yet private (it must never ride the wire, so
  not `ops.db`). The user backs it up; sync ignores it.

Separate files rather than table-groups because the boundary then can't be crossed
by accident: sync ships `ops.db` deltas and *physically cannot* ship a projection
or a local grant; `projections.db` can be deleted any time and rebuilt with zero
information loss; `local.db` is the one a careful user backs up but never shares.
(`projections.db` is the natural thing to `.gitignore`.)

A note on WIP: the local-vs-synced choice for WIP ([conflicts.md](conflicts.md)) is
exactly the question of whether WIP lives in `local.db` (option a, never folds into
cross-instance conflicts) or in `ops.db` as non-committed ops (option b, folds and
can clash). The three-store model makes that choice concrete: it is "which file."

**A projection declares three things**, and nothing else couples it to the core:

1. **Which op kinds it folds** (so the cache knows what's relevant).
2. **Its scope** — global, per-branch, or per-session.
3. **Its invalidation trigger** — which incoming op kinds dirty it.

On op arrival, mark dependent projection entries dirty; rebuild lazily on next
read (incremental view maintenance). A branch switch re-points or rebuilds the
per-branch caches; a session ends and its session-scoped projections evaporate.
No projection is ever authoritative, so none of them can be "wrong" in a way that
needs distributed repair.

**Why the distribution race is now a non-problem.** Two instances binding the same
name to different hashes is *not* two projections that disagree and must be
reconciled — the `name → hash` binding is a projection on both sides, derived from
each instance's ops. What actually exists is two ops in the log. When both ops fold
on one instance, the disagreement surfaces as the `Name → two hashes` conflict
([conflicts.md](conflicts.md)) and the dispatch resolves it. Recovery is re-fold +
dispatch over `ops.db`, never a distributed lock on `projections.db`. Keeping the
projection non-authoritative is exactly what makes races cheap.

The open part is now narrow: the precise table shapes inside `projections.db` and
how aggressively to cache per-branch (rebuild-on-switch vs. keep N warm) — a
performance tuning question, not an architectural one.

## Event-streaming *is* sync

The layering we are pursuing: event-streaming is not a feature alongside sync —
it **is** sync. Stream events, replay them, detect conflicts (which may
themselves be streams), compose. Solved with the least total code, this is the
path that eventually lets us **remove the `.dark` files from the repo**: once the
package corpus is just an op stream with projections, the checked-in files are
redundant. (That removal is itself punted until baseline sync + stability —
see [bootstrap.md](../removing-dark-files/bootstrap.md).)

## Replay and nondeterminism — an op records the result, not the intent to call

Replay is "re-fold `apply` over the op stream," and several docs lean on it being
**deterministic**: [conflicts.md](conflicts.md) requires resolutions to resolve to
"the recorded choice"; [async.md](async.md) calls playback deterministic because
await points are explicit. That seems to clash with two facts — some bodies are
"forever lazy" LLM calls that are *nondeterministic* on every invocation
([algorithm.md](../pdd/algorithm.md)), and the materialization event bus is marked **not
durable** ([event-bus.md](event-bus.md)). The reconciliation is one rule:

> **An op records the *result*, not the *intent to call*.** When a materializer
> (an LLM, a corpus search, a network fetch) produces a value, the op appended to
> the stream carries the produced value — already content-addressed. Replay folds
> that recorded value; it never re-invokes the producer.

So the materialization *bus* being ephemeral is not a contradiction: the bus is a
high-frequency live signal you don't persist, but the **committed result** of a
materialization is an ordinary durable op. Live evaluation calls the LLM; replay
reuses the committed output. Effects work the same way — a `Promise` resolves
once, live; the resolved value is what lands in the op, so the re-fold is pure.

The one genuine exception is exactly the flagged risk: a **forever-lazy body that
is never committed** — a fn whose body *stays* a delegated LLM call and is re-run
on every invocation. That body cannot replay deterministically, because there is
no recorded result to fold; re-running it is re-rolling the dice. That is the
"trace-replay divergence" hazard in
[process-risks.md](../meta/process-risks.md), and the
mitigation is the same rule applied to traces: **capture the LLM's output in the
trace** so a replay of *that run* folds the captured output, while a fresh live run
is free to differ. Determinism is a property of folding recorded results, not of
the producers that first generated them.

## Convergence precondition: shared App logic

Replay-determinism handles the *result* of an op. There is a second, easy-to-miss
precondition for two *instances* to converge: they must fold the same op stream
with the **same `apply`/`conflict`/`resolve`**. These functions are part of the
`App`, and the `App` is editable and forkable — so two instances could be running
*different* reconciliation logic, reach different `resolve` outputs for the same
clash, and diverge.

This does not need a new guard, because the App's own functions are themselves
**content-addressed package items in the op stream**. So:

- "Same logic" = "same hashes." Two instances that resolved from the same
  `apply`/`conflict`/`resolve` hashes are guaranteed to fold to the same state;
  that *is* the convergence guarantee.
- A fork of the reconciliation logic (someone edits `resolve`) is not silent drift
  — it is a new hash for that package item, which shows up as an ordinary
  `Name → two hashes` conflict ([conflicts.md](conflicts.md)) on the App's *own*
  definition. The system surfaces "you two are running different resolvers" as the
  same kind of data it surfaces any other divergence.
- Divergence by fork is therefore *deliberate and visible*, never accidental.
  Whoever wants to converge re-points to a shared hash; whoever wants to keep their
  fork keeps it, and the two are simply different Apps.

This is why the App logic living *in* the synced stream (not as privileged F#) is
load-bearing: it makes "do we even agree on how to merge?" a first-class, inspectable
conflict rather than an invisible assumption.

## The App is live, forkable, and self-managing

- **The App value is editable** — by people (via the CLI) or by agents. The App
  is data like everything else.
- **Any user of an app can fork or extend it** — its views, its behavior, any
  part of the experience — and migrate their data along the way. Not just the
  author: whoever is *using* it. "If a user's system implements this `App` type,
  we respect it and run it."
- **An App's package values magically get their own management** — or, more
  precisely, the CLI provides that management *around* them, so an App author
  does not re-implement listing, history, diffing, and sharing.
- **Auto-views from data.** A value with no hand-written `views` still renders —
  by reflection over its type, or by LLM code generation of a first view the user
  then edits. This needs a baseline **view engine** (see
  [view-sketches.md](../editing-software/view-sketches.md) and [structural-editor.md](../editing-software/structural-editor.md)).

## Rebuild the CLI as an App

The proving ground is to rebuild a fork of the **current CLI experience as an
App**, solving sync and distribution along the way. The target is the *smallest*
system that replicates current CLI functionality — core extracted, added pieces
composed on top — and that eventually lets us drop the `.dark` files.

This **involves the parser**: the App/editor work wants a `ref` keyword (a
reference to e.g. a hash — probably just a global function like `print` that we
teach the parser/name-resolver), a **composable parser written in Darklang**
(compilable to tree-sitter or similar — a DSL of types/fns plus Dark code that
compiles down), and **`compile` as a builtin as soon as possible**. These are
enabling primitives, design inputs rather than immediate builds.

## Crons and daemons are just distributed apps

- **Crons** are modeled as a distributed App we officially support, as an
  extension of the default CLI. A cron tick is an op; subscribers run on each tick.
- **Daemons** start via something like `start()` and are hosted by the resident
  process (see [cli-daemon.md](cli-daemon.md)).
- **One projection of any distributed App is "the list of conflicts"** — usually
  ignorable, occasionally the thing you act on.

## Respect the special types

Two kinds of constraint are first-class and the `App.invariants` member is where
they land:

- **Runtime tests / constraints** — checked as the App runs.
- **At-rest constraints / tests** — checked on the state at rest.

Scriptorium is the reference for how these read and report.

**What happens when an invariant is violated.** `invariants` returns a
`List<Violation>`, and a violation is *not* a special case — it folds into the same
conflict model as everything else. Each kind is checked at its matching timing
([conflicts.md](conflicts.md)): at-rest invariants after the fold reaches a resting
state, runtime invariants during evaluation. A non-empty result is a
`ConstraintViolated` conflict handed to the dispatch. Consistent with "most
conflicts are OK," the default is to **surface, not block**: the current set of
violations is itself a projection — the "violations list," a sibling of the
conflict list — that the App can render and a human can act on later. Only an
invariant deliberately declared *hard* resolves to `FailLoudly` and blocks the op
that would breach it. So `invariants` is to at-rest/runtime constraints what
`conflict` is to op clashes: a producer of standard conflicts, not new machinery.

## Most conflicts are OK

A recurring stance worth stating once, here, and referencing elsewhere: **most
conflicts are fine.** A conflict is just data — a condition we do not like and can
get to later. The system should surface conflicts as a projection and keep
running, not block. Only a small minority need eager resolution; the
[conflicts](conflicts.md) dispatch decides which.

## How the docs compose

| Doc | Its role in this frame |
|---|---|
| [event-bus.md](event-bus.md) | the substrate that carries and replays ops; parking |
| [conflicts.md](conflicts.md) | the `conflict`/`resolve` members, by evaluation-time |
| [sync.md](sync.md) | moving the op stream between instances |
| [composable-mvu.md](composable-mvu.md) | the msg/cmd UI loop layered on `apply`/`views` |
| [capabilities.md](capabilities.md) | what an App's ops are allowed to *do* as effects |
| [async.md](async.md) | how a frame waits on a not-yet-resolved op |
| [view-sketches.md](../editing-software/view-sketches.md) / [structural-editor.md](../editing-software/structural-editor.md) | the view engine that renders `views` |
| [apps-surface.md](apps-surface.md) | `dark apps` — the user-facing install/fork/run shape of an App |

The meta-claim, worth holding loosely but testing hard: capabilities, conflicts,
event streams, and MVU are not five systems — they are one distributed
event-sourced, branched-MVU system seen from five angles.

## Refactors are ops

A refactor — rename, move, extract, inline, change-signature — is not a special
tool bolted onto the editor. It is **an op**, the same kind of thing every other
state change is here. This falls straight out of the `App` model: if the only way
state moves is `apply : 'op -> 'state -> 'state`, then a rename is just a
`Rename` op the agent emits and the fold replays, no different in kind from `Inc`
on the counter.

Building refactors into the language (rather than hand-coding each one) buys the
whole event-sourced story for free:

- **They sync.** A refactor op rides the wire like any other op; a peer re-folds
  it and lands on the same projection.
- **They conflict and resolve.** A concurrent rename-vs-edit or move-vs-edit is
  exactly the op-vs-op case in `conflicts.md` — mostly commutative, auto-resolved,
  and recorded. Signature-change-vs-call-site surfaces per call site. The refactor
  needs no bespoke conflict logic; it reuses the `conflict`/`resolve` members.
- **They replay deterministically.** Because the refactor is a content-addressed
  op, playback re-applies it identically — no "re-run the refactor heuristic and
  hope" step.

The agent invokes refactors as ordinary moves during materialization; the human
**approves or refines** the resulting ops. So a refactor is a first-class citizen
of the op stream, surfaced for human sign-off, not an out-of-band mutation that
the event-sourced model has to be patched to tolerate.
