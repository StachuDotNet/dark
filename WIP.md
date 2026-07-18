# WIP — CLI UX workbench implementation

The loop reads THIS file first, does the NEXT ACTION, updates it, reschedules. Be ruthless: ship small
working increments, keep the tree loading, commit often. Autonomous — the user is asleep; make every call,
never wait.

---

## PR SUMMARY — the CLI UX "workbench" (branch `cli-ux-workbench` off `github/main`)

A new full-screen home for the `dark` CLI: `dark` (no args) now opens a framed, navigable **workbench**
instead of a bare prompt. Implements the design in `main/notes/cli-ux/`. ~46 commits, built bottom-up and
verified on screen at each step.

**Try it:** `./scripts/run-cli` (no args) → the workbench. `DARK_CLASSIC=1 ./scripts/run-cli` → the old
prompt (nothing lost). With args (`status`, `eval`, `tree`, …) → unchanged, non-interactive. Also `dark
workbench` / `dark wb`.

**What's there.** A shared frame (tab bar with the active view marked · view-aware breadcrumb · honest
per-view key hints) around a body that switches per view. 11 views wired from real command data:
- Home (dashboard: branch, WIP + recent WIP names, last commit, owner count)
- Tree (package navigation, `→` into / `←` up) | Inspect (right pane: live source of the selection)
- Changes (WIP items) · History (commits) · Resolve (sync conflicts) · Mesh (tailnet, offline-safe) ·
  Runs (traces) · Services (daemons/apps) · Docs (topics).
- Deferred, shown as "coming soon": Edit (needs a multiline editor), Agents (data source is a mock render),
  Things (needs a type arg / no generic value list).
Interactions: `↑↓` move / scroll (focus-aware), `→`/`Enter` descend or open, `Tab` focus Tree↔Inspect,
`1`-`9` + `[`/`]` switch views, a reusable full-screen **reader** (`Enter` opens a Tree leaf's source, a
History commit's ops, a Changes item's source, a Docs topic; `?` shows the keymap; `↑↓` scroll, `esc`
close), a viewport scrollbar, and graceful empty/error states throughout.
**Write actions** (single-line input mode, `esc` cancels): Changes `c` commit-all (message → `SCM.commit`),
Changes `x` discard-all (y-confirm), History `b` new-branch (create + switch) / `s` switch-branch-by-name.
All verified end-to-end through the UI.

**New/changed files:** `cli/apps/workbench/{frame,app}.dark` (the view), `cli/ui/splitpane.dark` (focus-aware
two-pane split), `cli/ui/layout.dark` (+`hstack`/`distributeCols`/width combinators), `cli/core.dark` (the
no-args → workbench flip + `workbench` command). Dev helpers (not product): `dev-ux-check` (reload+error),
`dev-drive` (PTY visual-verify).

**Honest state / not done:** Read + basic write. Committing / discarding / branching work from the UI;
**Edit (authoring) is deferred** — it needs a real multiline editor + a TUI-friendly save path (the `fn`
create fn prints to stdout and wants the full CLI AppState), so the Edit tab shows a placeholder pointing at
the `fn`/`type`/`val` commands. Also deferred: Agents (its data source is a mock render), Things (needs a
type arg / no generic value list), and item rename (no clean API — only branch rename exists). Mesh/Runs
show "unavailable/empty" here (no tailscale, no traces). Commit op-render capped at 20 (seed "Init" commit
has 10k+ ops). This is a solid, reviewable foundation with the frame + component seams (SplitPane, reader,
input mode, view dispatch) in place to grow Edit and the rest onto.

---

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
- [x] P1 FRAME — DONE. hstack/distributeCols; SplitPane; Frame (tab bar + breadcrumb + key hints); workbench
      SubApp with Tree|Inspect split (live source pane, Tab focus); `dark` no-args opens it (DARK_CLASSIC=1 =
      classic prompt). Verified on screen via ./dev-drive. Commits 5053e91f4…4a82936cf.
- [ ] P2: Home dashboard + Changes (lift SCM.Review.App into SplitPane) + History (branches/commits/ops).
- [ ] P3: `:` CommandBar (MainPrompt in an overlay) + `/` global search.
- [ ] P4: MultilineEditor → Edit + Scratchpad.
- [ ] P5: Resolve (wire the conflictDispatch seam + conflicts.dark).
- [ ] P6: Mesh, Agents, Runs, Services, Things, Docs.

