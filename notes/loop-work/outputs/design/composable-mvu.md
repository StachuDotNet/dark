# Composable MVU — the UI loop on top of `App`

The msg/cmd UI loop layered on the one thin `App`. This doc is the focused
treatment of **how UI intent becomes ops**, and how those ops fold back into the
state that views render. It sits directly on top of
[distributed-event-sourcing.md](distributed-event-sourcing.md) (the keystone, and
the home of distributed op-playback) and rides
[event-bus.md](event-bus.md) (the substrate that carries and replays ops).

> **May eventually flatten.** This is deliberately a satellite of
> [distributed-event-sourcing.md](distributed-event-sourcing.md). Once the MVU
> layer is settled, it likely folds *into* that doc — the distributed-op-playback
> home — rather than standing alone. Until then, keep it here as the
> "MVU-on-top-of-`App`" treatment so the keystone stays thin.

## The thing being composed is one big `App` — not a Model

The earlier framing of this doc composed *Models* (product/sum of sub-Models). The
correct unit of composition is the **`App` type** from
[distributed-event-sourcing.md](distributed-event-sourcing.md):

```fsharp
type App<'state, 'op> =
  { name       : String
    empty      : 'state                     // starting state
    apply      : 'op -> 'state -> 'state     // play ONE op back — the only way state moves
    conflict   : 'op -> 'op -> Bool          // do two concurrent ops clash?
    resolve    : 'op * 'op -> List<'op>      // reconcile a clash
    views      : 'state -> List<View>        // projections to render
    invariants : 'state -> List<Violation> } // at-rest / runtime constraints
```

Everything the system shows — viewers, editors, traces, the PDD-daemon UI,
eventually darklang.com — is **one composed `App`**. The PDD viewer, a SCM-branch
view, and a user's own program are not separate Models that the runtime
interleaves; they are facets of a single `App` whose `'op` is the sum of their op
types and whose `views` returns all their projections. Composition is composition
*of `App`s* — combine `apply` by op-variant dispatch, `views` by concatenation,
`conflict`/`resolve` by routing on op kind, `invariants` by union.

The split that has to stay clean (the **ops-vs-projections lens**):

- **Ops** are the durable, synced, replayable thing. State moves *only* by
  `apply`-ing an op. The op stream is canonical.
- **Projections** (`views`) are derived. Losing one costs only the CPU to
  re-fold. Nothing in `views` is authoritative; it is a read of folded state.

The MVU msg/cmd loop is the layer **on top** of this `App`. It does not own state;
it turns UI intent into ops that the `App` plays back.

## What the MVU layer adds — intent → op

The Elmish-shaped loop lives above the thin `App` core. Its single job: take a
user's intent and emit the op(s) that express it.

- **Msg** is UI intent, not durable state: a keypress, a selection, a timer tick,
  an event arriving from a bus.
- **The loop's `update`** is *not* `App.apply`. It is the **intent translator**:
  `Msg -> 'state -> List<'op>`. It reads current state, decides what the intent
  means, and emits ops. Those ops are the only thing that survives.
- **`App.apply`** then folds each emitted op into state. This is the one place
  state moves.
- **`App.views`** renders the new state. The renderer (see below) walks each
  `View` to a target.

So the loop is `Msg → (intent translator) → ops → App.apply → state → App.views →
render`. The msg/cmd half is ephemeral and local; the op half is durable and
synced. Cmds/effects — subscriptions, I/O, capability requests — are produced by
the intent layer and executed by the substrate, gated by
[capabilities.md](capabilities.md). They are kept *out* of the thin `App` core,
exactly as the keystone prescribes.

### Why the loop is a layer, not the core

Folding the intent translator into `apply` would put UI-specific, instance-local
decisions inside the thing that syncs. A keypress is not an op; "increment the
counter" is. Keeping them separate means the op stream stays portable across
instances while each instance's UI loop is free to interpret intent however its
local `view` and input model dictate.

## The runner — F#, Dark, or a combination

Something has to drive the loop: drain pending Msgs, run the intent translator,
fold the resulting ops through `apply`, render `views`, and pump effects. Call it
the **runner**.

The runner relates directly to **op-playback**: folding `apply` over the op stream
*is* replay. The runner that processes live ops and the replay that rebuilds state
from history are the **same fold** — one reads ops as they arrive, the other reads
them from the stored stream:

