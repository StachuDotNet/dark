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

## NEXT ACTION (updated 14:46)
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
