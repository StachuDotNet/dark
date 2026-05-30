# Composable MVU — the UI loop on top of `App`

The msg/cmd UI loop layered on the one thin `App`: **how UI intent becomes ops**, and how
those ops fold back into the state that views render. It sits directly on
[distributed-event-sourcing.md](distributed-event-sourcing.md) (the keystone, home of
distributed op-playback) and rides [event-bus.md](event-bus.md) (which carries + replays
ops). A satellite of the keystone — once settled it likely folds *into* it; kept separate
for now so the keystone stays thin.

## The unit of composition is one big `App`, not a Model

The thing being composed is the **`App`** from the keystone — not a tree of sub-Models:

```fsharp
type App<'state, 'op> =
  { name; init; apply; conflict; resolve; views; invariants }   // full shape in the keystone
```

Everything the system shows — viewers, editors, traces, eventually darklang.com — is **one
composed `App`** whose `'op` is the sum of its facets' op types and whose `views` returns all
their projections. Compose `apply` by op-variant dispatch, `views` by concatenation,
`conflict`/`resolve` by routing on op kind, `invariants` by union. The ops-vs-projections
split holds: **ops** are durable/synced/replayable (state moves *only* by `apply`-ing one);
**`views`** are derived reads, never authoritative.

## What the MVU layer adds — intent → op

The Elmish-shaped loop lives above the thin `App` core. Its one job: turn a user's intent
into the op(s) that express it.

- **Msg** is UI intent, not durable state: a keypress, a selection, a timer tick, a
  bus-delivered event.
- **The loop's `update` is NOT `App.apply`.** It's the **intent translator**
  `Msg -> 'state -> List<'op>`: read current state, decide what the intent means, emit ops.
  Those ops are the only thing that survives.
- **`App.apply`** folds each emitted op into state — the one place state moves.
- **`App.views`** renders the new state.

```
Msg ─► intent translator ─► ops ─► App.apply ─► state ─► App.views ─► render
        (ephemeral, local)        (durable, synced)
```

Folding the translator into `apply` would put instance-local UI decisions inside the thing
that syncs. A keypress is not an op; "increment the counter" is. Keeping them separate keeps
the op stream portable while each instance interprets intent however its local input model
dictates. Cmds/effects (subscriptions, I/O, capability requests) are produced by the intent
layer and executed by the substrate, gated by [capabilities.md](capabilities.md) — out of
the thin core, as the keystone prescribes.

## The runner — and why it *is* op-playback

Something drives the loop: drain Msgs, run the translator, fold ops via `apply`, render
`views`, pump effects. That **runner** is just op-playback with a live source and a render
sink — the identical fold the keystone uses for replay, differing only in where ops come from:

```
live   : ops arriving  ─ fold apply ─► state ─► views ─► render
replay : stored op log ─ fold apply ─► state ─► views ─► render
```

```dark
let run (app: App<'state, 'op>) (ops: List<'op>) : 'state =
  Stdlib.List.fold ops app.init (fun state op -> app.apply op state)   // live or replay
```

So time-travel debugging falls out for free: pause, scrub, replay-to-point are slices of the
same fold; diffing two runs is aligning two op sequences. Where the runner *lives* is a
F#/Dark seam:

- **F# (thin):** drain the Msg queue, invoke the Dark translator, fold ops, diff `views`,
  dispatch render, execute effects against the bus + capability system. This is the same
  Ply scheduler [event-bus.md](event-bus.md) describes — the runner is a subscriber that
  also produces ops.
- **Dark:** the `App`s, the intent translators, the `View` library + default views, the
  composition helpers — the interesting, evolving code.
- The seam is movable; the contract (drain → translate → `apply` → `views` → render) is
  stable regardless of which side hosts which step. The runner holds no privileged state
  beyond the Msg queue and subscriptions — `'state` is a projection, so a restart rebuilds
  by replay.

## Where MVU meets the event bus

The bus is **delivery**; the MVU loop is **consumption**. A subscription is an effect the
intent layer requests (`subscribe materialization (fun ev -> SomeMsg ev)`); the runner
registers it on the runtime bus; a matching emit arrives as a Msg; the translator turns it
into ops. So an `App` never sees raw events (only runner-derived Msgs) and never mutates
state (only emits ops). The MVU loop is one large bus subscriber whose rendered state is a
projection of bus events, never a separate store.

## Default view per thing, and the structured `View`

Every value has a default view so an `App` author rarely hand-writes a card:
`Stdlib.UI.view : 'T -> View`, specialized per type. Built-ins ship one each (Int64 → number
badge, List → bulleted, Record → field rows); user types get a structure-derived default,
overridable by publishing your own `view`; language items render structurally (a fn shows
signature + body). `views` returns a **tree**, not a string:

```dark
type View =
  | Text of String * Style
  | Row of List<View>      | Column of List<View>
  | Bordered of View       | KeyHints of List<(String * String)>
  | ScrollableList of List<View> * focused: Int64
  | Input of String * placeholder: String
  | Empty
```

