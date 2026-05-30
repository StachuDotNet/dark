# Distributed event sourcing + branched MVU

The keystone. The single idea the other pre-S&S docs are facets of: **Dark is a distributed
event-sourced system with a branched model-view-update loop.** State is a timestamped stream
of **ops**; everything you see is a **projection** of that stream; the unit that syncs and
replays is one thin `App`. The sync layer, the conflict dispatch, and the UI loop are all
built *on* this — they reference down here, not the reverse.

## Simplify Darklang greatly

Reductive, not additive. The system needs exactly four things:

1. **A timestamped set of ops** — the only way state ever changes.
2. **A model of their conflicts/constraints** — when two ops disagree, and what to do.
3. **A way to sync all of it** — ops move between instances; nothing else has to.
4. **Some projections** — derived views of the folded op stream.

That's the smallest composable system for both **data** and **apps**. Everything richer — the
package manager, SCM, CLI, crons, a structural editor — is built on these four, in Dark, as
an App.

## The one thin `App` type

The sync primitive is a single low, thin generic shape. It does **not** hold a *list* of
ops; it declares the **op type** and **how to play one op back**. Elmish, but where the
update unit (`'op`) is the thing that syncs and replays across instances.

```fsharp
type App<'state, 'op> =
  { name       : String
    init       : 'state                      // starting state
    apply      : 'op -> 'state -> 'state     // play ONE op back — the only way state moves
    conflict   : 'op -> 'op -> Bool          // do two concurrent ops clash?
    resolve    : 'op * 'op -> List<'op>      // reconcile a clash (auto where it can)
    views      : 'state -> Map<ViewId, View> // NAMED projections; an above-app picks 0-N by id
    invariants : 'state -> List<Violation> } // at-rest / runtime constraints
```

`views` is **keyed, not an anonymous list** — each projection has a stable `ViewId`, so a
composing ("above") App reaches in and renders *whichever* sub-views it wants (0, some, or all)
rather than getting an opaque blob. A concrete App typically wraps the keys in a typed record
for ergonomics (e.g. the outliner exposes `outlinePane`/`editorPane`/`keyHints` — see
[composable-mvu.md](composable-mvu.md)); the generic type stays `Map<ViewId, View>`.

A counter, in full:

```fsharp
type CounterOp = | Inc | Dec | SetTo of Int64

let counter : App<Int64, CounterOp> =
  { name = "counter"; init = 0L
    apply = fun op n -> match op with | Inc -> n + 1L | Dec -> n - 1L | SetTo v -> v
    conflict = fun _ _ -> false          // increments commute — never clash
    resolve  = fun (a, _) -> [a]
    views    = fun n -> Map [ "count", Text $"count: {n}" ]
    invariants = fun _ -> [] }
```

The counter is the *minimum* — its `conflict`/`resolve`/`invariants` are trivial. For an App
where those do real work (concurrent same-key clashes, surface-as-data resolution, a hard
invariant), see [example-app.md](example-app.md).

> Naming: the starting state is `init` (not `empty`) — it may take args, matching Elmish
> `init`.

### How it distributes

- Ops arrive locally or stream in from a peer over the event bus
  ([event-bus.md](event-bus.md)). The **op stream — not the `App` value — is the durable,
  synced thing.**
- State is **derived**: fold `apply` over the ordered op stream. Replay = re-fold.
- Concurrent ops: `conflict` flags the pair, `resolve` emits reconciling ops. Most clashes
  auto-resolve; the rest are just data we fix later.
- `views` are projections for the CLI/UI; `invariants` are runtime/at-rest constraints.
- A msg/cmd UI loop (the Elmish part) is a *layer on top* — UI intent → ops — kept out of the
  thin core ([composable-mvu.md](composable-mvu.md)).
- **Ops flow through instances that don't run the consumer.** An instance relays and stores
  ops for Apps/extensions it isn't itself running — they sit in its stream as inert,
  well-formed data. Participation is a *local* choice about which projections you materialize,
  **not** a gate on which ops cross your boundary. So an extension adopted later still sees the
  complete history, and a fleet adopts incrementally with no lockstep upgrade.

