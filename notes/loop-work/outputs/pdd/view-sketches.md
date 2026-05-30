# View Sketches — In-Focus-Fn Experience

High-level visual brief for the recursive-live-development viewer.
**Not committal on tech.** Sketches at multiple moments to convey
shape; whoever builds the viewer takes these as inputs, not
specifications.

The viewer is one MVU app (see [composable-mvu.md](../pre-s-and-s/composable-mvu.md))
subscribed to the event bus (see [event-bus.md](../pre-s-and-s/event-bus.md)),
surfacing the in-focus fn as it gets shaped.

## Layout principle

Three regions, always present:

```
┌─────────────────────────────────────────────────────────────┐
│ TOP STRIP                                                   │
│ Prompt / In-focus fn name / Run controls / Cap requests     │
├────────────────────────────────────┬────────────────────────┤
│ FOCUS PANEL                        │ DIVE-IN PANEL          │
│                                    │                        │
│ Whatever the user is looking at:   │ Optional. When user    │
│ - the top-level fn skeleton, or    │ clicks a sub-fn or     │
│ - a sub-fn detail, or              │ event, this panel      │
│ - a trace replay scrub, or         │ shows its detail.      │
│ - the materialize-attempts view    │ Stacks: can dive in    │
│                                    │ multiple levels.       │
│                                    │                        │
├────────────────────────────────────┴────────────────────────┤
│ EVENT STREAM (timeline)                                     │
│ Each event with timestamp, kind, target. Scrubbable.        │
└─────────────────────────────────────────────────────────────┘
```

The user always sees: where they are, what's happening, and
recent history. The dive-in pattern means "click anything, get
more detail, without leaving where you were."

## t=0 — just after the user typed a prompt

```
┌─────────────────────────────────────────────────────────────┐
│ Prompt: "build a CSV pipeline that finds rows where the     │
│          variance is highest"                               │
│ [ Stop ] [ Refine prompt ] [ Open trace ]                   │
├────────────────────────────────────┬────────────────────────┤
│                                    │                        │
│  ⋯ decomposing…                    │                        │
│                                    │                        │
│  (no fns yet)                      │                        │
│                                    │                        │
├────────────────────────────────────┴────────────────────────┤
│ 18:42:01  prompt received                                   │
│ 18:42:02  decompose started (gpt-4o-mini)                   │
└─────────────────────────────────────────────────────────────┘
```

Just feedback: I heard you, I'm working on it. The "Stop" button
is real — sessions are interruptible.

## t=1 — decompose produced a Dark expression

```
┌─────────────────────────────────────────────────────────────┐
│ Prompt: "build a CSV pipeline that finds rows where the     │
│          variance is highest"                               │
│ Top-level: analyzeCsv : String -> List<String>              │
├────────────────────────────────────┬────────────────────────┤
│  analyzeCsv csv =                  │                        │
│    csv                             │                        │
│    |> parseRows         ⋯ pending  │                        │
│    |> List.map calcVar  ⋯ pending  │                        │
│    |> sortByVariance    ⋯ pending  │                        │
│    |> takeHead          ✓ Stdlib   │                        │
│    |> getDate           ⋯ pending  │                        │
│                                    │                        │
│  4 Pending  ·  1 resolved          │                        │
│  Materializing in parallel…        │                        │
│                                    │                        │
├────────────────────────────────────┴────────────────────────┤
│ 18:42:01  prompt received                                   │
│ 18:42:02  decompose started                                 │
│ 18:42:04  decompose done → 5 fn refs                        │
│ 18:42:04  4 materializations kicked off                     │
└─────────────────────────────────────────────────────────────┘
```

Each fn is annotated with status (`⋯` pending, `✓` resolved,
`▼` fake, `↻` cached, `✗` failed). The expression IS the
high-level fn view; not a separate panel.

## t=2 — materializations in flight

