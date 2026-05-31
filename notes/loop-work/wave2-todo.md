# Wave 2 — spec the path from `main` to "print-md stable & syncing"

This is the **living worklist** for the 24h loop. It is a reorganized form of
`more-feedback-raw.md` (frozen original); all content from there is preserved here,
reordered into a dependency-respecting processing order.

## The goal

Spec **everything** between `main` and *"the `print-md` script is stable and syncing
across various Dark instances."* Come Sunday, Stachu does a final review, adjusts a
few small things, and says "go" in a fresh session against the spine doc to start
**pre-S&S** work — and should return confident there's a real plan to print-md sync.

**The spine:** `stable-and-syncing/steps-towards-print-md-sync.md` (renamed from
next-steps) — detailed, step-by-step, references the other docs, so a future AI can
follow it. **For each intended effort/PR: sketch the shape** — high-level *uncompiled*
code, empty fn bodies, pseudocode; identify good types, tests, needs, UX touchpoints,
and prereqs to pull out into *earlier* efforts. Iterate on those sketches hard.

## How this loop works (process — read every pass)

- 5-min loop, work in **chunks**, commit along the way, **never push**.
- **Recovery (if interrupted — quota, crash, reboot):** this file + git history ARE the
  durable state. Each pass commits, so an interruption loses at most the current
  uncommitted chunk; on restart, re-read this file and continue from the remaining todos.
  Quota exhaustion usually just *pauses* (the process stays alive) → the in-session loop
  resumes on its own when the limit resets. For a hard process death, the backstop is the
  OS-cron watchdog `notes/loop-work/loop-watchdog.sh` (if enabled) — it **resumes this
  session warm** (`--resume`, full context, not cold) and only fires when the live loop's
  heartbeat is stale, so it never double-runs.
- **Each pass, `touch /tmp/dark-wave2-loop.heartbeat`** — this tells the watchdog the live
  session is alive so it stays out of the way. (Cheap; do it with the pass's commit.)
- **When a todo is genuinely done — well and correctly — DELETE it from this doc.**
  This file shrinks toward empty; empty = done.
- **Add** newly-surfaced todos under "Discovered" as you go; check/delete them too.
- **Priority: pre-S&S and S&S.** Tighten them as much as possible — "enough to
  implement." Everything else can rest / be roughed-in.
- **Dependency rule (enforce constantly):** a doc may only reference its own bucket or
  *earlier* buckets. S&S must not reference PDD; etc. (See bucket order below.)
- **TIGHT docs, not long.** Tighten the *prose*; **keep code specs and step-by-step
  instructions** — those are the product, not bloat. Cut by: not repeating yourself, not
  narrating what a doc *used to* say, and not cross-referencing other docs more than
  necessary (a reference should earn its place). **Each doc < 1000 LOC, and total volume
  should feel light.** Little content should live in >1 file; end with *fewer* `.md` files
  than we started. If a doc is getting long, tighten or split — don't grow it.
- **Show, don't just tell — include VISUALS.** Embed fake **CLI/TUI experiences** (ASCII mockups of
  the actual terminal session a user would see), **dense code snippets** that show off the design
  (real type definitions, the shape of a fn, an op-stream fold), and small diagrams. These are tight
  and high-signal — they *are* the tight-doc style, not padding. A spec should be reviewable by
  *looking* at it. Prefer a 12-line mocked `dark apps` session over a paragraph describing it.
- **`main` is the source of truth — not this branch.** The `pdd` branch is a research
  **spike** (~368 files differ from `main`); many primitives here exist *only* in the spike.
  Verify every codebase claim against real `main` (`git show main:path`), never the working
  tree. Label spike-only things as spike-only. A clean idea on a real `main` primitive beats
  a slick one resting on something that only exists in the spike.
