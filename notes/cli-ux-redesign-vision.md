# CLI UX redesign — vision & sketches (for later, mostly post-this-PR)

Today the `dark` CLI is essentially **one view**: a chat-window / command-line REPL. The vision is a
small set of **high-level full-screen apps** you visit often, each a `Cli.Component`, that share a
common package-path + selection model and hand off to each other fluidly.

This PR ships **bare-bones render-only sketches** of three of them (pure `render` + `Model` + TODOs,
tested in `testfiles/execution/pre-s-and-s/apps-sketches.dark`). Interactive wiring is a TODO — see the
resolution gotcha note below.

## The views

1. **Explorer** (`Apps.Explorer`) — the **dir-based** experience. The package namespace as a file
   manager: a breadcrumb path bar, a selectable listing, cd/ls/open. "One level at a time."
2. **TreeView** (`Apps.TreeView`) — **package-tree** navigation + in-place adjustment. The whole
   hierarchy as one collapsible tree; rename/deprecate/move/open without leaving. "Shape at once."
3. **Repl** (`Apps.Repl`) — a full-screen **scratchpad / script-testing** loop. Persistent history,
   eval, watch-a-script-file-and-re-run, promote-last-result-to-a-value.

**Connections** (the "fluid" part): Explorer ↔ TreeView share the package-path + cursor (a key toggles
between them at the same location). Either can open the selected item into Repl. All three can flag a
location that has a pending sync conflict / WIP.

## Constraints & resolutions — the unifying thread

The sync conflict/resolution UX (now beautiful — `Sync.Display.conflictReport`, last-write-wins framed,
winner-marked, ack-prompting) is one instance of a **general "constraint" surface** the codebase already
gestures at (see `ProgramTypes.PackageOp` comments on Deprecate/PropagateUpdate "Constraints"):

- sync name divergences (done: auto-resolved by LWW, surfaced for ack),
- merge conflicts,
- propagation signals (a rename that orphans dependents; a new fn shadowing an existing signature),
- deprecation signals.

**Goal:** route ALL of these through one sophisticated, fluid resolution flow — surfaced as data, never
blocking, always landing somewhere the user eventually **acks or overrides** (nothing silently lost),
shown inline in Explorer/TreeView at the affected location, with a consistent `ack` / `resolve mine|theirs`
vocabulary. The conflict-dispatch seam (`ExecutionState.conflictDispatch`, this PR) is the runtime hook
all of these can eventually emit through.

## TODO backlog (later)
- [ ] wire each sketch interactive via `Cli.Component` (nested submodule — see gotcha)
- [ ] real data behind Explorer/TreeView (`pmListAtPath` / `pmChildren`, lazy)
- [ ] shared path-cursor model + view-toggle key
- [ ] Repl eval through `Cli.Eval`; watch-script loop; promote-to-value
- [ ] unify constraints (merge / propagation / deprecation / sync) into one resolution flow + inline badges
- [ ] route constraint resolutions through the `conflictDispatch` seam

## Gotcha (cost real time — see memory `reference_dark_component_resolution`)
The `Component` framework only resolves from a **nested submodule** under `Apps.*`, not the file's top
module. Bare `{ }` record construction needs a type-name prefix (`Model { … }`). A non-loading `.dark`
file aborts the whole reload; partial failed reloads can FK-corrupt the package DB (reset via
`migrations run`).