```
┌─────────────────────────────────────────────────────────────┐
│ Top-level: analyzeCsv : String -> List<String>              │
├────────────────────────────────────┬────────────────────────┤
│  analyzeCsv csv =                  │                        │
│    csv                             │                        │
│    |> parseRows         ✓ 3.2s     │                        │
│    |> List.map calcVar  ⋯ 5.4s ⤴   │  parseRows resolved    │
│    |> sortByVariance    ⋯ 5.4s     │  ──────────────────    │
│    |> takeHead          ✓ Stdlib   │  sig: String           │
│    |> getDate           ✓ 4.1s     │       -> List<List<    │
│                                    │             String>>   │
│  3 done · 2 in flight · 0 failed   │                        │
│  Cost so far: $0.0008              │  body preview:         │
│                                    │  csv                   │
│                                    │   |> Stdlib.String.    │
│                                    │      split "\n"        │
│                                    │   |> List.map ...      │
│                                    │                        │
│                                    │  [ Refine ] [ Pin ]    │
├────────────────────────────────────┴────────────────────────┤
│ 18:42:04  4 mats kicked off                                 │
│ 18:42:07  ✓ parseRows                                       │
│ 18:42:08  ✓ getDate                                         │
│ 18:42:09  ⤴ calcVar (LLM thinking, gpt-4o, 5.4s)            │
└─────────────────────────────────────────────────────────────┘
```

Click on `parseRows` (the user just did) and the dive-in panel
shows its sig, body preview, and actions. Live timings tick.

## t=3 — first eval running

```
┌─────────────────────────────────────────────────────────────┐
│ Top-level: analyzeCsv "date,o,c\n2024-01-01,100,108\n..."   │
│ [ ⏸ Pause ]  [ Skip frame ]  [ Open trace ]                 │
├────────────────────────────────────┬────────────────────────┤
│  analyzeCsv csv =                  │  parseRows running     │
│    csv ─────────────────► [42KB    │  ──────────────────    │
│    |> parseRows ────────► [127 rows]  current call:         │
│    |> List.map calcVar ⋯ executing │   parseRows            │
│    |> sortByVariance               │     "date,o,c\n..."    │
│    |> takeHead                     │  duration: 87ms        │
│    |> getDate                      │  result preview:       │
│                                    │   [[date,o,c],         │
│  Live values shown inline.         │    [2024-01-01,100,108]│
│  Top arrow shows current frame.    │    ...]                │
│                                    │  [ Inspect value ]     │
├────────────────────────────────────┴────────────────────────┤
│ 18:42:10  eval started                                      │
│ 18:42:10  parseRows  called  ⤴                              │
│ 18:42:10.087  parseRows  returned (127 rows)                │
│ 18:42:10.088  List.map calcVar  called                      │
└─────────────────────────────────────────────────────────────┘
```

Values propagate inline (`csv` → `[42KB]`, `parseRows` →
`[127 rows]`). The "currently here" arrow shows the live frame.
Pause/Skip controls let the user inspect mid-eval.

## t=4 — refining

```
┌─────────────────────────────────────────────────────────────┐
│ Top-level: analyzeCsv : String -> List<String>              │
│ Refining: calcVar  (attempt 2)                              │
├────────────────────────────────────┬────────────────────────┤
│  analyzeCsv csv =                  │  calcVar refine        │
│    csv                             │  ──────────────────    │
│    |> parseRows         ✓          │  diff (rev 1 → rev 2): │
│    |> List.map calcVar  ↻ refining │                        │
│    |> sortByVariance    ✓          │  - prices |> ...       │
│    |> takeHead          ✓          │  - |> List.average     │
│    |> getDate           ✓          │  + let mean = ...      │
│                                    │  + let sq = ...        │
│                                    │  + ...                 │
│                                    │  scored: 280 → 470     │
│                                    │  (richer body)         │
│                                    │  [ ✓ Accept ] [ ✗ Reject ]
│                                    │                        │
├────────────────────────────────────┴────────────────────────┤
│ 18:42:23  user clicked Refine on calcVar                    │
│ 18:42:24  refine: gpt-4o called                             │
│ 18:42:30  refine: candidate ready, score 470                │
└─────────────────────────────────────────────────────────────┘
```

Refines surface in the dive-in with old/new diff + score.
Accept/Reject are explicit; nothing auto-commits.

## t=5 — committed

```
┌─────────────────────────────────────────────────────────────┐
│ Top-level: analyzeCsv : String -> List<String>              │
│ All Pending settled. Ready to commit.                       │
│ [ Commit selected as patch ]                                │
├────────────────────────────────────┬────────────────────────┤
│  analyzeCsv csv =                  │  Commit preview        │
│    csv                             │  ──────────────────    │
│    |> parseRows         ✓ #4290f2  │  Patch: "analyzeCsv +  │
│    |> List.map calcVar  ✓ #b1cce5  │   helpers"             │
│    |> sortByVariance    ✓ #82a401  │                        │
│    |> takeHead          ✓ Stdlib   │  4 fns to commit:      │
│    |> getDate           ✓ #d4e7b3  │   parseRows  fresh     │
│                                    │   calcVar    fresh     │
│  Hash-stamped. Stable.             │   sortByVariance fresh │
│  Other code can rely on these.     │   getDate    fresh     │
│                                    │  Namespace: User.Stachu│
│                                    │  Goes to: branch       │
│                                    │           "csv-pipeline"│
│                                    │  [ Send to remote ]    │
├────────────────────────────────────┴────────────────────────┤
│ 18:42:23  refine accepted: calcVar v2                       │
│ 18:42:31  user clicked Commit                               │
│ 18:42:32  4 PackageOps generated                            │
└─────────────────────────────────────────────────────────────┘
```