### Versus Elmish

`apply` ≈ `update` (an op instead of a msg; effects via the substrate +
[capabilities](capabilities.md), not a `Cmd` in the core); `views` ≈ `view`. The additions
that make it work across instances are the distributed reconciliation (`conflict`/`resolve`)
and `invariants`. We rebuild the current CLI as one such App; users build their own on top.

### Open: conflict-blind vs. conflict-carrying

Does generic op-playback stay **conflict-blind** (each projection owns its conflict handling)
or does the `App` **carry** `conflict`/`resolve`? Leaning toward carrying it — more
ergonomic, keeps reconciliation next to the op it's about. But there's a real pull the other
way: op-playback should be hot-swappable, and a generic playback engine arguably shouldn't
think about conflicts at all — each projection deals with what it cares about. The counter
carries them for ergonomics; a projection-heavy App (the package manager) might push them
down. Settle when the first real App is built.

### The fuller field set (and where each piece lives)

Earlier sketches carried more fields. They aren't dropped, they're *placed*:

| Sketch field | Where it lives now |
|---|---|
| `data` / `state` | `'state` — the fold target |
| `ops` | `'op` — the op type; the stream is external (synced), not a field |
| `msg`, `cmd` | the MVU layer on top; UI intent → ops, effects via capabilities |
| `conflicts` | `conflict` (or pushed to the projection) |
| `resolutions` + `autoResolutions` | `resolve` — auto-resolution is the no-human subset |
| `views` | `views` |
| `projections`, `DBs` | the storage split below — regenerable caches, not core fields |
| `constraints` | `invariants` |

The thin core stays `name/init/apply/conflict/resolve/views/invariants`.

## Storage — one DB per branch, per app, plus a core store

`main` today ships a single `data.db` (seeded from `seed.db`) in `.darklang/`. The
event-sourced model splits that by *lifetime and shareability*, with the rule of thumb:
**if losing it costs only CPU to rebuild, it's a projection.**

```
~/.darklang/
  core.db              # THE canonical core: ordered, content-addressed ops + sync
                       #   coordination (remotes, routing, app registry). The core daemon
                       #   owns it; the ONLY thing sync replicates. ("Each repo is an ops DB"
                       #   — core.db is the central repo.)
  apps/
    <app>.db           # one ops+projection DB per active/long-running app. Most are
                       #   temporary and GC'd; some persist. Sync is built ON this per-app
                       #   system — an app's ops sync like any repo's.
  branches/
    <branch>.db        # per-branch (or per-session) PROJECTION cache + WIP: name→hash table,
                       #   item bodies, dep graphs, file view, conflict list. DROP-able;
                       #   rebuilt by folding the relevant ops. The natural `.gitignore`.
  settings             # local-authoritative + private: cap grants, sync remotes, the
                       #   tailnet-login→account binding. A serialized Dark value / JSON blob
                       #   — NOT SQL (these are a handful of simple fields, bad as rows),
                       #   NOT synced, NOT derived. The one file a user backs up by hand.
```

Why this shape:

- **Ops are per-repo/per-app, coordinated by `core.db`.** Each app is effectively its own
  ops DB; the core store holds the central repo's ops plus the coordination state (which
  remotes, which apps are live). This mirrors the daemon topology — core daemon owns
  `core.db`, per-app daemons own their `apps/<app>.db` ([cli-daemon.md](cli-daemon.md)).
- **Projections are per-branch/session and disposable.** A branch switch re-points or
  rebuilds its cache; a session ends and its session-scoped projections evaporate. No
  projection is authoritative, so none can be "wrong" in a way needing distributed repair.
- **Settings are a blob, not a schema.** Cap grants and remote config are a few fields the
  user sets and reads — a serialized Dark value is simpler than a table and can't be
  mistaken for synced state.

**A projection declares three things**, nothing else couples it to the core: (1) which op
kinds it folds, (2) its scope (global/per-branch/per-session), (3) its invalidation trigger
(which incoming op kinds dirty it). On op arrival, mark dependent entries dirty; rebuild
lazily on next read (incremental view maintenance).

