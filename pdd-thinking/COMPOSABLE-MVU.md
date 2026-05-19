# Composable MVU

The fifth substrate piece. **What apps look like** in this system —
viewers, editors, traces, the PDD-daemon UI, eventually
darklang.com itself.

Vault material is thin and old. The relevant bits: user wants
"elm-beautiful" TUI FRPs in Darklang, with F# knowing about the
abstractions (modeled in `ProgramTypes`); each "thing" (fn, type,
value, trace, prompt) should have a *default* nested view + edit;
users can override per context; hot-reloadable. Synthesizing from
there.

## Why MVU as the substrate

Pure functions of state. Same shape regardless of where rendered
(TUI, web, VS Code webview). Composable by construction.
Deterministic — every state transition is a `(Model, Msg) →
Model`, so replay is free.

Three properties that matter here:

- **Pairs with traces.** A trace is a `List<Msg>`. Replaying a
  trace = re-applying Msgs to the initial Model. Diffing two
  traces = aligning their Msg sequences and showing
  Model-divergence per step. The trace and the app are the same
  shape.
- **Pairs with hot-reload.** Swap the `update` or `view` fn,
  keep the Model. The new behavior takes effect immediately; the
  user's navigation state survives. (See `HOT-RELOAD.md`.)
- **Pairs with event streams.** Msgs come from the event bus
  (B4). When `Materialized name body` fires, that's a Msg the
  viewer's update consumes.

## The shape

```dark
type App<'Model, 'Msg> = {
  init    : unit -> 'Model
  update  : 'Msg -> 'Model -> 'Model
  view    : 'Model -> View    // View is the renderable thing
  effects : 'Model -> List<Effect>   // I/O, subscriptions, etc.
}
```

Standard Elm/Bonsai shape. `effects` is the bridge to the
event bus and capability system — an Effect is something the
runtime executes (HTTP call, materialization request,
subscription to a stream).

## Composable — apps nest

Apps compose via product/sum:

- **Model** composes by record/struct: `type Composed = { left: A.Model; right: B.Model }`
- **Msg** composes by variant: `type ComposedMsg = | LeftMsg of A.Msg | RightMsg of B.Msg`
- **update** dispatches: `match msg with | LeftMsg m -> { state with left = A.update m state.left } | ...`
- **view** assembles: `H.row [ A.view state.left; B.view state.right ]`
- **effects** interleaves: produce both sub-apps' effects, tagged

This is mechanical and well-trodden. The interesting part is
making it pleasant to write in Dark.

## "Default view per thing" — the polymorphic-rendering bit

The old composable-UI note's right intuition: **every value has a
default view, default editor**. Concretely:

- `Stdlib.UI.view : 'T -> View` — polymorphic-ish. Specialized
  per type by users or by the package store.
- For built-in types: the language ships a view per primitive
  (Int64 → number badge, String → quoted-text, List → bulleted,
  Record → field rows, etc.)
- For user types: a default derived from the type structure;
  user can override by providing their own `view` impl.
- For language items: a fn shows its sig + body + a "materialize"
  button if Pending; a trace shows its event log; a Pending
  shows its state machine + last-attempt info.

So when you write a PDD viewer, you don't write a fn-card from
scratch — you call `UI.view someFunction` and the default
fn-card renders. Customize per context (e.g. the trace-replay
viewer wants a different fn-card with frame-state highlights).
The polymorphism is package-store-driven; users can publish
their own views.

## Mapping the spike's PDD viewer to this model

The spike's HTML view: hand-written F#, single global EventSink,
mutates a sessions dict and rewrites the HTML each event.

The right version:

```dark
type ViewerModel = {
  inFocus      : FQFnName              // top-level fn the user prompted for
  fnStates     : Dict<FQFnName, FnState>   // ⋯/✓/▼/↻/✗ per fn
  events       : List<EventEntry>       // chronological log
  selectedFn   : Option<FQFnName>       // for the dive-in panel
}

type ViewerMsg =
  | Materialized of FQFnName * Body
  | MaterializeStarted of FQFnName
  | Failed of FQFnName * reason: String
  | UserSelected of FQFnName
  | BodyChanged of FQFnName * newHash: Hash
  // ...

let update msg model = ...
let view model = ...
let effects model =
  [ Subscribe RT.Streams.materialization (fun ev -> Materialized(ev.name, ev.body))
    Subscribe RT.Streams.bodyChanged (fun ev -> BodyChanged(ev.name, ev.hash))
    ... ]
```

The viewer subscribes to streams via the `effects` channel; Msgs
flow in; Model updates; views re-render. Hot-reloadable. Trace-
replayable. **The viewer is a Dark app, not F# code.**

## F# substrate vs Dark composition

The F# substrate provides a small, principled runtime:

- The Elmish loop: pick a Msg, run `update`, diff the View
- Effects executor: dispatch Effects to the right subsystem
  (HTTP, stream-subscribe, etc.)