One tree, many renderers — terminal emits ANSI, web emits HTML, voice narrates, reMarkable
emits pdf. The renderer is a substrate function (F# adapter first, eventually Dark), not
per-`App` code. That target-independence is what justifies the whole shape.

## Is this structure reasonable? — versus mature MVU

Worth checking the core shape against systems that have lived a long time, because "novel
architecture" is a risk, not a feature.

| System | What it has | What we add / differ |
|---|---|---|
| **Elm / Elmish** | `init`, `update : msg -> model -> model`, `view`, `Cmd` for effects | We **split `update` in two**: the intent translator (`Msg -> state -> List<op>`, local) and `apply` (`op -> state -> state`, synced). Elm has no op layer — its `update` is both, so nothing is portable across instances. |
| **Redux** | actions, a pure reducer, a single store | Our `op` ≈ a Redux action that is *also persisted + synced*; `apply` ≈ the reducer. Redux's "time-travel via action log" is exactly our replay — we make the log canonical and distributed. |
| **F# MVU (Elmish.WPF/Bolero)** | the Elm loop hosted in F#, `view` to a retained tree | Our `View` tree + multi-target renderer is the same idea; our runner is the same loop, but its fold doubles as event-sourced replay. |
| **Event sourcing (CQRS)** | events as the source of truth, projections as read models | This is our `op` stream + `views`. We add the **MVU loop on top** so the same events drive a live UI, and **`conflict`/`resolve` on the App** so projections converge across instances. |

The reassuring read: every piece has a long-lived precedent — we're recombining Elm's loop
with event sourcing's log, not inventing a new paradigm. **The one genuinely novel claim is
that `conflict`/`resolve` live on the `App` so distributed op-playback converges** — and
that's exactly the part the keystone leaves open (conflict-carrying vs conflict-blind) and
the first real App must prove. Everything else is well-trodden. Where it could still need
refinement: composing `apply` across many facets may want a real effect/subscription algebra
(Elm needed `Cmd`/`Sub` for this), and the `View` diffing story needs the same keyed
reconciliation Elm/React learned the hard way. Neither is novel; both are work.

## Mapping the outliner onto this model (the worked focus)

`main` already ships the outliner at `packages/darklang/cli/apps/outliner/`: `core.dark`,
`outline-editor.dark`, `text-editor.dark`, `list-picker.dark`, `markdown.dark`,
`export.dark`, wrapped by `app.dark` into a `SubApp` whose `onKey` returns
`Continue|Save|Exit`. It's a real, non-trivial app — the right thing to bring into this
world (and once it's here, `print-md` is easy: a tiny App reusing the same markdown +
export path).

```
outliner as a composed App:
  'op   = NodeAdded id parent | NodeEdited id text | NodeMoved id newParent | NodeDeleted id
          | Exported format          (durable facts — these sync + replay)
  Msg   = KeyPressed key | Selected id | Scrolled n | ToggleExport   (UI intent — ephemeral)
  init  = empty outline
  apply = fold a NodeXxx op into the tree
  translate (the intent layer):
    KeyPressed Enter   on node n   ->  [ NodeAdded (fresh()) (parentOf n) ]
    KeyPressed (Char c) editing n  ->  [ NodeEdited n (insert c) ]
    ToggleExport                   ->  [ Exported Markdown ]      // reuses markdown.dark
  views = outline pane (ScrollableList of nodes) + text-editor pane (Input) + key hints
```

The migration from today's `SubApp` is mechanical: the `onKey` body that returns
`Continue/Save/Exit` splits into the **intent translator** (decide → emit ops) plus
**`apply`** (fold ops); `onDisplay : Unit -> String` becomes `views : state -> List<View>`
(structured tree); a save stops being `onSave : Unit -> Unit` and becomes just another op.
Then it gains, for free: **sync** (the `NodeXxx` ops ride the wire), **replay** (re-fold the
op log), **hot-reload** (swap `views`/translator, keep the op stream), and **`print-md`** as
a sibling App over the same export op.

| Today on main (`SubApp`) | Toward composed `App` |
|---|---|
| `onKey : Key -> … -> SubAppAction * SubApp` | intent translator `Msg -> state -> List<op>`; keys wrap into `KeyPressed` |
| `onDisplay : Unit -> String` | `views : state -> List<View>` (structured tree) |
| `onSave : Unit -> Unit` | a `Save`/`Exported` op, folded like any other |
| `Page = … \| SubApp` (one slot) | one composed `App`; facets routed by op kind |
| terminal-only render | multi-target via the `View` tree |
| no subscriptions / no op log | subscriptions as bus effects; ops are the durable log |

## Open questions

- **Effect/subscription algebra.** Composing effects across facets likely needs an
  Elm-`Cmd`/`Sub`-style structure; effects are capability-gated
  ([capabilities.md](capabilities.md)).
- **`View` diffing.** Keyed reconciliation for the tree (the lesson Elm/React paid for).
- **Conflict placement.** Conflict-carrying (`App`) vs conflict-blind (projection) — the
  keystone's open question; the first real App (the outliner) decides it.
- **`Stdlib.UI` content.** Which primitives ship vs are user-built — likely layout primitives
  + per-builtin-type defaults, users adding views for their own types.
- **Real-time collaboration.** Two clients' translators emitting into one op stream is just
  concurrent ops — handled by `conflict`/`resolve`.

## Pitch in one sentence

The outliner, `print-md`, the trace inspector, and darklang.com's package browser are facets
of **one composed `App`** — its ops sync and replay, its `views` project, and a thin MVU loop
turns UI intent into those ops — so the runner that plays ops live is the same fold that
replays them, and every app the language needs falls out of building that substrate once.
