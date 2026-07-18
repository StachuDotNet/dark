# Workbench UX iteration — Phase 5 (autonomous, until ~17:00)

Branch `cli-ux-redux` (loop-fun, container `peaceful_knuth`). Make the workbench a real TUI someone can live
in. Reference: `notes/tui-ux-principles.md` (research checklist). Also in scope: reusable UI components + Stdlib
for building nice TUIs in Dark.

Recovery: `run-cli` "type hash not found" => loop-fun devcontainer exited => `docker start peaceful_knuth`.
Inner loop: edit -> `./dev-ux-check` (add `twice` after a State/type change) -> tmux (`wb`, restart app after
reload) -> commit -> `git push github cli-ux-redux`. NEVER reset --hard/stash/force-push.

## User feedback driving this phase
1. Authoring (types/fns) felt visually cramped — done in the tiny bottom prompt bar. Wants SPACIOUS MULTI-LINE
   editing, in a real editor pane, not the bottom bar.
2. Keyboard shortcuts felt "unexpected". Wants a coherent, thought-through scheme — explicit, with Tab /
   Shift-Tab for moving between panes.
3. Conflicts visible on Home (DONE). Chooser instead of DARK_CLASSIC flag (DONE).
4. "Think through everything." Build components/Stdlib that drive the big app.

## DONE so far this phase
- Conflicts on Home + honest sync badge + Resolve tab badge (df9bb17).
- UI.Box component; every list view framed (no naked pages) (7d3ca05).
- Discoverable footer (key bright/label dim, per-view + anchored globals) + `:` run prompt (eval) (adabd18).
- One-key experience chooser on launch (703f2bc).

## KEYMAP REDESIGN (the coherent scheme — implement + document in ? and footer)
Principle: same key means the same thing everywhere; movement is vim + arrows; Tab/Shift-Tab = pane focus;
numbers/brackets = views; letters = context actions shown in the footer.