The transition from PackageID → Package(hash) is visible —
status indicators change from `✓` to `✓ #hash`. SCM commit is a
normal next step, surfaced in the dive-in panel.

## Dive-in mechanic

Click anything → that thing's detail opens in the dive-in panel,
stacking on what was there. Examples:

- Click a fn → see its sig, body, tests, dependents, trace
- Click an event in the timeline → jump to that step in the
  trace; live values update to that point
- Click a value preview (`[127 rows]`) → see the actual value
- Click a hash → see other fns/locations at that hash
- Click a Pending status → see materialization attempts (LLM
  calls, retries, failures, scores)
- Click a cap-request (top strip) → see what cap, requested by
  what, granted/denied
- Click a conflict notification → see both sides + manual-merge

Dive-in stacks: you can click-deep multiple levels and back out.

## Multiple zoom levels

Navigation goes:

```
whole program ↕ module ↕ fn ↕ expression ↕ value ↕ byte
```

At the highest zoom: a graph of fns + their dependencies, with
each fn shown as a node with status. Drill into a node to see
its body. Drill into an expression to see its sub-expressions.
Drill into a value to see its structure. Drill into a byte... ok
probably not, but the navigation is consistent.

Status badges propagate up: a fn is `⋯ pending` if any of its
sub-fns are pending. A module is `⋯ pending` if any of its fns
are. The user can always see "what's outstanding from where I
am."

## Concurrent threads / multiple in-flight workflows

Per the "`dark prompt` as daemon" idea (see [cli-daemon.md](../pre-s-and-s/cli-daemon.md)):
a user can have multiple in-progress sessions at once. The viewer shows them in
a sessions strip:

```
┌─────────────────────────────────────────────────────────────┐
│ Sessions:                                                   │
│  ▣ csv-pipeline      ⋯ 2 pending  (in focus)               │
│  ▪ html-render-fns   ✓ all done                             │
│  ▪ json-shape-test   ✗ 1 failed                             │
│  ▪ refine watcher    ↻ running, settled 3 of 5              │
└─────────────────────────────────────────────────────────────┘
```

Click a session to switch focus. Each session has its own
trace, its own fns-in-flight. Mostly independent; can
cross-reference (a fn might appear in multiple sessions if it's
shared).

## What the viewer is NOT (intentionally)

- **Not an IDE.** No code-editing affordances beyond the dive-in
  edit-this-body modal. The IDE (VS Code, etc.) is separate.
- **Not a test runner UI.** Tests are inside the dive-in panel
  for fns that have them.
- **Not a SCM tool.** SCM ops surface here, but the main SCM UI
  is its own app (using the same composable-MVU substrate).
- **Not a deployment surface.** No deploy buttons; PDD work
  becomes shareable through sync ([sync.md](../stable-and-syncing/sync.md)), not
  deploy.

It's purposefully focused: **the in-progress fn + everything you
need to steer it**.

## What this means for implementation