- Sandbox: **edit** only under `notes/loop-work/` (the `outputs/` tree + this file). Never
  **edit** the real `pdd-thinking/` or the Obsidian vault. **Reading `~/vaults/Darklang Dev`
  is allowed** (read-only) for reference — but its details may be **old/wrong**, so favor
  local docs + `main` over the vault wherever they conflict. Never use the section-sign in
  prose (write "section N"). Never `git stash`/`reset --hard`.

## Keep going until Sunday 6pm — perpetual mode (read every pass)

> **PHASE 2 — ACTIVE IMPLEMENTATION (read first).** Phase 1 (DESIGN/SPEC) is done: pre-S&S + S&S
> tight/grounded/dependency-clean, 6 PR specs written + consistent + 3 prototype-validated +
> builtin-count-grounded, sanity/coherence/compose checks, vault sweep, promotion map. Stachu's
> steer: *"there's no reason to ever stop"* + *"work on PRs for pre-S&S steps."* So the ongoing
> work is now **implementing the PRs as real, compiling, tested code** in the isolated clone
> `~/code/dark/loop-fun` (prework/* branches off `main`, isolated `*_loopfun` devcontainer
> volumes). This is **effectively unbounded** — always a next thing: get each PR compiling, fix
> real F# errors, add the .fs + .dark tests the spec lists, flesh out empty fn bodies, then the
> next PR. **Order:** pre-S&S foundations first (EventBus, async Stage A, ops⊥projections,
> conflict-dispatch), then S&S. Each pass: pick up where the last left off (`git log` in loop-fun
> + build state), make real progress, commit on the prework branch, feed findings back into the
> matching `outputs/` PR spec. **Don't stop, don't declare "saturated."** `main/` (pdd) source is
> NEVER edited — all code work is in loop-fun.

The `[ ]` items below remain the design-side standing checks (still passing); the *implementation*
work lives in loop-fun, tracked by its git history + the spec back-references.

When the explicit list is empty, the work is not done — there is always genuine depth
here. **Stay BROAD — don't tunnel into any one mode (especially not just coding prototypes).**
Rotate deliberately across all of: **specs** (PR specs + tightening design docs — these are
the core product), **prototyping** (the loop-fun clone, when a design is solid enough to test
in real code), and **broad-picture thinking** (step back, reflect on the whole design, run the
sanity check). A good run mixes all three; a run that's all-code or all-one-doc has lost the
plot. Rotate through these (and write what you do as new "Discovered" todos):

- **Deepen the PR-shape sketches.** For every effort, push the sketch further: real type
  definitions, the shape of key fns (empty bodies / pseudocode), the test list, UX
  touchpoints, the exact prereqs to pull into earlier efforts — **and a visual** (a fake
  CLI/TUI session, a dense code block, a small diagram) wherever it shows the design better
  than prose. These sketches are the product — they get *sharper*, not longer.
- **When a design is ~100% solid → escalate the sketch into a PR spec.** This is where the
  real time goes; it's the highest-value perpetual work. Once a design is genuinely settled
  (not before — half-baked specs waste the effort), write/expand a **per-PR spec** using the
  template below. Iterate on them: compare an above-PR against the below-PR it depends on
  (do they hand off cleanly? is a prereq missing from the lower one?), and **think through
  problems Stachu hasn't raised** (failure modes, migration order, data shape, perf cliffs).
  A PR spec is *concrete*: which `.fs` files change and how, which `.dark` files change, the
  test plan for each step. Don't pad — concrete and dense, still <1000 LOC.

  **PR-spec template** (one per intended PR; lives in the effort's own bucket so the spine
  links *down* to it):
  ```
  # PR: <name>     (effort N of the spine)
  Goal            one sentence; what's true after this merges that wasn't before.
  Prereqs         which earlier PRs must land first; what to pull *into* them.
  .fs changes     THE MOST IMPORTANT THING TO MODEL — spend the effort here.
                  Lead with LibExecution (RuntimeTypes, Interpreter, Execution, ...).
                  Call out ProgramTypes changes EXPLICITLY where relevant: PT is the
                  serialized AST/type surface, so a PT change ripples to serialization,
                  ProgramTypesToRuntimeTypes, and the embedded package-ref hashes (needs
                  the two-build pass). Each file + how it changes (new type, changed
                  signature, new module). Ground every path against `main` (git show main:path).
  .dark changes   which package items / .dark files change; new builtins exposed.
  SQL/schema      only when a step adds/changes DURABLE state: the table + migration.
                  Sometimes it matters, often it doesn't — say which, don't invent rows.
  New types/fns   the key type defs + empty fn bodies / pseudocode (the shape).
  Test plan       per step, BOTH kinds: .fs tests (which test file; add vs adjust) AND
                  .dark tests (which package test; add vs adjust) — say which exist vs new.
                  Plus the observable done-signal: "how do we know step K works?"
  CLI impact      CHECK EVERY PR (even if "none"): does the `dark` CLI gain/change a
                  command, flag, or output? What breaks for an existing CLI user?
  UX change       state it concretely — what does the user SEE differently before vs
                  after? A fake before/after terminal session. "Nothing user-visible" is
                  a valid answer, but say it explicitly.
  Risks/unknowns  problems not yet raised; failure modes; what could force a redesign.
  Above/below     what the PR above expects from this one; what this expects from below.
  ```
- **Build out the spine.** `steps-towards-print-md-sync.md` is the ordered list of those
  PRs — a sequence a future AI can execute step-by-step, each step linking *down* to the
  PR spec + design doc it needs. Spine = the index; PR specs = the detail.
- **Adversarial gap-hunt.** Re-read across docs for cross-doc tensions, missing pieces,
  unstated assumptions, and dependency-rule violations. Fix what you find.
- **Top-to-bottom sanity check (every few passes).** Step back from the individual docs and
  read the whole thing as one design. Do the docs *make sense together*? Are we going insane
  / over-engineering? **Will this actually work as a product?** North star: **as universal
  and boring-reliable as `git` and `sqlite`** — something people reach for without thinking,
  that just works, that you'd build a hundred other things on. Ask plainly: *what's getting
  in the way of that?* (Too many moving parts? A primitive that's too clever? A story that
  only works for print-md and nothing else?) Write the answer somewhere durable
  (`meta/sanity-check.md` or Discovered) and feed the gaps back as todos. This is worth
  *hours* over the run — not a checkbox; keep returning to it.
- **Tighten + consolidate.** Reduce lines, merge overlapping docs, fewer files.
- **Re-verify against `main`** (not the spike tree) any codebase claim a sketch leans on.

**Focus the perpetual work on pre-S&S and S&S docs.** That's where the depth should go.
It's fine to do the other buckets and think ahead too, but those are secondary — the
bar to clear by Sunday is a tight, implementable pre-S&S + S&S spec.

**Keep docs TIGHT, not long.** Tight beats thorough-but-bloated. If a doc is getting
long, tighten or split it — do not grow it. PR-shape sketches should be dense and
short: the key types, the empty fn bodies, the test list — not prose padding. Deepening
a sketch means making it *more precise*, not *longer*.

Guardrail (per `looping-preferences.md`): don't *churn* already-good prose for its own
sake. The design space is large, but the product is a tight spec — prefer making
pre-S&S / S&S sharper and shorter over adding surface. Leave a one-line status each pass.

## Today — day plan (2026-05-31, revised ~10:35) — read every pass

**Mode shift (Stachu, mid-morning):** *"don't just prove things can work. tighten things
until they're beautiful and ready to review/merge. remove what we no longer need. look for
existing sync attempts, consolidate."* + *"focus on getting sync working locally between two
instances, one as server."* So the priority is no longer *more prototypes/proofs* — it's
**CONSOLIDATE → MERGE-READY**, then **local two-instance sync**.

**State now.** The prework engine is built + tested but **fragmented across leaf branches**,
and this session added more on separate leaves without re-integrating:
- dist-App / composable MVU: **DONE** — `composable-mvu.dark` **28/28** on
  `prework/composable-mvu` (Counter, Flag, Register w/ conflict+resolve, GrowOnlySet, Log;
  2 compositions; keyed-merge views; **LWW-Register CRDT two-node convergence**). All the
  old dist-App sub-todos are complete. Don't add more app variants — it's proven.
- `capabilities`: `CapabilityAnalysis.fs` adapter (walks real Instructions) — 16/16, on
  `prework/capabilities` only.
- `conflict-dispatch`: `CSyncDivergence` aligned to compose-check's form — 4/4, on the leaf.
- `sync-read-write`: blob content channel (`Blob.missing`/`getMany`) — 39/39, on the leaf.
- These are **NOT yet on `compose-check`** → it's no longer the true whole.

**Sync consolidation findings (already audited this pass — act on them):**
- `Queries.getAllOpsSince` (main, **timestamp** cursor, returns `(op, isWip)`) is **dead — zero
  callers** anywhere. The prework's `Inserts.opsSince` (**rowid** cursor) is a parallel reimpl
  of it. → unify to ONE op-since read; remove the dead duplicate (or reconcile WIP-awareness,
  but WIP-sync is punted).
- `SyncIdempotency.Tests` "cross-store" + "cross-store projection" tests are **raw-SQL
  reimplementations** that LibPmSeam's production `connStore` tests supersede (LibPmSeam's own
  comment says so). → remove the redundant ones; keep the production `connStore` tests.
- Rung 1 of local sync (a receiver resolves the sender's `name→hash`) is **already proven**
  (LibPmSeam connStore test). The engine exists; what's missing is transport + the CLI.

### ~10:35 → ~16:00 — CONSOLIDATE, then local sync — ALL DONE + beyond
1. **[x] Sync consolidation.** Removed dead `getAllOpsSince` (zero-caller timestamp dup of
   `opsSince`); removed the redundant `SyncIdempotency` cross-store projection test (superseded by
   LibPmSeam's production `connStore`); tightened `Sync.fs`.
2. **[x] Re-integrated the fragmented leaves into `compose-check`** (blob channel,
   CapabilityAnalysis, composable-mvu; CSyncDivergence already there). Targeted suites green.
3. **[x] Rung 2 (local two-instance sync) — DONE end to end + DEMOED LIVE.** `Sync.pull`/
   `pullFromFile` (op log + projections + blobs + per-peer cursor), `pmSyncPull` builtin,
   `dark sync pull <file>` command. **Cross-instance proven**: a value authored on a separate
   instance (`DARK_CONFIG_DB_NAME`) pulled into local and resolved (`val syncVal = 42L`).
4. **[x] Rung 3 (cross-machine HTTP) — BUILT + SSRF blocker FIXED.** Wire codec
   (`encodeBatch`/`decodeBatch`) + `applyWireBatch` (13/13); `pmSyncOpsSince`/`pmSyncApplyWire`
   builtins; `Darklang.Sync.Server.router` (via `dark serve`) + `dark sync pull <url>`. Found +
   fixed the SSRF guard blocking loopback + `100.64/10` (tailnet) — `httpClientGetUnsafe` via
   `looseConfig`. **Live HTTP demo pending a controlled env** (headless server-readiness can't be
   probed); code is in place, core sync proven by the rung-2 demo.
> **Session net:** sync went scattered-primitives → consolidated → working-local (demoed) →
> cross-machine-built+fixed. Running the full suite on `compose-check` to confirm it's all green.

### ~16:00 — REVIEW PASS (when the pass nearest 16:00 fires, switch modes)
- [ ] **Tidy/review** the `outputs/` `.md` files: dependency rule, dedup, tightness (<1000 LOC).
- [ ] **Re-test every prework branch** + **full suite on `compose-check`**; record counts.
- [ ] **Write the report**: what's DONE per PR (test counts) + NEXT STEPS. Refresh
  `meta/prework-status.md`; drop a dated review at `meta/review-2026-05-31.md`. Include the
  **consolidation log** (what was removed/unified and why).

### Evening → onward
Finish rung 2 if unfinished; rung 3 framing; keep hunting redundancy across the sync surface
(Merge.fs / Rebase.fs / BranchOpPlayback.fs are existing branch-op machinery — check for
overlap with the sync path). Keep everything merge-ready.

### Standing guardrails (Stachu, this session) — apply every pass
- **Tighten/remove/consolidate beats add.** Before writing a new fn, look for an existing one
  to reuse/extend (the `getAllOpsSince` lesson). No parallel reimplementations.
- **Everything must be merge-ready**, not "proven." No scaffolding left lying around; redundant
  tests get removed once a production version exists.
- **Conservative on deleting SOURCE** (disable-don't-delete bias) — but truly-dead duplicate
  functions + superseded tests are fair to remove, with a one-line why in the commit.
- **Ground before acting** (the recurring catch: rung 1 already done; CSyncDivergence already
  on compose-check). Check the integrated branch + main before building.

## The shape: one goal, what's below it, what's above it

The buckets aren't a flat list — they're **a goal with a stack under it and a stack on
top.** Read them that way:

```
            ┌─────────────────── above the goal (built ON it) ───────────────────┐
   later/             remote-control, P2P, app-fork, hot-reload, distributed liveness
   pdd/               we own the loop (resting — rough-in only)
   good-for-ai-agents/ the CLI as a cohesive tool: AI agents own the loop
            └────────────────────────────────────────────────────────────────────┘
   ════════════════════════ THE GOAL ════════════════════════
   stable-and-syncing/   print-md edited on one Dark instance, SYNCING to the others.
                         (sync, conflicts-and-resolutions, bootstrap; spine threads it all)
   ════════════════════════════════════════════════════════════
            ┌─────────────────── below the goal (the goal NEEDS) ────────────────┐
   pre-s-and-s/          the App model, ops+db architecture, capabilities, core
                         Tailscale, the apps surface, event-bus, async, cli-daemon
            └────────────────────────────────────────────────────────────────────┘
```

- **The goal** is `stable-and-syncing/` — everything is measured against "is print-md
  syncing yet?" The **spine** (`steps-towards-print-md-sync.md`) is the through-line: it
  names the efforts in order and points down into the docs that detail each.
- **Below** (`pre-s-and-s/`) is what the goal *rests on* — foundations that must exist
  before sync is even meaningful. This is also the base that makes the CLI a cohesive tool.
  **Tighten this hardest** — it's the load-bearing layer and the priority.
- **Above** (`good-for-ai-agents/` → `pdd/` → `later/`) is what gets *built on* a syncing
  substrate. Real, but secondary; rough-in and think ahead, don't polish.
- **`meta/`** is off to the side — scratch/cleanup; kill almost all of it after extracting
  the one looping-prefs doc.

**How docs fit together = the dependency rule.** A doc may reference only its own layer or
a *lower* one — never up. So the goal's docs may lean on `pre-s-and-s/`, but `pre-s-and-s/`
must stand alone; `pdd/` may lean on everything under it but nothing leans up into it. The
spine reads top-down (goal → foundations); dependencies point down. Same arrow, both ways:
**lower is more fundamental.**

---

## 0. Setup / structure (do first — unblocks the dependency + dedup work)

> Buckets established + all moves/kills done (pre-s-and-s/, stable-and-syncing/,
> good-for-ai-agents/, pdd/, later/, meta/). README + open-decisions killed; conflicts →
> conflicts-and-resolutions; next-steps → steps-towards-print-md-sync. Remaining structure work:

- [ ] **Dependency pass.** Identify the buckets + their dependencies, then read and
  reread all `.md` until you're *sure* no doc references a higher bucket (S&S ↛ PDD, etc.).
  **Known violations to fix now:** pre-s-and-s docs (esp. distributed-event-sourcing, apps-surface,
  composable-mvu, package-system-layers, capabilities, event-bus, async) currently link UP to
  `stable-and-syncing/` (sync, conflicts), `pdd/`, and `later/` — invert or remove those refs
  (the lower doc should be referenced BY the higher one, not reference it).
- [ ] **Dedup pass.** Same thoroughness: ensure little content lives in multiple files;
  reduce total lines; consolidate/split where clear; end with fewer files.

## 1. pre-S&S (foundations) — PRIORITY, tighten hard


## 2. S&S


## 3. good-for-ai-agents (the tool; base for PDD)


## 4. PDD (we own the loop; resting — rough-in only)


## 5. later

- [ ] **`hot-reload.md` → `later/`** (punt). You *may* think about hot-reload needs and
  shape `async` / projections / op-playback toward a solution that will support
  hot-reloading — but **don't mention hot-reloading** in those docs.
- [ ] **P2P sync, `dark apps fork`, "the App is live/forkable", distributed-app
  liveness** → `later/`.

## 6. meta (cleanup — do the kills only after section-0 extraction; consolidate at the very end)


## Cross-cutting (ongoing every pass)

- [ ] For each intended effort/PR, **sketch the shape**: uncompiled high-level code,
  empty fn bodies, pseudocode; good types; tests; needs; UX touchpoints; prereqs to pull
  into earlier efforts. These sketches are the real product — iterate on them.
- [ ] Keep enforcing the **dependency rule** and **reducing total lines / file count**.
- [ ] Keep **tightening pre-S&S + S&S** until "enough to implement, review, and go."

## Discovered (add todos here as they surface)

- [ ] **`later/dark-virtual-files.md` is ~1200 lines** — over the <1000 bar (only doc that is).
  Tighten/split when convenient; low priority (punted/secondary bucket). Found in 2nd sanity pass.

- [ ] **Prework prototyping track (NEW outlet — validate specs in real code).** Isolated clone
  at `~/code/dark/loop-fun` (off real `main`, branch `prework/event-bus-primitive`). The `main/`
  tree (pdd) is untouched — never edit it or checkout another branch there. **Goal:** implement
  the floor PR specs as real code to validate "enough to implement." Start with the EventBus
  primitive (cleanest leaf). **Build-safety rule:** the devcontainer mounts *named, shared*
  Docker volumes (`dark_build`, `dark_nuget`, …) — a naïve second devcontainer would corrupt
  this session's build. Before compiling in loop-fun, **rename its volumes** in
  `loop-fun/.devcontainer/devcontainer.json` (e.g. `dark_build_loopfun`). Until then, WRITE the
  F# against real source (high-value, zero-risk) and defer the compile to a deliberate pass.


- [ ] **Standing: does each PR stand alone?** (sanity-check finding #3). On each pass, spot-check
  that no doc/effort *requires the whole cathedral* — each primitive must ship independently.
  Flag any that don't. (The spine's floor/vision split is the first cut of this.)

- [ ] **Per-PR-spec contract (apply to every spec; see the template).** Each spec must
  model: LibExecution changes first (esp. **ProgramTypes** where relevant — serialization +
  package-ref-hash ripple); **SQL/schema** only when durable state changes; **both .fs and
  .dark tests** (which file, add vs adjust); **CLI impact** checked every time; and the
  concrete **UX change** (what the user sees before/after). Sweep existing and future specs.
- [ ] **async ↔ event-bus are mutually referencing** (both pre-S&S, allowed). Keep the
  boundary clean: async owns suspension (scheduler/Promise/force), event-bus owns delivery.
  Re-check on each edit that neither re-derives the other's content.
- [ ] **Minimum F# / PT for the core ops+storage layer** (emailed thought #3). What are the
  "core internal tables that own others," the *smallest* F# to support ops+projections+sync,
  and what the **PT** for an op looks like? This is the substance of the ops⊥projections PR
  (spine effort 3) + the EventBus/storage specs — flesh it out as those PR specs deepen.