- View → render-target adapter (TUI, web, webview)
- Model persistence (for app restart with state)

Maybe 500 LoC of F#. Everything else is Dark.

Dark provides:

- The apps themselves
- The View library (`Stdlib.UI`)
- Polymorphic default views per type
- Composition helpers (rows, columns, panels, dive-in panels)
- Standard effects (subscribe, materialize, capability-request)

## Connection to traces — Msg log IS a trace

The trace and the Msg-log are the same artifact, just consumed
differently:

```
trace ──── playback ────► Model evolution
Msg log ── playback ────► Model evolution
```

When the user "replays" a trace, they're feeding the Msgs back
into `update` and watching the Model evolve. Diffing two traces
is diffing two Msg sequences and showing Model divergence. The
replay scrubber is just a slider over the Msg log.

This makes "each eval is separately debuggable as it goes"
(from FRONTIER) concrete: the eval *is* a sequence of Msgs into
the viewer's Model; you can pause, rewind, replay any time.

## Connection to events (B4)

MVU Msgs and event-bus events are isomorphic. The bridge:

- A subscription is an Effect: `Subscribe stream (fun ev -> SomeMsg ev)`
- The Elmish loop polls subscribed streams between Msgs
- An event emit on a subscribed stream becomes a Msg in the
  app's queue

So the event bus is the *delivery mechanism*; MVU is the
*consumption mechanism*. Apps don't see events directly — they
see Msgs.

## Multiple concurrent apps

Open question from the bucket: how do multiple apps compose at
the runtime level? The PDD viewer + a SCM-branch view + the
user's own app may all want to be live at once.

Two answers:

- **Separate Models, one process.** Each app has its own Model;
  they don't share state. The runtime maintains a list of
  active apps; the Elmish loop interleaves their updates. The
  user picks which app to show (tabs, split-pane). This is
  probably right for v1.
- **One big composed Model.** Everything is sub-app under a root
  app. The root's Model has `viewer`, `branchView`, `userApp`
  fields. Composition all the way down. More elegant; more
  rigid; harder to dynamically add an app.

Probably a hybrid: a thin root with a `List<RunningApp>` field,
each `RunningApp` boxes its own typed Model/Msg/etc. Add/remove
apps by mutating the list. Composable when you want it; flexible
when you need it.

## What this unlocks

- The PDD viewer becomes a Dark program (FRONTIER goal)
- The trace inspector is the same shape — just a different
  default view over the same Msg log
- The SCM diff/merge UI from `flows/conflict-resolution.md` is
  an app — same composition pattern
- Users can write their *own* apps and share them like packages
- Hot-reload of UIs (per HOT-RELOAD.md) works for free —
  re-load `view` or `update`, the Model survives
- Polymorphic views mean adding a new type comes with a working
  view "for free" (derived) until you write a custom one

## Open questions

- **Render target abstraction**: a `View` should render to TUI,
  HTML, native widget. The F# adapter does this; what's the
  language-level abstraction? Probably a tree of typed nodes
  (`Row | Column | Text | Input | Button | ...`) with target-
  specific renderers in F# at first, eventually in Dark.
- **Effects discipline**: Effects need to be capability-gated
  (you can't subscribe to a stream you don't have caps for).
  Routes through B5.
- **Trace replay semantics**: replaying a trace through an app
  with a *different* `update` fn (e.g., the user refined the
  update fn since the trace was recorded) — does that produce
  a different Model? Probably yes; that's a feature for testing
  "would my new update fn have handled this trace better?"
- **Real-time collaboration**: two users with the same Model;
  Msgs from either flow into the shared Model. Standard
  optimistic-CRDT-with-conflict-resolution territory. Routes
  through B2.
- **State persistence**: the Model survives restart? Survives
  hot-reload? Routes through HOT-RELOAD.md's contract.
- **`Stdlib.UI` content**: what primitives ship vs what's user-
  built? Probably ships the layout primitives + per-type
  defaults for builtin types; users add per-user-type views.

## Compared to existing systems

- **Elm**: closest reference. Same `init/update/view` shape.
  Differences: our effects integrate with capabilities; our
  Msg-log is the trace; our views are polymorphic-per-type by
  default.
- **Bonsai (Jane Street)**: arguably closer — explicitly
  composable; treats each component as a value. Worth a deeper
  look when implementing.
- **React**: not really MVU; implicit state; Hooks added later
  but the model leaks. We're not React-shape.
- **SwiftUI / Jetpack Compose**: declarative views like ours
  but with implicit state mgmt. We keep state explicit (in
  the Model).

## Pitch in one sentence

**The PDD viewer, the trace inspector, the SCM merge UI, and
darklang.com's package browser are all the same kind of program:
a Dark MVU app subscribing to event streams, with polymorphic
default views, hot-reloadable, trace-replayable. Build the
substrate once; every app the language ever needs falls out.**