(Intentionally vague — that's the brief.)

- Render target: TUI for terminal users; webview for VS Code;
  HTML for browser. Composable-MVU `View` abstracts the
  difference.
- State: lives in the Model of one MVU app. Survives hot-reload.
- Events come from the bus (B4).
- Polymorphic views (per [composable-mvu.md](../pre-s-and-s/composable-mvu.md)) mean
  a Pending renders one way, a fn body another, a trace another — all
  out of the box, customizable per user.
- The whole thing should be Dark code on top of the substrate
  (see the keystone [distributed-event-sourcing.md](../pre-s-and-s/distributed-event-sourcing.md)).

## Aspirational closing

The viewer is the user's *cockpit* for the recursive-live-
development experience. They see what's happening, they steer
it, they dive into anything that matters. The system isn't
producing source files for them to read; it's producing
*decisions* for them to make. The viewer is where those
decisions live.

Build this well and PDD goes from "interesting demo" to
"actually how I write code now."

---

# Extensions — current thinking (added)

The sketches above are the original brief and stay intact. What
follows extends them with ideas that have firmed up since. The
through-line: **a view is a projection of state, never the state
itself.** Everything above renders from the same underlying op
stream; the sketches below make that lens explicit and push it
further.

## The core lens: views are projections of the folded op stream

Nothing the viewer shows is canonical. The canonical thing is the
op stream — the append-only log of operations (see
`distributed-event-sourcing.md`). State is what you get by
folding that stream; a view is a projection of that folded state
into pixels or characters.

```
   ops (append-only log)
        │  fold
        ▼
   state (current value of the world)
        │  project
        ▼
   view (TUI / HTML / webview)
```

Consequences that matter for the viewer:

- **Time travel is free.** Scrubbing the event-stream timeline
  (the bottom region in every sketch above) is just folding the
  op stream up to a chosen offset, then re-projecting. The
  "currently here" arrow at t=3 is the projection of a fold that
  stopped at one op.
- **Every panel is a pure function** `state -> view`. No panel
  owns mutable state of its own; the dive-in stack is itself a
  small piece of view-local state projected over the same world.
- **Two viewers, same stream, different projections.** A user on
  a phone and a user in a terminal fold the same ops and project
  differently. Divergence is a projection choice, not a fork.
- **"What changed" is a diff of two folds.** The refine diff at
  t=4 is `project(fold(ops≤A))` vs `project(fold(ops≤B))`.

Sketch — the same op stream, three projections side by side:

```
   op stream:  [decompose][mat parseRows]...[refine calcVar]
        │
        ├── fold@now ──► project(TUI)   ► the t=5 sketch
        ├── fold@t3  ──► project(graph) ► dependency DAG, mid-eval
        └── fold@now ──► project(HTML)  ► browser cockpit
```

## Auto-generated views from a value's type

Today the focus panel knows how to render a fn body, a Pending, a
trace. That's a fixed catalog. Extend it: for *any* value whose
type the system knows, the viewer can synthesize a view with no
hand-written renderer.

Two generation paths, both producing an editable starting point:

1. **Reflection.** Walk the type structure and emit a default
   projection — records become labeled rows, lists become
   scrollable tables, enums become tagged cards, recursive types
   become collapsible trees. Deterministic, instant, no LLM.
2. **LLM codegen.** Hand the type (and optionally a sample value)
   to a model and ask for a nicer projection — units, sparklines,
   a chart, domain-aware grouping. Slower, prettier, fallible.

Crucially the generated view is **a normal Dark view fn** —
itself a value in the system, hand-editable afterward. The
auto-view is a *seed*, not a *cage*.

```
┌─────────────────────────────────────────────────────────────┐
│ Value: stats : { mean: Float; stdev: Float; n: Int;         │
│                  samples: List<Float> }                     │
│ View: ◉ auto (reflection)  ○ auto (LLM)  ○ custom           │
├─────────────────────────────────────────────────────────────┤
│  mean    1.84                                               │
│  stdev   0.37                                               │
│  n       127                                                │
│  samples ▁▂▅▇▆▃▂▁  (127 values, click to expand)            │
│                                                             │
│  [ Edit this view ]  ← drops you into the structural editor │
└─────────────────────────────────────────────────────────────┘
```

Flow: reflection gives an instant default; if the user wants
nicer, "regenerate with LLM" projects a richer candidate; either
way "Edit this view" hands the projection fn to the editor below.

## One view engine, shared with the structural editor

The projection machinery that renders values is the *same* engine
the structural editor uses to render and edit code (see
`structural-editor.md`). Code is just a value (an AST is a
typed tree), so editing code and editing a view are the same
operation: a projection you can act on.

- The editor renders an AST by projecting it — exactly how the
  focus panel projects a value.
- An edit in the structural editor emits an op onto the same
  stream the viewer folds. Edit and view close the loop.
- "Edit this view" on an auto-view (above) opens the projection
  fn *in the same editor*, because that fn is also just an AST.

```
        ┌──────────── view engine ────────────┐
        │  project : (Type, Value) -> Tree     │
        │  render  : Tree -> TUI | HTML         │
        │  edit    : Tree, Action -> Op         │
        └───────────────┬──────────────────────┘
            ┌────────────┴────────────┐
            ▼                          ▼
      structural editor            in-focus-fn viewer
      (project an AST)             (project any value)
```

One engine means: keybindings, theming, collapse/expand, and
polymorphic renderers are written once and shared. The viewer is
not a cousin of the editor; it is the same thing pointed at
different values.

## "List of conflicts" as a standard view

Conflicts (see [conflicts.md](../stable-and-syncing/conflicts-and-resolutions.md)) are not a modal
interruption — they are a projection of state, so they get a
standard view like anything else. The set of unresolved conflicts
is `fold(ops) |> filter unresolved`; render that list.

```
┌─────────────────────────────────────────────────────────────┐
│ Conflicts (3 open)                          [ Resolve all ]  │
├─────────────────────────────────────────────────────────────┤
│ ⚠ calcVar        local rev2  vs  remote rev5   [ View diff ] │
│ ⚠ User.Stachu.fmt  type changed both sides     [ View diff ] │
│ ⚠ parseRows      deleted here, edited there    [ Keep / Drop]│
├─────────────────────────────────────────────────────────────┤
│ Resolved this session: 4   ·   Auto-merged: 11              │
└─────────────────────────────────────────────────────────────┘
```

Each row dives in to the existing both-sides + manual-merge UI
already listed in the dive-in mechanic. The list is just the
top-level projection over the conflict set; resolving emits an op
and the list re-projects (one fewer row).

## Live "what's parked" view

Async work parks on the event bus (see `event-bus.md` and
`async.md`): materializations awaiting an LLM, evals
waiting on a capability grant, refines mid-flight, anything
suspended on an unmet condition. "What's parked" is a projection
of the parked set on the bus.

```
┌─────────────────────────────────────────────────────────────┐
│ Parked (5)                            updates live ↻         │
├─────────────────────────────────────────────────────────────┤
│ ⤴ calcVar        waiting on gpt-4o          12.3s   [ Kill ] │
│ ⏸ writeReport    waiting on cap: net:send   ∞       [ Grant]│
│ ⏸ fetchPrices    parked on retry backoff    3.1s    [ Now ] │
│ ⤴ sortByVariance waiting on parseRows        —       (dep)   │
│ ⏸ session:nightly parked until 02:00         6h      [ Wake ]│
├─────────────────────────────────────────────────────────────┤
│ Oldest park: 6h   ·   Parked on caps: 1   ·   on LLM: 2     │
└─────────────────────────────────────────────────────────────┘
```

This is the steering surface for "why is nothing happening?" —
each parked item names *what it is waiting on* and offers the
unblock action (grant a cap, run-now, wake, or kill). It is the
inverse of the event stream: the stream is what *happened*, the
parked view is what is *waiting*.

## Components borrowed from immediate-mode UI

The render layer should feel like an immediate-mode UI library
(Clay is the touchstone): the view fn declares the layout it
wants *this frame* from current state, and the engine lays it out
and diffs against the previous frame. This fits the projection
lens exactly — each frame is `project(fold(ops))`, recomputed,
declarative, no retained widget tree the programmer hand-mutates.

What to borrow concretely:

- **Declarative layout primitives** — row/column/grow/fixed/wrap,
  expressed inline in the view fn. No imperative "create widget,
  set parent, set constraint."
- **Per-frame, retained-by-engine.** The programmer writes as if
  rebuilding everything each frame; the engine retains and diffs
  so it is cheap. Hot-reload-friendly: a changed view fn just
  produces a different frame.
- **Layout that is itself a projected value** — so the layout of
  a panel can be inspected and edited in the same structural
  editor, recursively.

```
   row [ grow ] {
     column [ fixed 32 ] { focusPanel(state) }
     column [ grow     ] { diveInStack(state) }
   }
   row [ fixed 6 ] { eventStream(state) }
```

## Render to HTML as well as the terminal

Everything above must project to **HTML in a browser**, not only
the TUI. Same view fns, same immediate-mode layout, two backends:
a terminal backend emitting box-drawing + ANSI, and an HTML
backend emitting elements + CSS (the webview path from "What this
means for implementation" generalizes here). The browser
projection gets real affordances the terminal fakes — actual
charts for the sparkline above, draggable dive-in panels,
hyperlinked hashes — but it is the *same projection of the same
folded op stream*, not a separate app. A user can have the TUI
open in a terminal and the HTML cockpit open in a tab, both
folding one stream, projecting in parallel.

```
                    project(fold(ops))
                   ╱                   ╲
        terminal backend          HTML backend
        box-drawing + ANSI        elements + CSS + canvas
        sparkline ▁▂▅▇            <svg> sparkline
        [ Inspect value ]         clickable, draggable panel
```
