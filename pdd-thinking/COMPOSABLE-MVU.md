# Composable MVU

> **v0 design — deepened from sketch via loop T18 (2026-05-20).**
> Big finding: main **already has a SubApp/MVU framework in Dark**
> at `packages/darklang/cli/`. Confirmed via `git show
> main:packages/darklang/cli/core.dark`. The substrate work is
> evolving the existing shape — adding Msg type + subscriptions
> + Effects + composition — not building from scratch.

The fifth substrate piece. **What apps look like** in this system —
viewers, editors, traces, the PDD-daemon UI, eventually
darklang.com itself.

## What exists on main today

From the 30-sec main check:

### `SubApp` is already a real type

```dark
// packages/darklang/cli/core.dark
type SubAppAction = Continue | Save | Exit

type SubApp =
  { onKey: Key -> Modifiers -> Option<String> -> (SubAppAction * SubApp)
    onDisplay: Unit -> String
    onSave: Unit -> Unit }
```

This is **already an MVU framework**, just folded:
- `onKey` is the update fn — except input is a key, not a Msg sum
- `onDisplay` is the view fn — returns a String (terminal output)
- `onSave` is one specific Effect
- `SubAppAction` is the result of update: Continue with new state,
  trigger Save effect, or Exit

### `AppState` is the model of the top-level CLI shell

```dark
type AppState =
  { isExiting: Bool
    prompt: Prompt.State              // sub-state for the prompt input
    needsFullRedraw: Bool
    packageData: Packages.State       // sub-state for package browsing
    currentPage: Page                 // which "page" is in focus
    currentBranchId: Uuid             // SCM context
    accountID: Option<Uuid>           // identity (per IDENTITY.md)
    accountName: String
    previousRenderedRows: Int64       // render bookkeeping
    nonInteractive: Bool
    cachedHintInput, cachedHintValue: String     // caches
    allCommandsCache, commandOptionsCache: ...
    cachedStatusBar, cachedStatusBarBranch: ...
    telemetryEnabled: Bool }

type Page =
  | InteractiveNav of Packages.NavInteractive.State
  | CompletionPicker of CompletionPicker.State
  | SubApp of SubApp                   // delegated to a child app
```

`Page` is a sum — multiple "kinds of screen" already supported.
`SubApp` is one variant, so the host's update-fn delegates key
events to the active SubApp.

### Apps already exist

`packages/darklang/cli/apps/` directory contains:

- **`outliner/app.dark`** — interactive tree outliner. Full
  Model + onKey + render. Per-document persistence.
- **`review/app.dark`** — review interface for SCM patches.
- **`views/app.dark`** — view selector for browsing.

Each app:
- Has its own State module (`Main.State`)
- Has its own action sum (`Continue s | Save s | Exit s`)
- Has its own `handleKey` (the update fn)
- Wraps itself in `makeSubApp` to be a `SubApp` value the host
  can run

This is mature. The framework is **proven**, not theoretical.

### What's *not* on main yet

- A separate `Msg` type per app (today, keys go directly into
  onKey; Msgs would be more general — KeyPress | TimerTick |
  EventFromBus | UserSelection).
- An `Effects` channel for general I/O (today: save is the only
  built-in effect; others would require leaking out through
  global mutation).
- Subscriptions to EventBuses (T16 work). Apps can't yet listen
  for "materialization-done" events.
- Composition primitives beyond `Page` variants. Sub-apps as
  *values that compose by product/sum* (vs as one-of-three Page
  variants) — would let any app embed any other.
- Trace replay (Msg log replay) — there's no Msg log because
  there's no Msg type.
- Cross-target rendering (today: terminal only; web/HTML/voice
  would need a View abstraction).

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

## Evolution path — from SubApp to full composable MVU

The existing SubApp framework is the **right starting point**.
Evolution is incremental:

### Step 1 — Add `Msg` as a type alongside Key events

```dark
type AppMsg =
  | KeyPressed of Key * Modifiers * Option<String>
  | TimerTick of Stdlib.DateTime
  | EventArrived of Bus.Name * Bus.Event
  | UserAction of String          // generic for future expansion
  | CompletionDone

// SubApp evolves to:
type SubApp =
  { onMsg: AppMsg -> (SubAppAction * SubApp)        // generalized from onKey
    onDisplay: Unit -> View                          // returns structured View, not String
    subscribes: List<Subscription> }                 // what events to receive
```

This is **backward-compatible**: existing apps wrap key events
into `KeyPressed` Msgs. New apps can dispatch on Msg variants
directly.

### Step 2 — Structured `View`

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

A View tree renders to different targets:
- Terminal: walks the tree, emits ANSI
- Web (later): walks the tree, emits HTML
- Voice (later): walks the tree, narrates structure
- reMarkable (later): walks the tree, emits svg/pdf

Same `View` tree, different renderer. The renderer is a
substrate function, not per-app code.

### Step 3 — Subscriptions to EventBuses (T16)