## Keymap/badge = 91 is authority
x=destroy, d=diff/detail, r=rename-or-rerun; Ctrl+Tab=views/Tab=panes; ?=HelpOverlay; badge set frozen (91 §4).

## NEXT ACTION
P2 — make the workbench a real daily driver. Order (each small, verify with ./dev-drive, commit):
1. DONE ✓ Tree viewport scroll (stateless bottom-anchor: scrollOffset = max 0 (selected-visible+1)). Verified
   in a 14-row window: DOWN×18 into Darklang scrolls, `> Tailscale/` stays visible. (dev-drive now honors
   DEV_DRIVE_ROWS/COLS + shows only the final frame.)
2. DONE ✓ Inspect-pane scroll (detailScroll; focus-aware ↑↓; reset on selection change). Verified: Tab into
   Inspect + ↓×6 scrolls a fn's source, selection unchanged.
3. DONE ✓ Changes view (activeView=4): view-aware `items` (digit-switch reloads via `itemsForView`); WIP list
   from `SCM.PackageOps.getWipItems`; "✓ working tree clean" empty state. Verified both (created a WIP fn →
   showed "WbTest demo"; discarded → clean). v1 = list only; diff/source detail is a follow-up.
4. DONE ✓ History (view=5, digit 6): getCommitsWithAncestors → commit rows (shortHash + msg + N ops). Verified
   (shows the Init commit). Also DONE ✓ `[`/`]` cycle all 13 views (digits only reached 1-9).
5. Read-only views wired so far: DONE ✓ Resolve(6, conflicts), DONE ✓ Docs(12, topics). Still to wire:
   a. MESH (view=7): tailnet devices. `Darklang.Tailscale.status ()` — but grep found NO `let status` in
      packages/darklang/tailscale/. FIND the real API: `grep -rn 'Tailscale' packages/darklang/cli/devices.dark`
      shows how devices.dark calls it; follow to the module. If it returns a raw multi-line string, split to
      lines as body items. If the API is unclear/needs network, SKIP Mesh (leave coming-soon) and move on.
   b. DONE ✓ SERVICES (view=10): Apps.Registry.available → daemon/app list. Verified.
   c. THINGS (view=11): `find-values`/ValueSearch by type — lower priority (needs a type arg); skip for now.
