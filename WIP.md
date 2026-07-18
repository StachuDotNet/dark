# WIP — CLI UX workbench implementation

The loop reads THIS file first, does the NEXT ACTION, updates it, reschedules. Be ruthless: ship small
working increments, keep the tree loading, commit often. Autonomous — the user is asleep; make every call,
never wait.

## Loop horizon
Run until ~2026-07-19 04:00 (24h). Each fire: `date` — if past that, do a final commit + write a summary at
the top of the Log + stop the loop (ScheduleWakeup stop:true). Otherwise keep going, reschedule ~300s.

## Goal
Implement the CLI UX redesign (the "workbench") designed in `/home/stachu/code/dark/main/notes/cli-ux/`
(read those deep docs — 01 interaction model, 03A-D component kit, 10-23 views, 90 adjustments ledger, 91
the keymap/badge AUTHORITY). North star for day 1: **`dark` opens on a framed, navigable Tree + Inspect**
instead of a bare prompt.

## Where
- Repo: `/home/stachu/code/dark/loop-fun` (the ACTIVE build dir — data.db is live here).
- Branch: `cli-ux-workbench` off `github/main` (= merged PR #5685 syncing-clean; has conflicts/ops/sync).
- Design docs: `/home/stachu/code/dark/main/notes/cli-ux/` (read-only reference; different dir).
- NEVER force-push, reset --hard, or stash. Commit forward only.

## Inner loop (the tight cycle) — use `./dev-ux-check`
1. Edit a `.dark` (or `.fs`) file.
2. `./dev-ux-check` — reloads packages, greps for load errors, prints PASS/FAIL + the error.
3. If PASS, test: `./scripts/run-cli <cmd>` (or the interactive form via expect if needed).
4. Commit when a unit works: `DARK_ACCOUNT=stachu git commit -am "cli-ux: <what>"`.
- .dark reload ~10s; F# build ~1min (auto via the running `_build-server --watch`).
- GOTCHA: a non-loading .dark aborts the WHOLE reload and can FK-corrupt the package DB. So reload+verify
  after EVERY edit; never stack unverified edits. Recovery: `./scripts/run-cli` still works off last-good
  DB; if corrupted, restore from `rundir/seed.db` or `scripts/build/reset-test-db` (see main memory).
- GOTCHA: Dark type changes need TWO reload passes (embedded package-ref-hashes). Reload twice on type edits.
- Component framework resolves only from a nested submodule under the file's module (see main memory
  `reference_dark_component_resolution`); named fns only in AppState (no inline lambdas persist).

## Build order (from 90 §8) — each phase independently shippable; MainPrompt keeps working throughout
- [ ] P1 FRAME: `hstack`+`distributeCols` in ui/layout.dark → `SplitPane` → the Frame (TabBar+Breadcrumb+
      StatusBar/Signals+KeyHintBar) → mount a `TreeWidget` (extract from navInteractive+tree) → `dark` opens
      on framed Tree. Add Inspect. (readKey timeout for live ticks can come later — P1 is static-navigable.)
- [ ] P2: Home dashboard + Changes (lift SCM.Review.App into SplitPane) + History (branches/commits/ops).
- [ ] P3: `:` CommandBar (MainPrompt in an overlay) + `/` global search.
- [ ] P4: MultilineEditor → Edit + Scratchpad.
- [ ] P5: Resolve (wire the conflictDispatch seam + conflicts.dark).
- [ ] P6: Mesh, Agents, Runs, Services, Things, Docs.

## Keymap/badge = 91 is authority
x=destroy, d=diff/detail, r=rename-or-rerun; Ctrl+Tab=views/Tab=panes; ?=HelpOverlay; badge set frozen (91 §4).