```
live   : ops arriving  ─ fold apply ─► state ─► views ─► render
replay : stored op log ─ fold apply ─► state ─► views ─► render
```

This identity is why the runner belongs in the same frame as
[distributed-event-sourcing.md](distributed-event-sourcing.md): the runner is just
op-playback with a live op source and a render sink attached.

Where the runner lives is a **F# / Dark / combination** question:

- **F# side (thin, principled).** The minimum the substrate owes the loop: drain
  the Msg queue, invoke the Dark-side intent translator, fold ops via `apply`,
  diff `views`, dispatch the resulting render to a target, and execute effects
  against the [event-bus.md](event-bus.md) and capability system. This is the same
  Ply-based scheduler the bus doc describes — the runner is a subscriber that also
  produces ops. Effects route through the bus; subscriptions are `waitForOne` /
  `subscribe` on the runtime buses.
- **Dark side.** The `App`s themselves, the intent translators, the `View` library
  and per-type default views, the composition helpers. The interesting, evolving
  code is Dark.
- **The combination.** The runner can be split: an F# core loop that owns the
  Ply scheduler and the render adapter, with the per-`App` intent translation and
  `views` evaluated as Dark. Or, as the language matures, more of the loop itself
  moves into Dark, with F# retreating to just the render-target adapters and the
  bus primitive. The seam is deliberately movable; the contract (drain → translate
  → `apply` → `views` → render) is stable regardless of which side hosts which
  step.

The runner has no privileged state of its own beyond the Msg queue and the
subscription wiring — the `App`'s `'state` is a projection of the op stream, so a
runner restart (or a fresh instance) rebuilds by replaying. That is the whole
point of putting playback and the runner under one roof.

## Where MVU meets the event bus

[event-bus.md](event-bus.md) is the **delivery** mechanism; the MVU loop is the
**consumption** mechanism. The bridge:

- A subscription is an effect the intent layer requests:
  `subscribe materialization (fun ev -> SomeMsg ev)`. The runner registers it on
  the runtime bus.
- The runner's scheduler delivers a matching emit as a Msg into the loop's queue.
- The intent translator turns that Msg into ops; `apply` folds them; `views`
  re-renders.

So an `App` never sees raw events — it sees Msgs the runner derived from bus
emits. And it never mutates state directly — it emits ops. The bus carries the
durable op events (`SyncOpArrived`, `ConflictResolved`, `Materialized`, …); the
MVU loop is one large subscriber whose rendered state is a **projection** of those
events, never a separate mutable store. This is the same statement the bus doc
makes about the viewer's Model — restated here as the general rule for any `App`.

## Default view per thing — the polymorphic-rendering bit

Every value has a default view and a default editor, so an `App` author rarely
hand-writes a card:

- `Stdlib.UI.view : 'T -> View` — specialized per type by users or the package
  store.
- Built-in types ship a view each (Int64 → number badge, String → quoted text,
  List → bulleted, Record → field rows).
- User types get a default derived from structure; override by publishing your own
  `view`.
- Language items render structurally: a fn shows its signature + body; a trace
  shows its event log; a `Pending` shows its state machine and last attempt.

This is the **view engine** the keystone defers to
[view-sketches.md](view-sketches.md). `App.views` returns `List<View>`; the engine
renders each. Writing a PDD viewer means calling `UI.view someFunction` and getting
the default fn-card, then customizing per context.

## The structured `View` and multi-target rendering

`views` returns a tree, not a string:

```dark
type View =
  | Text of String * Style
  | Row of List<View>
  | Column of List<View>
  | Bordered of View
  | KeyHints of List<(String * String)>
  | ScrollableList of List<View> * focused: Int64
  | Input of String * placeholder: String
  | Empty
```