```dark
type Subscription =
  | OnBus of Bus.Name * (Bus.Event -> AppMsg)
  | OnTimer of intervalMs: Int64 * (Stdlib.DateTime -> AppMsg)
  | OnExternal of waitOn: WaitOn * (Result<'a, String> -> AppMsg)

// In the viewer:
let subscribes = [
  OnBus "materialization" (fun ev ->
    EventArrived ("materialization", ev))
  OnBus "bodyChanged" (fun ev ->
    EventArrived ("bodyChanged", ev))
  OnTimer 1000L (fun t -> TimerTick t)
]
```

The CLI host loop runs subscriptions in the background and
delivers their resulting Msgs to the active SubApp. Connects
directly to EventBus from T16.

### Step 4 — Effects channel

Today's `onSave` is one effect. Generalize:

```dark
type Effect =
  | SaveState of bytes: Bytes
  | PublishToBus of name: Bus.Name * event: Bus.Event
  | SubscribeTo of Subscription
  | Spawn of subApp: SubApp                    // sub-app composition
  | Exec of cmd: String * args: List<String>   // capability-gated
  | None_                                       // no effect

type SubApp =
  { onMsg: AppMsg -> (SubAppAction * SubApp * List<Effect>)
    onDisplay: Unit -> View
    subscribes: List<Subscription> }
```

The host runs effects per the granted caps (per CAPABILITIES);
some effects (e.g., `Exec`) need explicit grants and route
through the conflict dispatch.

### Step 5 — Real composition

Today: SubApp is a leaf — one of Page variants. With Step 4's
`Spawn` effect, an app can embed another:

```dark
type DiffViewer.Model = {
  left: ContentViewer.Model
  right: ContentViewer.Model
  highlighted: Set<Int64>
}

let update msg model =
  match msg with
  | LeftSubMsg sub ->
    let (action, newLeft, fx) = ContentViewer.update sub model.left
    (action, { model with left = newLeft }, mapEffects LeftSubMsg fx)
  | RightSubMsg sub -> ...
  | ...
```

Standard Elm composition. Sub-apps are first-class. Diff viewer
contains two content viewers; trace inspector contains a diff
viewer + a sidebar; etc.

### Step 6 — Trace replay

Once Msgs exist, the Msg log per session is the trace. Replaying
a trace = re-feeding Msgs to a fresh model:

```dark
let replay (initialModel: Model) (msgs: List<Msg>) : Model =
  Stdlib.List.fold msgs initialModel (fun m msg ->
    let (_action, m', _fx) = update msg m
    m')
```

Time-travel debugging falls out: pause, scrub, replay-to-point.

## F# substrate sketch (~500 LoC)

What the F# side provides for the substrate (Dark apps don't see this):

```fsharp
// LibExecution.Mvu (new module)

/// The runtime loop. Picks the active SubApp; routes Msgs from
/// subscriptions + user input + bus events to its onMsg.
type Loop = {
  activeApp : SubApp
  msgQueue : Queue<AppMsg>
  subscriptions : List<ActiveSubscription>
  renderTarget : RenderTarget
}

/// One iteration: drain queue, apply Msgs, render if changed.
let tick (loop : Loop) : Ply<Loop> = uply {
  if loop.msgQueue.IsEmpty then
    return loop
  else
    let msg = loop.msgQueue.Dequeue()
    let (action, newApp, effects) = invokeOnMsg loop.activeApp msg
    let! loop' = applyEffects loop effects        // can grow queue, spawn subs
    match action with
    | Continue -> return { loop' with activeApp = newApp }
    | Save -> ... // run onSave; persist Model
    | Exit -> ... // teardown
}

/// Hook subscriptions to EventBuses (T16).
let activateSubscription (sub : Subscription) (loop : Loop) : Ply<Loop> = ...

/// Effects executor.
let applyEffects (loop : Loop) (effects : List<Effect>) : Ply<Loop> = ...

/// Renderer per RenderTarget.
type RenderTarget =
  | Terminal of ITerminal
  | Web of IWebSocket            // later
  | VsCodeWebview of IPipe       // later
```

Stays small. Most logic is Dark-side; the F# substrate just
runs the loop + executes effects.

## How this lands against existing main code

| Today on main | After this evolution |
|---|---|
| `SubApp.onKey: Key -> Modifiers -> ... -> SubAppAction * SubApp` | `SubApp.onMsg: Msg -> ... -> SubAppAction * SubApp * List<Effect>` |
| `SubApp.onDisplay: Unit -> String` | `SubApp.onDisplay: Unit -> View` |
| `SubApp.onSave: Unit -> Unit` | (subsumed by Effects.SaveState) |
| `Page = ... | SubApp` (one slot) | Multiple sub-apps composable via Effects.Spawn + product-Models |
| Apps in `apps/` (outliner, review, views) | Same apps + new ones (viewer, trace-inspector, conflict-merge, agent-watcher) |
| Terminal-only rendering | Multi-target via View tree |
| No event subscriptions | Subscriptions to EventBuses (T16) |

The migration is mechanical:

1. Add Msg type to each existing app
2. Wrap key handling in a `KeyPressed` Msg variant
3. Change onKey signature to onMsg (backward-compat shim wraps
   keys)
4. Introduce View tree gradually (start: a View.Text wrapping
   the existing String output; refactor per app over time)
5. Add subscription support (new field, defaults to empty list)
6. Add Effects (new return value, defaults to empty list)

None of this requires migrating all apps at once. Each app moves
when convenient.

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