- Movement (focused pane): `↑/k` up, `↓/j` down, `g`/`G` top/bottom, PageUp/Dn.
- Structure (Tree): `→/l/Enter` descend or open; `←/h` up a level.
- Panes: `Tab` next pane, `Shift-Tab` prev pane (fall back: Tab wraps if Shift-Tab not delivered — verify what
  the F# readKey sends for CSI Z; test in tmux). Focused pane = bright border (already), others dim.
- Views: `1`-`9` jump; `[`/`]` cycle; (Ctrl+arrows NOT used). Numbers >9 by typing the number (already works).
- Global: `:` run (eval), `/` search (to build), `?` help, `Esc` back-out-one-level, `q` quit.
- Context actions (shown in footer per view):
  - Tree: `n` new fn · `t` new type · `v` new value · `e` edit selected · `d` deps (to build) · Enter open.
  - Changes: `c` commit · `x` discard · Enter source.
  - History: `b` new-branch · `s` switch · (later `m` merge · `r` rebase) · Enter ops.
  - Resolve: Enter detail · (later `a`/`o` accept ours/theirs).
- Esc discipline: closes editor/input/reader first; from a drilled Tree location, `←` goes up; Esc at top-level
  exits. Never destructive.
- Confirm scaling: discard already y-confirms; keep. Destructive stays explicit-key, never Enter-default.

## AUTHORING REDESIGN (spacious, multi-line, boxed)
Kill the bottom-bar name prompt for new items. Instead:
- `n`/`t`/`v` opens the multi-line editor immediately, pre-filled with a FULL declaration template incl. a
  placeholder name, e.g.
    fn:    `let newFunction (x: Int): Int =\n  x`
    type:  `type NewType =\n  { field: Int }`
    value: `let newValue =\n  1`
  Cursor positioned on the placeholder name for immediate replacement.
- The editor renders inside a titled UI.Box ("New function" / "Editing Owner.Mod.name"), full body region,
  with the error line inside the box — spacious, unmistakably a mode.
- saveEditing parses the WHOLE buffer (already a full declaration): extract the leaf name from the source
  (keyword + next identifier), build location via parseRelativeTo state.location leaf, then the existing
  WrittenTypes->PT->ops path. Drop the `nameStr`-prepend. Edit-existing (`e`) prefills the full declaration.
- Footer in editor: `^s save · esc cancel · ↑↓←→ move · tab indent`. Consider a status: parse-ok/parse-err live.

## Remaining plan (priority)
- [ ] A. Authoring redesign (above). HIGH — explicit feedback.
- [ ] B. Keymap redesign (above) — Tab/Shift-Tab panes, vim motion, consistent, documented. HIGH.
- [ ] C. List+preview splits for Changes/History/Docs/Resolve (IDE 3-panel; reuse SplitPane + existing detail
       fns changesSourceText/commitOpsText/topic content). Makes them dense + useful, not just framed lists.
- [ ] D. Global search `/` — overlay, search packages, jump to result. Re-add `/` to footer when built.
- [ ] E. More touchpoints: deps in Inspect (`d`), branch merge/rebase in History, sync-now, login state on
       Home, deprecate/delete/undo on a Tree selection.
- [ ] F. Components/Stdlib: UI.ListView (extract renderTreeList), UI.Prompt, a status/toast row, semantic
       color slots in Colors if missing. Whatever removes duplication + drives the app.
- [ ] G. Apply TUI checklist: transient message/toast row; "terminal too small" guard; mode color block.

## NEW feedback (fold in)
- H. Use syntax highlighting of code in MORE places, broadly (Inspect pane, source readers, editor, Changes
     source). Find the existing highlighter (LSP/semantic — see PackageRefs WrittenTypes note) and apply it.

## Findings for upcoming items
- Syntax highlighter EXISTS: `SyntaxHighlighting.highlightCode (src: String) : String` in
  packages/darklang/cli/utils/syntaxHighlighting.dark (used by the `view` command; also highlightLine per-line
  with SemanticTokens). CAVEAT: it emits ANSI, and UI.Layout.printAt truncates by raw String.length (counts
  ANSI bytes) — so highlighted text truncates wrong. Apply first in the READERS (full-width source view:
  Tree-leaf Enter, Changes source, History) where lines rarely exceed width; for the narrow Inspect pane and
  the editor, need ANSI-aware truncation or print-without-truncate first. Consider a `UI.Layout.printAtAnsi`
  that truncates by visible width (skip ANSI escape runs) — a reusable component worth building for item F.
- Shift-Tab: unverified whether F# readKey delivers it (CSI Z). For 2-pane, Tab toggles both ways already.
  Test `\x1b[Z` in tmux before relying on it; else Tab cycles + wraps.

## DONE this phase
- Conflicts on Home + badge (df9bb17) · UI.Box, framed views (7d3ca05) · footer + `:` run (adabd18) ·
  chooser (703f2bc) · A: spacious authoring (29393e1) · B: vim motion + safe Esc-to-Home (99a5d27).

## NEXT ACTION (updated 15:50)
ALL planned items done: A B C D H + reader-highlight + login + too-small-guard + toast + shallow-auth-fix +
`d` deps + `m`/`r` merge/rebase + F (UI.Layout.truncateVisible ANSI-aware truncate wired into preview+reader;
UI.ListView extracted from renderTreeList). Backlog essentially exhausted.
NEXT: light polish only if clearly valuable (e.g. make `/` search results jumpable — deferred, moderate), else
IDLE toward the 16:50 FINALIZE. Do NOT start risky new features in the last hour.
FINALIZE at ~16:50: add "Phase 5: UX iteration" section to main/notes/cli-ux-workbench-report.md (list all the
above), refresh `git diff --shortstat github/main..HEAD`, `~/bin/print-md` it, `git push github cli-ux-redux`,
clear chat message (branch; run `cd /home/stachu/code/dark/loop-fun && ./scripts/run-cli`; what's new; honest
not-done: search jump-to-result, Agents/Things views, item rename, merge/rebase success paths untested), STOP loop.

## Prior NEXT (15:36)
Done: A B C D H + reader-highlight + login + too-small-guard + toast-row + shallow-authoring-fix + `d` deps +
`m`/`r` merge/rebase (confirm + typed toasts: green ✓ success / pink ✗ fail/guidance; error paths verified,
success needs a branch with changes). NEXT (item F, reusable components — user explicitly asked):
- UI.ListView: extract the selectable, scrolling, scrollbar list from renderTreeList into a reusable component
  (region + items + selectedIndex + a row-renderer). Have the workbench's list use it. Low user-visible risk;
  verify the Tree/Changes/etc lists still scroll + select + show the scrollbar after.
- UI.Layout.truncateVisible (ANSI-aware): truncate by VISIBLE width, skipping ANSI escape runs — lets
  renderPreview/renderReading highlight even lines that don't fit (drop the fit-or-plain fallback). Test with a
  long highlighted line.
Time check each fire. FINALIZE at ~16:50: report Phase 5 section + shortstat + print-md + push + chat + STOP.

## Prior NEXT (15:23)
Done: A B C D H + reader-highlight + login + too-small-guard + toast-row (State `message`, green ✓ in footer,
cleared next keypress; set on commit/discard/save/branch). Also FIXED shallow-depth authoring: `let` needs a
BARE name (qualified = parse error) so location needs owner+module -> authoring requires depth>=2; n/t/v now
toast-guide you to descend instead of opening a doomed editor. NEXT:
- `d` deps on a Tree leaf -> reader. Get hash: searchExactMatch state.branchId (modulePathOf location)
  item.name -> results.{fns,types,values} head -> item.entity.hash. Builtin.depsGetDependencies branchId hash
  returns (hash, _) list; resolve names (see deps.dark resolveName / namesDict) or just show hashes short.
  openReader isCode=false. Footer: add `d` deps to Tree hints.
- History `m` merge / `r` rebase with y-confirm (SCM.Merge/Rebase are AppState cmds — port core like commit).
- F: extract UI.ListView from renderTreeList; ANSI-aware truncateVisible in UI.Layout (reused by preview+reader).
FINALIZE at ~16:50.

## Prior NEXT (15:09)
Done: A B C D H + reader-highlighting + login-on-Home + too-small-guard (render splits into renderFull + a
render wrapper; can't trigger in tmux since getWidth reads the real terminal not the pane). NEXT:
- Transient message/toast row: add `message: String` to State (two-build), show it dim/green in the hint row
  when non-empty (takes priority over the view hints), set on commit/discard/save/branch success ("committed",
  "discarded", "saved <name>", "on <branch>"), clear on next keypress. Good action feedback.
- `d` deps on a Tree leaf: get the selected item's hash (searchExactMatch -> item.entity.hash), call
  Builtin.depsGetDependencies branchId hash (or depsGetDependents with loc+kind), resolve names, show in the
  reader (isCode=false).
- History `m` merge / `r` rebase with y-confirm (SCM.Merge/Rebase are AppState cmds — port the core like commit).
- F: extract UI.ListView from renderTreeList; ANSI-aware truncateVisible in UI.Layout.
FINALIZE at ~16:50 (report Phase 5 section + shortstat + print-md + push + chat + STOP).

## Prior NEXT (15:04)
Done: A B C H D + reader-highlighting (full-screen reader now highlights code via readingIsCode + openReader)
+ login-state-on-Home (accountName). NEXT, in priority for remaining ~1h45:
- G quick wins: "terminal too small" guard (render a centered hint below a min size instead of garbage);
  a transient message/toast row for action feedback ("committed", "discarded", "saved") — needs a State
  field `message: String` (+ set/clear); optional mode color block.
- E rest: `d` deps on a Tree leaf (Builtin.depsGetDependencies branchId item.entity.hash — get hash via
  searchExactMatch; resolve dep hashes to names) -> reader; History `m` merge / `r` rebase (SCM.Merge/Rebase
  are AppState cmds — adapt like commit) with confirm; sync-now.
- F: extract UI.ListView from renderTreeList; a proper ANSI-aware truncateVisible in UI.Layout.
Each: dev-ux-check (twice after a State change) -> tmux -> commit -> push. FINALIZE at ~16:50 (report + print +
push + chat + stop).

## Prior NEXT (14:50)
Done: A B C H D (D = global `/` search -> results in reader; read-only v1, jump-to-result is a follow-up;
`/` re-added to footer). NEXT: item E — surface more touchpoints:
- `d` on a Tree leaf -> deps (Cli.Deps / Query) of the selected item, shown in the reader.
- Home: login state (Auth — who you're logged in as, or "not logged in").
- History: `m` merge / `r` rebase (SCM.Merge / SCM.Rebase) with confirm; sync-now somewhere (Mesh/Resolve).
Also quick wins: extend syntax highlighting to the full-screen reader for CODE (Tree-leaf/Changes Enter) — needs
a way to know the reader content is code (add a `readingIsCode: Bool` to State — two-build pass — or store a
small tagged reading type). Then F (ANSI-aware printAt truncateVisible + UI.ListView extract), G (toast/message
row, "terminal too small" guard, mode color block). Commit+push each; keep footer honest.

FINALIZE at ~16:50: update main/notes/cli-ux-workbench-report.md (Phase 5 section), refresh shortstat, print-md,
push, chat message, STOP loop.

## Prior NEXT (14:46)
Items A + B + C + H DONE. C: list+preview splits for Changes/History/Docs/Resolve (previewLines unifies
pane+scroll+reader; History uses a cheap commitSummary, full ops behind Enter; switchView resets focus). H:
renderPreview syntax-highlights code views (Inspect + Changes) via SyntaxHighlighting.highlightCode with the
fit-or-plain guard (print highlighted only when the plain line fits, else plain-truncated — no ANSI truncation).
NEXT: item D — global search `/`. An overlay/input that searches packages (Packages.Search or Query) and lets
you jump to a result (set location + view). Re-add ("/", "search") to the global footer once it works. Then:
extend highlighting to the full-screen reader for code (Tree-leaf/Changes Enter) — track content kind or add a
`readingIsCode` flag; the editor too if feasible. Then E (touchpoints: deps `d` in Inspect, branch merge/
rebase, sync-now, login state on Home), F (ANSI-aware printAt, UI.ListView extract), G (toast row, too-small
guard, mode color block). Commit+push each; keep footer honest.

## Old NEXT (done)
Items A + B DONE. NEXT: item C — list+preview splits (IDE 3-panel) for Changes / History / Docs / Resolve,
reusing UI.SplitPane + the existing detail fns (changesSourceText / commitOpsText / topic content / conflict
detail). Left pane = the list (dominant ~55%), right = preview of the selected item; Tab toggles focus; the
preview scrolls when focused. This makes those views dense + useful, not just framed lists. Verify each in
tmux. Then H (syntax highlighting in readers — mind the ANSI/printAt truncation), D (search `/`), E (more
touchpoints), F (components: ANSI-aware printAt, UI.ListView), G (toast row, too-small guard, mode color).
Commit + push after each. Keep the footer honest (only advertise working keys).

## Log
- Rebase: already current (github/main = upstream = 17eb99eca #5685; 0 behind).
- Container misrouting fixed (peaceful_knuth restarted).