6. DONE ✓ Home dashboard + DONE ✓ default landing = Home + DONE ✓ view-aware breadcrumb.
7. DONE ✓ Runs (Builtin.tracesList; empty "no runs yet"). DONE ✓ per-view keyhints (hintsForView).
8. DONE ✓ Mesh (Tailscale.status behind safe Ok/Error wrap; "tailnet unavailable" when no tailscale). Verified.
9. NEXT — 10/13 views live (Home,Tree,Inspect,Changes,History,Resolve,Mesh,Runs,Services,Docs). Remaining views
   Edit/Agents/Things are DEFERRED (need MultilineEditor / mock render / a type arg). So pivot to POLISH — pick
   one per fire, all low-risk:
   DONE ✓ a. scrollbar  b. Docs reader  c. `?` help  d. full SWEEP (all views clean) + Home plural fix.
   DONE ✓ e. HISTORY detail: Enter on a commit → its ops in the reader (commitOpsText via PackageOp.packageOp,
      capped 20). Verified.
   DONE ✓ f. CHANGES detail: Enter → WIP item's source in the reader (name is the FULL path; split → mods+leaf →
      searchExactMatch → PrettyPrinter.packageFn/Type/Value). Verified.
   DONE ✓ h. Tree leaf Enter → source in reader (modules still descend). Verified. Enter now opens content
      consistently across Tree/Changes/History/Docs.
   DONE ✓ g. richer Home. DONE ✓ i. final sweep (clean) + PR summary (top of WIP).
   READ-ONLY WORKBENCH COMPLETE. Now: WRITE ACTIONS (the next real frontier of the design).
   DONE ✓ P3.1: commit from Changes. Single-line INPUT MODE (State.input: Option<InputState{prompt,text,action}>
   + State.accountId threaded from cliState.accountID). `c` on Changes → "commit message:" prompt; type; Enter →
   SCM.PackageOps.commit → reload; Esc cancels; no-account → "not logged in" line. VERIFIED END-TO-END (created
   a WIP fn, committed "wbcommit" via the workbench, log shows it, tree clean). THE WORKBENCH CAN NOW WRITE.
   DONE ✓ b. Changes `x` discard (y-confirm → SCM.PackageOps.discard). Verified end-to-end.
   Write actions so far: commit (c), discard (x) — both on Changes, both verified.
   DONE ✓ a. BRANCH ops from History: `b` create+switch, `s` switch-by-name (Darklang.SCM.Branch.create/getByName;
      full paths to dodge the runtime-resolution trap). Verified incl. the "no branch" error path.
   Write actions now: commit (c, Changes), discard (x, Changes), branch create/switch (b/s, History).
   DONE ✓ Edit placeholder (informative, points at fn/type/val) + PR summary updated (write actions; Edit
   deferred honestly). createFnInline PRINTS to stdout + wants full AppState → can't call it in the TUI.
   NEXT — build EDIT properly (multi-fire; verify each step; if 2D editing gets janky, fall back to append-only
   or STOP — don't ship broken):
   1. DONE ✓ MULTILINE BUFFER: ui/editor.dark (module Darklang.Cli.UI.Editor) — Buf {lines,row,col} + insertChar/
      newline/backspace(+join)/moveLeft/Right/Up/Down/fromText/toText/currentLine. Eval-verified (insert, newline
      split, backspace join). Ref from workbench as `UI.Editor.*` (like UI.SplitPane).
   2. NEXT: State.editing: Option<{ nameStr: String; buf: UI.Editor.Buf }>. Render it full-body like the reader but with a
      visible cursor at (row,col) — draw each line, put a reverse-video block at the cursor. Reuse renderReading-
      style windowing for scroll.
   3. handleKey editing branch (outermost, before input/reading): printable→insertChar, Backspace, Enter→newline,
      arrows→move, Ctrl+S (modifiers.ctrl && key==S)→SAVE, Esc→cancel (confirm if non-empty? optional).
   4. SAVE locally (don't call createFnInline): fullSource = "let {name} {bufText}"; Builtin.parserParseToWritten
      Types → build AddFn+SetName ops (copy the 6 lines from fn.dark createFnInline) → SCM.PackageOps.add branchId
      ops. On parse error, show it in the buffer footer, DON'T save. On ok → back to Tree, reload.
   5. Wire Tree `n`: input "new fn name (owner.Mod.name): " → on enter, open editing with buf = one line
      "(x: Int): Int =" placeholder or empty. VERIFY: author a fn in the workbench, see it in Changes, commit.
   Step 1 (the buffer + pure ops, eval-verified) is the safe first increment. Do that, commit, then step 2.
   1. State: add `input: Stdlib.Option.Option<InputState>` where InputState = { prompt: String; text: String;
      action: String } (action tag e.g. "commit"). Init None in execute. (double reload — type change.)
   2. handleKey: guard at TOP like `reading` — if `input` is Some: printable char → append to text (keyChar);
      Backspace → drop last; Enter → perform the action (commit) then clear input; Esc → clear input. Nothing
      else. (Put this branch BEFORE the reading branch, or combine.)
   3. In the None/normal branch, Changes (activeView==4): keyChar "c" → set input = Some { prompt="commit msg: ";
      text=""; action="commit" }.
   4. render: when input is Some, draw the prompt+text+cursor on the key-hint row (or a line above it):
      Frame.renderKeyHints r (input.prompt ++ input.text ++ "_"). Reader/normal hints otherwise.
   5. commit action: needs accountId. THREAD it: add `accountId: Stdlib.Option.Option<Uuid>` to State; in
      execute set it from cliState.accountID. On Enter with action=="commit": match accountId with Some a ->
      SCM.PackageOps.commit a state.branchId text (check its real signature/return!) ; None -> set input=None +
      maybe a toast/line "not logged in (DARK_CLASSIC: login)". After commit, reload items (Changes) + selected=0.
   6. Verify: create a WIP fn, `dark wb`, 5 (Changes), c, type a msg, Enter → committed (status clean; History
      shows it). Test the no-account path too. Discard/cleanup any test artifacts.
   Grep first: `grep -rnA6 'let commit' packages/darklang/scm/packageOps.dark` for the exact signature/return.
   Keep it small — input infra first (verify a dummy), then the commit wiring.
Keep each fire small + verified. AGENTS/EDIT/THINGS stay deferred (mock render / MultilineEditor / type arg).
Digit map: "1"→Home(0) … "9"→Agents(8); `]`/`[` reach Runs(9)/Services(10)/Things(11)/Docs(12).

## Status: P1 COMPLETE ✓ — `dark` opens the framed Tree|Inspect workbench (verified on screen; classic prompt
   behind DARK_CLASSIC=1; with-args commands unaffected). Commits 5053e91f4…4a82936cf. Now on P2.

## Gotchas learned (append as you hit them)
- Typed lambda params are NOT supported: `fun s -> …` only, never `fun (s: String) -> …` (PARSE-UNCLOSED).
- Module-level VALUES use `val name = …` (NO type annotation). `let` is only for functions + local bindings.
  (e.g. `val viewNames = [ … ]`, not `let viewNames : List<String> = …`.)
- Name resolution from `Darklang.Cli.Apps.Workbench.*`: use `UI.Layout.…` (not bare `Layout`) — the ancestor
  chain hits `Darklang.Cli`, from which `UI.Layout` resolves but `Layout` doesn't. `Colors` resolves bare.
- `UI.Layout.printAt` truncates by String.LENGTH, which counts ANSI escape bytes — so a *colored* string near
  the right edge (small maxLen) gets eaten. For single-glyph colored output at an edge (scrollbar, borders),
  print DIRECTLY via `Colors.moveCursorTo` + the colored string (like SplitPane.drawBox), not printAt.
- Module VALUES (not fns) → `val name = …` (no type annotation). Cross-module SCM.PackageOps etc. resolve via
  fall-through to Darklang.SCM even from Cli modules (Darklang.Cli.SCM.PackageOps doesn't exist).
- A CLEAN RELOAD does NOT catch unresolved package-fn refs — they error only at RUNTIME. Always EXERCISE the
  code path (eval or dev-drive), not just reload. Ex: `PrettyPrinter.ProgramTypes.packageOp` loaded fine but
  raised "not found" at runtime — packageOp is nested in `module PackageOp`, so the path is
  `PrettyPrinter.ProgramTypes.PackageOp.packageOp`. Check indentation to find the real module path.
- `packageOp branchId op` does per-op branch lookups — CAP how many you render (20) or big commits (10k-op
  seed) are slow.
- NO `let private` — Dark has no `private` modifier ("Module value declarations must use 'val'"). Just `let`.
- A failed reload can leave the DB in a TRANSIENT bad state (applyOps/insert error on the NEXT reload). RETRY
  `reload-packages` once before assuming corruption — it often clears on the 2nd pass. (True corruption →
  `migrations run` / restore seed.db.)
- A non-loading .dark aborts the whole reload → always `./dev-ux-check` after each edit; read the real error
  via `grep -niE 'error\\[|Unresolved|expected|not found|not supported' rundir/logs/packages.log | tail`.

## Log (newest first)
- 2026-07-18 07:53 — P3.5 (Edit step 1): built ui/editor.dark — a pure multiline text buffer (Buf{lines,row,col}
  + insert/newline/backspace+join/motion). Eval-verified all. Hit `let private` (invalid) + a transient reload
  DB error (cleared on retry — recorded both gotchas). Commit c3ebbdab3. Next: State.editing + cursor render (step 2).
- 2026-07-18 07:45 — P3.4: made the Edit tab an informative placeholder (points at fn/type/val + classic prompt)
  and updated the PR SUMMARY to reflect the 3 write actions + honest Edit-deferred state. Decided createFnInline
  can't be called in-TUI (prints + needs full AppState). Commit 54ffb2deb. Next: build Edit properly, starting
  with an eval-verified multiline buffer (step 1). If 2D editing gets janky, fall back / stop — don't ship broken.
- 2026-07-18 07:37 — P3.3: branch ops from History — `b` create+switch, `s` switch-by-name (input mode;
  Darklang.SCM.Branch.create/getByName full-path). Verified: created+switched to "wbbr", switched by name,
  "no branch 'zzz'" error path. Commit d2ebc36b8. 3 write actions now (commit/discard/branch). Next: Edit-lite
  (crude multiline new-fn editor) — or stop at polish if it gets janky. (Left a test branch 'wbbr' in local DB;
  harmless, not in git; reload may clear it.)
- 2026-07-18 07:28 — P3.2: discard from Changes (`x` → "type y then enter" confirm → SCM.PackageOps.discard).
  Verified end-to-end (created WbTest.demo2, discarded via workbench, tree clean). Safe: only "y" discards, Esc
  cancels. Commit 703796ca1. Two write actions now (commit + discard). Next: branch ops from History, then Edit-lite.
- 2026-07-18 07:22 — P3.1 FIRST WRITE ACTION: commit from Changes. Built single-line input mode (State.input +
  accountId) + performInputAction; `c` → commit-message prompt → SCM.PackageOps.commit. VERIFIED end-to-end
  (committed "wbcommit" via the workbench UI; log confirms; tree clean). Commit 1333ea692. The workbench is no
  longer read-only. Next: Tree `r` rename, Changes `x` discard (with confirm). (Test fn/commit are ephemeral —
  reload rebuilds the DB from disk.) NOTE: `fn` create is slow (~>1min) — run it alone, not in a compound.
- 2026-07-18 07:09 — FINAL PASS done: swept the deep interactions (Docs Enter/scroll/esc, all Enter drill-ins,
  `?`) — all clean, no regressions. Wrote the PR SUMMARY at top of WIP. Commit 66ae67a43. READ-ONLY WORKBENCH
  COMPLETE (11 views, drill-in reader, help, scrollbar, richer Home). Next frontier: WRITE ACTIONS — starting
  with commit-from-Changes (needs a single-line input mode). Plan in NEXT ACTION.
- 2026-07-18 07:03 — P2.17: richer Home (recent WIP item names + "last commit: …" line via getCommitsWithAncestors
  head). Verified on clean tree. Commit 11cbebd88. Next: final sweep + PR summary at top of WIP.
- 2026-07-18 06:56 — P2.16: Tree leaf Enter → source in the reader (modules still descend). Verified. Enter is
  now consistent (opens content) across Tree/Changes/History/Docs; reader mode reused for all. Commit e894ce188.
  Next: richer Home, then FINAL sweep + PR summary.
- 2026-07-18 06:47 — P2.15: Changes Enter → WIP item source in the reader (changesSourceText; WIP name is the
  FULL path → split to mods+leaf → searchExactMatch). Verified via eval + dev-drive (created/discarded a test
  fn). Commit 9d34fa4ea. Reminder used: reload-packages WIPES WIP items — recreate WIP AFTER any reload to test.
  Next: richer Home / Tree-leaf Enter → source / final sweep.
- 2026-07-18 06:36 — P2.14: History Enter → commit ops in the reader (commitOpsText). Hit the packageOp nested-
  module gotcha (clean reload but runtime "not found" → real path PrettyPrinter.ProgramTypes.PackageOp.packageOp)
  + capped renders at 20 (10k-op seed commit was slow). Verified via eval + dev-drive. Commit 0fddd3032. Next:
  Changes Enter → item source in reader.
- 2026-07-18 06:27 — P2.13 QA: full dev-drive sweep — Home/Tree-split/coming-soon(Edit/Agents/Things)/all
  render clean, no glitches. Fixed Home "1 commits"→"1 commit" (plural helper). Commit 3af372f22. Next: History
  Enter→commit ops in the reader (reuse reading mode).
- 2026-07-18 06:21 — P2.12 polish: `?` help overlay (full keymap via reusable reader mode). Verified. Commit
  e62cb634b. Next: full dev-drive SWEEP across all views (QA for glitches), then History detail pane / richer Home.
- 2026-07-18 06:11 — P2.11 polish: Docs Enter-to-read — a reusable full-body reader (State.reading; ↑↓ scroll,
  esc/q close). Verified (Enter on for-ai showed the doc content). Restructured renderBody→renderViewBody +
  dispatcher to avoid a nested-match indentation trap. Commit a98524e36. Next: `?` help overlay (reuses reader).
- 2026-07-18 06:03 — P2.10 polish: scrollbar thumb (▐) on overflowing lists. Hit + recorded the printAt-truncates-
  colored-strings gotcha (print edge glyphs directly via moveCursorTo). Verified. Commit ad9348a05. Next: Docs
  Enter-to-read (topic.content into a scrollable pane).
- 2026-07-18 05:55 — P2.9: wired Mesh (Tailscale.status behind Ok/Error wrap; "tailnet unavailable" empty).
  Verified: safe, no crash when tailscale absent. 10/13 views live. Commit a5be68e98. Next: polish (scroll
  indicator, Docs Enter-to-read). Edit/Agents/Things deferred.
- 2026-07-18 05:48 — P2.8: wired Runs (Builtin.tracesList, empty "no runs yet") + honest per-view keyhints
  (hintsForView). Verified. 9/13 views live. Commit e1ede1a32. Next: Mesh (offline-safe wrap) or polish
  (scroll indicator / Docs Enter-to-read). Agents/Edit/Things deferred.
- 2026-07-18 05:40 — P2.7: default landing = Home (execute activeView 1→0); view-aware breadcrumb (Home /
  package path / "ViewName — N items"). Verified (opens on Home; History crumb = "History — 1 items"). Commit
  14f96237c. Next: wire Runs (traces) if clean API, else Mesh/skip; then per-view keyhints.
- 2026-07-18 05:33 — P2.6: Home dashboard (WIP/commits/owners summary via getWipSummary+getCommitCount) +
  Services view (Apps.Registry.available). Both verified. 8 views live now (Home/Tree/Inspect/Changes/History/
  Resolve/Services/Docs). Commit 2e600e967. Next: optional default→Home; then Runs/Mesh; then polish (Enter, breadcrumb).
- 2026-07-18 05:25 — P2.5: wired Resolve (Sync.Conflicts.list; "nothing to resolve" empty) + Docs (allTopics
  topic list). Both verified via dev-drive. 6 views live now (Tree/Inspect/Changes/History/Resolve/Docs).
  Commit 3080ec551. Next: Services (Apps list) + Home dashboard; Mesh only if the Tailscale API is clean.
- 2026-07-18 05:16 — P2.4: History view wired (getCommitsWithAncestors; commit rows). + `[`/`]` view cycling
  so all 13 views are reachable (digits only hit 1-9). Verified both. Commits 7207468ac, f98e60386. Next: wire
  read-only Resolve/Mesh/Docs/Services (breadth), then Home dashboard.
- 2026-07-18 05:06 — P2.3: Changes view wired (view-aware items via itemsForView; digit-switch reloads body;
  getWipItems; clean-state message). Verified populated + empty (WbTest.demo shown, then discarded). Commit
  aaf33ee41. Note: `discard` needs `printf 'y\n' | …` non-interactively. Next: History view (press 6).
- 2026-07-18 04:55 — P2.2: Inspect-pane scroll done (detailScroll, focus-aware ↑↓, reset on selection change).
  Verified via dev-drive (Tab+↓×6 scrolls fn source in Stachu.Parser). Commit 0e30081be. Next: wire Changes view.
- 2026-07-18 04:46 — P2.1: Tree viewport scroll (stateless). Improved dev-drive (final-frame-only capture +
  DEV_DRIVE_ROWS/COLS). Verified scroll in a 14-row window (DOWN×18 → `> Tailscale/` visible, list scrolled).
  Commit 32830eb06. Next: Inspect-pane scroll (P2.2), then wire Changes/History/Home.
- 2026-07-18 04:38 — **P1 COMPLETE**. Flipped `dark` no-args → workbench (core.dark; DARK_CLASSIC=1 = classic).
  Verified all 3: no-args→workbench frame; `status`/with-args still print; DARK_CLASSIC=1→classic prompt.
  Commit 4a82936cf. `dark` now opens the framed, navigable Tree|Inspect workbench. On to P2 (scroll, then wire
  Changes/History/Home).
- 2026-07-18 04:27 — P1: Tree|Inspect body split done (SplitPane in renderBody; renderTreeList + detailLines
  + renderDetail; State.focus; Tab toggles). Verified: split renders both panes; source-fetch path returns
  real List.map source. Commit 601183329. LAST P1 step next: flip `dark` no-args → workbench (keep DARK_CLASSIC).
- 2026-07-18 04:18 — P1: VISUALLY VERIFIED the workbench. Built `./dev-drive` (PTY frame-dump helper; host
  has no expect, run-in-docker too slow). Frame renders correctly (tab bar w/ Tree active, breadcrumb + branch
  + ✓synced, tree body Darklang/Feriel/Stachu, key hints, status bar); ↑↓ + → descend work (→ into Stachu
  lists its submodules). Day-1 north star essentially hit. Commits 42567910a. Next: body split + `dark` flip.
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