One `View` tree, many renderers — terminal emits ANSI, web emits HTML, voice
narrates structure, reMarkable emits svg/pdf. The renderer is a substrate function
(an F# adapter at first, eventually Dark), not per-`App` code. This is the
target-independence that justifies the whole shape: the same composed `App`
renders anywhere.

## Mapping the PDD viewer onto this model

The spike's HTML view was hand-written F# with a global sink mutating a sessions
dict. The right version is the PDD viewer expressed as a facet of the composed
`App`:

- Its **ops** are the durable facts: a body materialized, a body changed, a
  conflict resolved — these arrive over the bus as the op stream.
- Its **Msgs** are UI intent: the user selected a fn, scrolled, toggled a pane.
- Its **intent translator** turns selection/scroll Msgs into local view ops, and
  turns bus-delivered Msgs (a `Materialized` arrived) into the ops that advance
  the per-fn state.
- Its **`views`** projects the current folded state into the fn-grid + event-log +
  dive-in panes.

The viewer subscribes to streams via effects; ops flow in; `apply` folds; `views`
re-renders. Hot-reloadable (swap `views` or the translator, keep the op stream;
see [hot-reload.md](hot-reload.md)). Replayable (re-fold the op
stream). The viewer is a Dark `App`, not F# code.

## Replay, traces, and time-travel — all one fold

Because state moves only by `apply`-ing ops, the op log *is* the trace, and replay
is re-folding:

```dark
let replay (app: App<'state, 'op>) (ops: List<'op>) : 'state =
  Stdlib.List.fold ops app.empty (fun state op -> app.apply op state)
```

This is identical to what the runner does on live ops — the only difference is the
op source. Time-travel debugging falls out: pause, scrub, replay-to-point are all
slices of the same fold over the op log. Diffing two runs is aligning two op
sequences and showing state divergence per step. "Each eval is separately
debuggable as it goes" becomes concrete: the eval *is* a sequence of ops folded
into state.

Note the subtlety the keystone cares about: replaying the same ops through a
*changed* `apply` (the author refined it since the ops were recorded) may produce a
different state — which is a feature for asking "would my new `apply` have handled
this stream better?", and a hazard for naive caching. It is the playback story, so
it is settled in [distributed-event-sourcing.md](distributed-event-sourcing.md).

## How this lands against existing main code

Main already ships a folded MVU framework in Dark at `packages/darklang/cli/`:
`SubApp` (with `onKey` / `onDisplay` / `onSave`), `AppState`, a `Page` sum, and
real apps in `apps/` (outliner, review, views). That is the proven starting point.
The evolution toward the composed-`App` shape is incremental and does not require
migrating every app at once:

| Today on main | Toward composed `App` |
|---|---|
| `SubApp.onKey: Key -> ... -> SubAppAction * SubApp` | intent translator `Msg -> 'state -> List<'op>`; key events wrap into a `KeyPressed` Msg |
| `SubApp.onDisplay: Unit -> String` | `App.views: 'state -> List<View>` (structured tree) |
| `SubApp.onSave: Unit -> Unit` | an op + effect, folded like any other |
| `Page = … | SubApp` (one slot) | one composed `App`; facets routed by op kind + concatenated `views` |
| Terminal-only rendering | multi-target via the `View` tree + substrate renderers |
| No event subscriptions | subscriptions as effects on the runtime buses (see [event-bus.md](event-bus.md)) |
| No Msg / op log | ops are the durable log; replay is re-fold (see [distributed-event-sourcing.md](distributed-event-sourcing.md)) |

The migration is mechanical: introduce the op type, wrap key handling into Msgs,
move the intent decision out of `onKey` into the translator, fold via `apply`,
grow the `View` tree per app over time, add subscriptions and effects as new
fields defaulting to empty.

## Open questions

- **Render-target abstraction.** The `View` tree's exact node set and where the
  per-target renderer lives (F# adapter vs Dark) — converges with
  [view-sketches.md](view-sketches.md).
- **Effects discipline.** Effects (especially `Exec`, `subscribe`) are
  capability-gated; routes through [capabilities.md](capabilities.md).
- **Runner placement.** How much of the runner stays F# vs moves to Dark, and the
  exact seam — tracks the async-model decision in
  [event-bus.md](event-bus.md) / `design/async.md`.
- **Real-time collaboration.** Two clients' intent translators emitting ops into
  one shared stream is just concurrent ops — handled by `conflict`/`resolve` in
  [conflicts.md](conflicts.md).
- **Replay through a changed `apply`.** Semantics of re-folding old ops through a
  refined `apply` — owned by [distributed-event-sourcing.md](distributed-event-sourcing.md).
- **`Stdlib.UI` content.** Which primitives ship vs are user-built; likely layout
  primitives + per-builtin-type defaults, with users adding per-user-type views.

## Pitch in one sentence

The PDD viewer, the trace inspector, the SCM merge UI, and darklang.com's package
browser are facets of **one composed `App`** — its ops sync and replay, its
`views` project, and a thin MVU loop turns UI intent into those ops — so the
runner that plays ops live is the same fold that replays them, and every app the
language ever needs falls out of building that substrate once.