**The distribution race is a non-problem.** Two instances binding the same name to different
hashes is not two projections that disagree — the `name → hash` binding is a projection on
both sides, derived from each side's ops. What exists is two ops in the log; folding both on
one instance surfaces a `Name → two hashes` conflict the dispatch resolves. Recovery is
re-fold + dispatch over the ops, never a distributed lock on a projection cache.

## Event-streaming *is* sync

Event-streaming isn't a feature alongside sync — it **is** sync. Stream events, replay them,
detect conflicts (which may themselves be streams), compose. Solved with the least total
code, this is the path that eventually lets us **remove the `.dark` files from the repo**:
once the package corpus is just an op stream with projections, the checked-in files are
redundant. (That removal is punted until baseline sync + stability.)

## Replay — an op records the *result*, not the *intent to call*

Replay is "re-fold `apply` over the op stream," and several docs lean on it being
deterministic. That seems to clash with two facts — some bodies are nondeterministic LLM
calls, and the materialization event bus is **not durable**. One rule reconciles it:

> **An op records the *result*, not the *intent to call*.** When a materializer (LLM, corpus
> search, network fetch) produces a value, the op carries the produced value — already
> content-addressed. Replay folds that recorded value; it never re-invokes the producer.

So the materialization *bus* being ephemeral isn't a contradiction: the bus is a live
signal, but the **committed result** is an ordinary durable op. Live evaluation calls the
LLM; replay reuses the committed output. Effects work the same way — a `Promise` resolves
once, live; the resolved value lands in the op, so the re-fold is pure.

The one genuine exception is a **forever-lazy body never committed** — a fn whose body stays
a delegated LLM call, re-run every invocation. It can't replay deterministically (no recorded
result to fold). Mitigation: capture the producer's output in the trace, so replay of *that
run* folds the captured output while a fresh live run is free to differ. Determinism is a
property of folding recorded results, not of the producers that generated them.

## Convergence precondition: shared App logic

Replay-determinism handles the *result* of an op. A second, easy-to-miss precondition for two
*instances* to converge: they must fold the same op stream with the **same
`apply`/`conflict`/`resolve`**. Those functions are part of the `App`, which is editable and
forkable — so two instances could run different reconciliation logic and diverge.

This needs no new guard, because the App's functions are themselves **content-addressed
package items in the op stream**:

- "Same logic" = "same hashes." Two instances resolving from the same
  `apply`/`conflict`/`resolve` hashes fold to the same state — that *is* the convergence
  guarantee.
- A fork of the reconciliation logic isn't silent drift — it's a new hash, surfacing as an
  ordinary `Name → two hashes` conflict on the App's *own* definition. Divergence by fork is
  deliberate and visible, never accidental. Re-point to a shared hash to converge.

