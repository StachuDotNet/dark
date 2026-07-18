# Workbench UX iteration — Phase 5

Branch `cli-ux-redux` (loop-fun, container `peaceful_knuth`). Goal: make the workbench feel like a real TUI
someone can live in — discoverable, consistent, full-screen, with the CLI's real touchpoints reachable.
Also in scope (user): build reusable UI components + Stdlib to support nice TUIs in Dark.

Recovery note: if `run-cli` crashes with "type hash not found", loop-fun's devcontainer exited — `docker start
peaceful_knuth` (it must be newer than zen_easley so run-in-docker's `--last 1` selector picks it).

## Inner loop
edit .dark -> `./dev-ux-check` (add `twice` after a State/type change — embedded hashes) -> tmux drive+capture
(session `wb`, restart the app after each reload) -> commit -> `git push github cli-ux-redux`.

## Findings (current state)
- Discoverability: there IS a bottom hint bar + a `?` overlay, but `?` is never advertised; Home's hint is
  thin; Inspect/Mesh/Agents/etc. fall to a generic hint. No "how do I run a command / prompt" affordance.
- Naked pages: Tree/Inspect use a bordered SplitPane (good). Changes/History/Resolve/Docs/Runs/Services/Mesh
  render a bare list into the raw region — no border, no title, lots of empty space. Home is top-left text.
- Conflicts: NOT surfaced on Home. Breadcrumb hardcodes green "✓ synced" regardless of real state.
- Missing touchpoints vs the ~55 CLI commands: search (`/`), eval / run-a-command (command palette `:`),
  deps (dependents/dependencies of selection), branch rebase/merge, sync-now, login state, deprecate/delete/
  undo on a selection, caps.

## Plan (priority order — land + verify + commit each)
1. [ ] Conflicts on Home + honest sync badge. State gets `conflictCount`; Home shows a warn line when >0;
       breadcrumb shows "⚠ N to resolve" (pink) vs "✓ synced" (green) from real `Sync.Conflicts.list`.
2. [ ] Reusable `UI.Box` (single bordered titled pane; extract from SplitPane.drawBox) + a reusable
       `UI.ListView` (scrollable, selectable, scrollbar). Wrap every single-list view in a titled box so the
       naked pages fill the screen consistently. This is the big consistency win + a real component.
3. [ ] Discoverability: hint bar always ends with "· ? help"; per-view hints for all views; a global-keys
       line. Make the input/prompt affordance obvious.
4. [ ] Global search `/` — overlay to search packages, jump to a result (fills the biggest command gap).
5. [ ] Command palette `:` (or Ctrl+K) — run a CLI command / eval from inside the app. The "prompt" the
       user is missing. Feeds a component: a fuzzy command list + arg entry + output reader.
6. [ ] More touchpoints: deps in Inspect; branch rebase/merge in History; sync-now in Mesh/Resolve; login
       state on Home; deprecate/delete/undo on a Tree selection.
7. [ ] Fold in the TUI-UX research (agent running) — apply its checklist; tighten color/badges/empty states.

## Log
- (start) Rebase: already current — github/main = upstream/main = 17eb99eca (#5685), branch 0 behind.
- Container misrouting fixed (peaceful_knuth restarted).

## NEXT ACTION
Item 1: conflicts on Home + honest sync badge. Add `conflictCount` to State (two-build pass), thread to
renderHome + renderBreadcrumb. Verify in tmux. Commit + push. Then item 2.
