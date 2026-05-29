# View Sketches — In-Focus-Fn Experience

High-level visual brief for the recursive-live-development viewer.
**Not committal on tech.** Sketches at multiple moments to convey
shape; whoever builds the viewer takes these as inputs, not
specifications.

The viewer is one MVU app (see `COMPOSABLE-MVU.md`) subscribed to
the event bus (see `EVENT-STREAMS-AND-PARKING.md`), surfacing the
in-focus fn as it gets shaped.

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
│  analyzeCsv csv =                  │                        │
│    csv ─────────────────► [42KB    │  parseRows running     │
│    |> parseRows ────────► [127 rows]  ──────────────────    │
│    |> List.map calcVar ⋯ executing │  current call:         │
│    |> sortByVariance               │   parseRows            │
│    |> takeHead                     │     "date,o,c\n..."    │
│    |> getDate                      │  duration: 87ms        │
│                                    │  result preview:       │
│  Live values shown inline.         │   [[date,o,c],         │
│  Top arrow shows current frame.    │    [2024-01-01,100,108]│
│                                    │    ...]                │
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
│  analyzeCsv csv =                  │                        │
│    csv                             │  calcVar refine        │
│    |> parseRows         ✓          │  ──────────────────    │
│    |> List.map calcVar  ↻ refining │  diff (rev 1 → rev 2): │
│    |> sortByVariance    ✓          │                        │
│    |> takeHead          ✓          │  - prices |> ...       │
│    |> getDate           ✓          │  - |> List.average     │
│                                    │  + let mean = ...      │
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
│  analyzeCsv csv =                  │                        │
│    csv                             │  Commit preview        │
│    |> parseRows         ✓ #4290f2  │  ──────────────────    │
│    |> List.map calcVar  ✓ #b1cce5  │  Patch: "analyzeCsv +  │
│    |> sortByVariance    ✓ #82a401  │   helpers"             │
│    |> takeHead          ✓ Stdlib   │                        │
│    |> getDate           ✓ #d4e7b3  │  4 fns to commit:      │
│                                    │   parseRows  fresh     │
│  Hash-stamped. Stable.             │   calcVar    fresh     │
│  Other code can rely on these.     │   sortByVariance fresh │
│                                    │   getDate    fresh     │
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

Per FRONTIER's "dark prompt as daemon" idea: a user can have
multiple in-progress sessions at once. The viewer shows them in
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
  becomes shareable through sync (`SYNC-AND-STABILITY.md`), not
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
- Polymorphic views (per `COMPOSABLE-MVU.md`) mean a Pending
  renders one way, a fn body another, a trace another — all
  out of the box, customizable per user.
- The whole thing should be Dark code on top of the substrate
  (`FRONTIER.md` framing).

## Aspirational closing

The viewer is the user's *cockpit* for the recursive-live-
development experience. They see what's happening, they steer
it, they dive into anything that matters. The system isn't
producing source files for them to read; it's producing
*decisions* for them to make. The viewer is where those
decisions live.

Build this well and PDD goes from "interesting demo" to
"actually how I write code now."