This is why App logic living *in* the synced stream (not as privileged F#) is load-bearing:
it makes "do we even agree on how to merge?" a first-class, inspectable conflict.

## Refactors are ops

A refactor — rename, move, extract, inline, change-signature — is **an op**, the same kind of
thing every other state change is. It falls straight out of the model: if the only way state
moves is `apply : 'op -> 'state -> 'state`, a rename is just a `Rename` op the agent emits
and the fold replays, no different in kind from `Inc`. Building refactors into the language
buys the event-sourced story for free: **they sync** (a refactor op rides the wire), **they
conflict and resolve** (rename-vs-edit is the op-vs-op case, mostly commutative + recorded),
and **they replay deterministically** (content-addressed, re-applied identically — no "re-run
the heuristic and hope"). The agent invokes refactors as ordinary moves; the human approves
or refines the resulting ops.

## Invariants and conflicts are the same machinery

`invariants` returns a `List<Violation>`; a violation folds into the same conflict model as
everything else. At-rest invariants are checked after the fold reaches rest, runtime
invariants during evaluation. A non-empty result is a `ConstraintViolated` conflict. Default
is **surface, not block** — the current violations are themselves a projection (the
"violations list," sibling of the conflict list) a human acts on later. Only an invariant
declared *hard* resolves to fail-loudly and blocks the breaching op. (Scriptorium is the
reference for how runtime/at-rest tests read and report.)

**Most conflicts are OK.** A conflict is just data — a condition we don't like and can get to
later. Surface it as a projection and keep running; only a small minority need eager
resolution.

## What's built on this (these reference down to here)

Crons are a distributed App we support (a tick is an op; subscribers run on each tick).
Daemons are Apps with a background loop ([cli-daemon.md](cli-daemon.md)). The proving ground
is rebuilding the current CLI experience as an App — the smallest system that replicates it
and solves sync along the way (this pulls in parser work: a `ref` keyword, a composable
Dark-written parser, `compile` as a builtin).

| Lower / sibling doc (pre-S&S) | Its role in this frame |
|---|---|
| [event-bus.md](event-bus.md) | the substrate that carries + replays ops |
| [async.md](async.md) | how a frame waits on a not-yet-resolved op |
| [composable-mvu.md](composable-mvu.md) | the msg/cmd UI loop layered on `apply`/`views` |
| [capabilities.md](capabilities.md) | what an App's ops may *do* as effects |
| [apps-surface.md](apps-surface.md) | `dark apps` — install/run shape of an App |
| [example-app.md](example-app.md) | an App where conflict/resolve/invariants do real work |

The higher layers — sync (move the op stream between instances), the conflict-resolution
dispatch (by evaluation-time), bootstrap, the view engine, the live/forkable app story —
build on this keystone and are specified in their own (higher-bucket) docs.

## Worked lens: the package system is just Apps over the op stream

The package system is not special — it's the clearest worked example of the four primitives.
There is no "stack of layers." Every annotation is an **op kind**; every catalog view is a
**projection**; a coherent bundle of the two is an **App**.

| Package concern | Ops appended | Projections folded out |
|---|---|---|
| body / definition | `AddFunction/Type/Value` | content-addressed store; the dependency graph |
| names | `SetName loc hash`, `Unbind` | name→hash resolution per branch |
| deprecation / harmful / stability | `Deprecate`, `Flag reason`, `SetStability` | latest-wins flagged set + a bus event on flag |
| descriptions | `SetDescription item path text` | rendered docs; a search index |
| tests / samples | `DeclareTest`, `DeclareSample` | reverse indexes ("what tests X?") |
| comments | `Comment`, `Reply`, `Resolve` | threaded tree per item |

Three consequences worth stating:

- **No shared table shape.** Deprecation wants latest-wins; comments want an append-only
  tree; dependencies want a doubly-indexed edge set. These aren't variations on one schema —
  they're different structures folding the same stream. A consumer reading several depends on
  all their shapes, openly; there's no `annotation_blob` to hide the coupling. **Dependencies
  are just one projection** (folded from bodies), not a foundational tier.
- **Runtime checks are subscribers, not core toggles.** `main` today bakes "harmful" into the
  core: `ExecutionState.allowHarmful : bool` + `fns.isHarmful` plumbing (RuntimeTypes.fs). The
  reframe: "harmful" is a `Flag` op stream; "refuse to invoke flagged code" is one Dark
  **subscriber** on the bus ([event-bus.md](event-bus.md)) that parks/refuses — and a runtime
  that doesn't care never subscribes. No core flag, no bespoke `PackageManager` query. Same
  machinery serves deprecation/stability warnings. (This is the path off `allowHarmful`.)
- **Declared vs inferred is two op sources, not two tiers.** An author's `DeclareEffects` and a
  background analysis stream feed two projections a consumer may read together; when they
  disagree, the disagreement is itself a projection — a conflict surfaced as data. Inferred
  facts carry `computed_at`/staleness because they fold a high-churn source; declared facts
  don't.

The meta-claim, held loosely but tested hard: capabilities, conflicts, event streams, and MVU
aren't four systems — they're one distributed event-sourced, branched-MVU system seen from
four angles.