## NEXT ACTION
P1 step 4 — SEE it, then split the body. The `workbench` SubApp is built + wired + data-verified (frame.dark,
app.dark, registered; loadItems root → [Darklang,Feriel,Stachu] ✓). Not yet visually verified (needs a TTY).
1. Interactive verify via expect (see `docs interactive-testing`): drive `./scripts/run-cli` → type `workbench`
   → capture the framed render (tab bar highlighted, breadcrumb, tree body, key hints). Confirm ↑↓/→/← work.
   Harness: `./scripts/run-in-docker expect scripts/testing/test-interactive.expect` — adapt or write a small
   .expect that spawns run-cli, sends "workbench\n", arrow keys, and dumps the screen. If expect is fiddly,
   at least run it under `script`/PTY to eyeball one frame. Fix any render bugs (line wrapping, offsets).
2. Split the body: when activeView==Tree, render body as SplitPane Tree(left)|Inspect(right). Inspect pane
   shows the selected item's source (reuse PrettyPrinter via the `view` path / Packages.View.viewEntity as a
   region renderer). Tab toggles focus. (Master-detail: moving selection updates the Inspect pane.)
3. THEN flip `dark` no-args → open workbench: in core.dark executeCliCommand, the `[]` branch currently prints
   welcome + runs the prompt loop; instead open the workbench SubApp (keep MainPrompt reachable via `:` later /
   a flag). Small, careful change; keep the old path behind `dark prompt` or an env flag so nothing's lost.
Design refs: main/notes/cli-ux/{03A,10,11,12}. Keymap authority: 91 (x=destroy/d=diff/r=rename-or-rerun).

## Status: P1 in progress. DONE: hstack/distributeCols; SplitPane; Frame chrome; workbench SubApp built+wired
   +data-verified (commits d957c2471, 7322c6ef1, e1cb56823). PENDING: visual/interactive verification.

## Gotchas learned (append as you hit them)
- Typed lambda params are NOT supported: `fun s -> …` only, never `fun (s: String) -> …` (PARSE-UNCLOSED).
- Module-level VALUES use `val name = …` (NO type annotation). `let` is only for functions + local bindings.
  (e.g. `val viewNames = [ … ]`, not `let viewNames : List<String> = …`.)
- Name resolution from `Darklang.Cli.Apps.Workbench.*`: use `UI.Layout.…` (not bare `Layout`) — the ancestor
  chain hits `Darklang.Cli`, from which `UI.Layout` resolves but `Layout` doesn't. `Colors` resolves bare.
- A non-loading .dark aborts the whole reload → always `./dev-ux-check` after each edit; read the real error
  via `grep -niE 'error\\[|Unresolved|expected|not found|not supported' rundir/logs/packages.log | tail`.

## Log (newest first)
- 2026-07-18 04:09 — P1: workbench SubApp done (app.dark: State/render/handleKey/makeSubApp/execute) + registered
  `workbench`/`wb`. Gotcha: Write dropped ESC bytes in control strings → normalized to  via python. Verified
  help + loadItems root → [Darklang,Feriel,Stachu]. Loads clean. Commit e1cb56823. NOT yet visually verified (TTY).
- 2026-07-18 04:00 — P1: frame.dark done (tab bar + breadcrumb + keyhint render helpers). Learned 2 gotchas
  (val for module values; UI.Layout not Layout from Apps.Workbench). Verified, committing. Next: app.dark SubApp.
- 2026-07-18 03:53 — P1: SplitPane done (ui/splitpane.dark: drawBox focus-aware border, render both
  orientations, narrow-collapse, toggle). Hit + recorded the typed-lambda-param gotcha. Verified, commit
  d957c2471. Re-strategized: ship a `workbench` SubApp (reuse nav) before touching core.dark. Next: the Frame.
- 2026-07-18 03:5x — P1: added hstack/distributeCols/fixedWidth/flexWidth/greedyWidth to ui/layout.dark;
  verified [40,60] split; committed 5053e91f4. Inner loop (./dev-ux-check) green. Next: SplitPane.
- 2026-07-18 03:5x — setup: branched cli-ux-workbench off github/main; loop-fun is the active build dir;
  wrote WIP + dev-ux-check + 5-min loop.
