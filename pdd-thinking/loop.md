# Substrate Sketches — Loop Plan

> **PREAMBLE FOR THE LOOPER (read this first, every iter):**
>
> You are an iter of a **5-minute** self-paced loop. Your job is to
> sketch the substrate concepts PDD really needs, one bucket per
> iter, into `.md` files in this directory. Concretely:
>
> 1. Find the **`NEXT:`** line below.
> 2. Read the matching `## BN — …` section.
> 3. Skim 2–3 of the listed vault notes (don't exhaustively read —
>    skim for the load-bearing claims, then write). Trust newer/
>    LibMatter-spec material over notes suffixed `-old`.
> 4. Write or update the `.md` deliverable listed in that bucket.
> 5. Tick the bucket's checkboxes as done.
> 6. Update the `NEXT:` line to the next unchecked bucket — or
>    `DONE` if all done.
> 7. Commit (bucket-named message). Don't push.
> 8. Schedule the next wake (**5 min**). If `NEXT: DONE`, **stop
>    scheduling** (B10 is the user-facing propose-push terminus).
>
> **The headline:** conflicts+resolutions (B2) is the most important
> sketch — **it's the gating piece for the SCM + sync work the user
> needs to take care of in the broader project**, not just a PDD
> concern. B3 (sync + stability + bootstrap-without-.dark-files)
> rides directly on top of it. Lean weight there; if the iter runs
> short, prioritize B2 depth over B4/B5.

## Status

**NEXT:** `B5` — Sketch CAPABILITIES.md

## Vault notes worth reading

User warned some are old/stale; prefer dated and LibMatter-spec
material; `-old.md` suffixes and undated docs are suspect.

**Conflicts / Ops / Sync / Bootstrap (most material here):**
- `~/vaults/Darklang Dev/05.Implementation/WIP and Unorganized/specs/LibMatter/` (full set: README, ops.md, ops-hierarchy.md, conflicts.md, sync.md, sync-model.md, db-schema.md, garbage-collection.md)
- `~/vaults/Darklang Dev/05.Implementation/WIP and Unorganized/specs/flows/conflict-resolution.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/README.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/Ops and Conflicts.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/Source Control.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/more thoughts on Source Control.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/dl-scm-distribution.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/dl-2025-11-12-ux-thinking.md` (recent — trust more)
- `~/vaults/Darklang Dev/05.Implementation/Package Bootstrapping/README.md`
- `~/vaults/Darklang Dev/05.Implementation/Package Bootstrapping/dl-bootstrapping.md`
- `~/vaults/Darklang Dev/05.Implementation/Package Bootstrapping/Dogfooding, Bootstrapping.md`
- `~/vaults/Darklang Dev/05.Implementation/Constraints/dl-constraints-conflicts-old.md` (likely stale — last resort only)
- `~/vaults/Darklang Dev/91.Ocean/Research/Blog posts/Conflict-free Replicated Data Types (CRDTs).md`

**Events / Parking / Async / Runtime:**
- `~/vaults/Darklang Dev/05.Implementation/Execution/Design - Async Execution.md`
- `~/vaults/Darklang Dev/05.Implementation/Execution/Runtime.md`
- `~/vaults/Darklang Dev/05.Implementation/Execution/dl-execution.md`
- `~/vaults/Darklang Dev/05.Implementation/Execution/dl-runtime-obs.md`
- `~/vaults/Darklang Dev/05.Implementation/Execution/Interpreter/interpreter rewrite/bonus, follow-ups/parallelism.md`

**Composable MVU / Apps / UI:** (user warned: old thoughts, treat critically)
- `~/vaults/Darklang Dev/05.Implementation/CLI/Apps/README.md`
- `~/vaults/Darklang Dev/05.Implementation/CLI/Apps/dl-frp-mvu.md`
- `~/vaults/Darklang Dev/05.Implementation/CLI/Apps/dl-mvu-frp-impl.md`
- `~/vaults/Darklang Dev/05.Implementation/Future Environments/Canvas UIs/dl-composable-ui.md`
- `~/vaults/Darklang Dev/05.Implementation/Future Environments/Canvas UIs/UIs.md`

**Hot-reload (relevant to B6):**
- `~/vaults/Darklang Dev/05.Implementation/CLI/Apps/Hot-reloading.md`

**Capabilities:**
- `~/vaults/2026-04-19_20darklang_20advisor_20call_20notes.md` (brief — has the "no safety, capabilities model, builtins are the only impure boundary" line)
- No explicit capability spec found in vaults — synthesize from FRONTIER + feedback + first principles

## In-conversation source thoughts to address

These are the user's thoughts from this session + the gaps surfaced
by auditing `feedback-original.md` against already-done work.
**Every item below must be addressed by some bucket.**

From this session's substrate-sketch request:
- [ ] **Sketch capabilities** — covered by B5 (CAPABILITIES.md)
- [ ] **Sketch conflicts+resolutions system** — covered by B2 (CONFLICTS-AND-RESOLUTIONS.md). User said *especially* focus here.
- [ ] **Sketch event/parking substrate** — covered by B4 (EVENT-STREAMS-AND-PARKING.md)
- [ ] **How conflicts+resolutions leads into syncing** — covered by B3 (SYNC-AND-STABILITY.md)
- [ ] **"Stability and sharing"** as the right framing — covered by B3
- [ ] **Removing .dark files from the codebase / package bootstrapping** — covered by B3
- [ ] **Rely on more recent vault thoughts; some notes are old/dumb** — protocol applied in every read-vault step
- [ ] **Is `build-serve-expr.py` still referenced/needed?** — covered by B8 (decide + act)
- [ ] **Sketches go in `.md` files** — every sketch bucket produces an `.md`
- [ ] **The system runs as a loop, 5 min cadence** — set up B1; will end naturally at B11 (~50-60 min remaining after B2)

From feedback-original.md audit — gaps not yet handled:
- [ ] **Highest-level fn in focus view sketches** ("at various points in time," "high-level pretty sketches") — covered by B7 (VIEW-SKETCHES.md, new). FRONTIER mentions the idea but doesn't sketch the views.
- [ ] **Hot reloading from first principles, tight .md** — covered by B6 (HOT-RELOAD.md, new). FRONTIER has a placeholder; user wanted a dedicated doc.
- [ ] **End goal: push the branch so user can pick back up** — covered by B10 (propose-push, new). My memory says never push pdd, but user's feedback explicitly states pushing is the end goal — surface for user confirmation, don't unilaterally push.
- [ ] **Composable MVU apps infra** (feedback line 31) — covered by B7 (COMPOSABLE-MVU.md, new). User upgraded from "not worth digging too deep" → "some composable MVU thinking could be good." Old vault thoughts exist in `CLI/Apps/` — treat critically.

Already addressed by the prior consolidation loop (B1–B9 of feedback.md):
- claims extraction + reframe, algorithm extraction, all deletions
  (anti-pitch, time markers, glossaries, heavy-hitters, project-
  level/smoke-detectors/pithy, acceptance summary, v1/v2/v3
  history, FINAL-REPORT), archive tidy, pitches tightened, FRONTIER
  written (covering 404-event idea, JSONL→SQLite, refactors in lang,
  CSV smarter defaults, done-ness tracking, WIP-by-location,
  WIP→hash on commit, speed benchmarks, Prompt as pinned type,
  search-by-type, daemon viewer, coordinator core, Dark interpreter
  in Dark, HTML view in Dark, darklang.com/gradual placeholder,
  highest-level-fn idea, re-eval-until-feels-good, eval-debuggable,
  HITL broader framing, Tracing surface reduction, pdd-command
  reconsidering, fewer pdd commands).

## B1 — Setup (done)

- [x] Snapshot pdd-thinking/ state (1449 LoC across 9 .md)
- [x] Survey vault paths; list above
- [x] Write this loop.md driver with explicit preamble
- [x] Capture every in-conversation thought as a checkbox above
- [x] Commit

## B2 — CONFLICTS-AND-RESOLUTIONS.md (the headliner)

The deliverable is `pdd-thinking/CONFLICTS-AND-RESOLUTIONS.md`.

Read first (skim 2–3 of these):
- LibMatter `conflicts.md`
- LibMatter `ops.md` + `ops-hierarchy.md`
- `flows/conflict-resolution.md`
- `Ops and Playback/Ops and Conflicts.md`

Doc structure to sketch:

- [x] **What is a conflict** (LibMatter sense + extended runtime sense)
- [x] **Where conflicts arise**: SCM op-vs-op, runtime Pending unresolvable, capability denied, human-input timeout, type-mismatch on materialization, sync disagreement, WIP-would-be-overwritten, resource-exhausted
- [x] **The resolution dispatch**: 4-layer hierarchy — auto rules → policy → park+ask → fail loudly
- [x] **Resolution outcomes**: Substitute / Park / PickSide / RetryWith / AskHuman / FailLoudly
- [x] **LibExecution integration**: Conflict + ConflictDispatch as base primitive, Interpreter emits-then-awaits
- [x] **F# type signature** sketched (Conflict sum, Resolution sum, ConflictDispatch fn)
- [x] **Examples** mapped: FnNotFound, Pending-unresolved, TwoNamesPointedToSameThing, CapabilityDenied, SyncDivergence
- [x] **Forward link to B3**: "Why this is gating for SCM + sync" section ties it to broader project
- [x] Commit

## B3 — SYNC-AND-STABILITY.md

The deliverable is `pdd-thinking/SYNC-AND-STABILITY.md`.

Read first (skim 2–3):
- LibMatter `sync.md` + `sync-model.md` + `db-schema.md`
- `Ops and Playback/Source Control.md` + `more thoughts on Source Control.md`
- `Ops and Playback/dl-scm-distribution.md`
- `Ops and Playback/dl-2025-11-12-ux-thinking.md` (recent)
- `Package Bootstrapping/README.md` + `dl-bootstrapping.md` + `Dogfooding, Bootstrapping.md`
- (Optional) CRDTs blog post

Doc structure:

- [x] **Stability** + **Sharing** defined at top
- [x] **Sync model**: events not entities; three streams (Branch/Patch/Package); idempotent append-only
- [x] **Removing `.dark` files**: content-snapshot bootstrap from `matter.darklang.com/data.db`; LibParser only at edit-time; .dark files gone from repo
- [x] **Open questions on bootstrap**: snapshot contents, upgrade migrations, snapshot-vs-event diff, reproducibility, trust
- [x] **How PDD fits**: Pending→PackageID→Package(hash) maps to WIP→committed-snapshot in SCM; same wire format
- [x] **Conflicts gate sync**: sync routes conflicts through the B2 dispatch
- [x] **WIP sync tension**: hybrid — WIP local by default, explicit "share my WIP" promotes to a named-draft branch
- [x] **What this unlocks**: bullet list at end (`.dark` gone, central canvas, pair programming, daemon-for-many, fast onboarding, branch-as-namespace)
- [x] Commit

## B4 — EVENT-STREAMS-AND-PARKING.md

The deliverable is `pdd-thinking/EVENT-STREAMS-AND-PARKING.md`.

Read first (skim 2–3):
- `Execution/Design - Async Execution.md`
- `Execution/Runtime.md` + `dl-execution.md`
- `parallelism.md`
- (Optional) `dl-runtime-obs.md`

Doc structure:

- [x] **Event streams as a primitive**: typed Stream<T>, multi-subscriber, with map/filter/merge/zip/until/first composers
- [x] **Concrete event kinds**: 10-row table with producers/subscribers per kind
- [x] **Parking semantics**: 8-step walkthrough (emit conflict → dispatch returns Park → scheduler subscribes frame → ... → wake)
- [x] **F# substrate sketch**: Stream<T>, EventSelector, Scheduler types
- [x] **Tie to PDD/conflicts/sync/caps/hot-reload/viewer**: all unified under stream subscription model
- [x] **Async execution model**: vault's "all values are promises" maps to Stream.first; Dark-level `!` operator is the visible park-point
- [x] **Compared to spike's EventSink**: typed/multi-subscriber/compositional/integrated-with-scheduler/error-propagating
- [x] Open questions: backpressure, cancellation, ordering, persistence, GC of parked frames, multi-emit-vs-single-emit, Stdlib shapes
- [x] Commit

## B5 — CAPABILITIES.md

The deliverable is `pdd-thinking/CAPABILITIES.md`.

Read first:
- `~/vaults/2026-04-19_20darklang_20advisor_20call_20notes.md` (one-liner only — most material has to come from synthesis)
- Re-read this repo's `FRONTIER.md` (the "Pre-PDD: capabilities-first" section) for the framing already locked in.

Doc structure:

- [ ] **Why capabilities first** (per feedback): LLM-generated code will try bad things; ungated runtime is a footgun
- [ ] **Capability tags**: `CapPure`, `CapReadFile`, `CapWriteFile`, `CapReadNet`, `CapWriteNet`, `CapReadEnv`, `CapReadTime`, `CapReadRandom`, `CapWriteDB`, `CapExec`, `CapSendSecret`, `CapAny` (forbidden in production)
- [ ] **Where checked**: at the call site in `Apply` for builtin calls
- [ ] **`BuiltInFn` declarations**: each builtin declares its caps; default `{CapPure}`; opt-in the impure
- [ ] **Result type**: `Granted | Denied of reason | DeniedAsk`
- [ ] **Denial → conflict-resolution**: a denial flows into the B2 resolver (substitute default, park-and-ask, or fail)
- [ ] **Grant model**: install-time defaults, per-session granted set, per-invocation `--allow`/`--deny`, interactive `--ask`
- [ ] **LLM-prompt side**: the generate-prompt sees only granted caps; belt-and-suspenders with the runtime gate
- [ ] **Sequencing**: ships before real PDD; PDD layers on top (a materialization needing new caps surfaces a grant request)
- [ ] **Open question**: how do user-defined fns declare effective caps? (Sum of cap-uses in body? Explicit annotation? Inferred?)
- [ ] Commit

## B6 — HOT-RELOAD.md (tight, from first principles)

Deliverable: `pdd-thinking/HOT-RELOAD.md`. Tight — keep it to one
page worth of dense prose. User said "think on this from 'first
principles' towards the end of this, add a .md, tight."

Read first:
- This repo's FRONTIER.md §"Hot reloading — from first principles"
  (the placeholder paragraph)
- B4's EVENT-STREAMS-AND-PARKING.md (just-written; hot-reload is
  effectively a publishing event on the event bus)

Structure:

- [ ] **What hot-reload is for**: in the recursive live-development
  experience, the user is steering a process; the code under their
  cursor changes mid-execution; the runtime should pick up changes
  without restart.
- [ ] **What triggers a reload**: a package item committed (SCM
  op), a WIP edit, a PDD materialization, a remote sync. All
  reduce to "this hash/name now refers to a different body."
- [ ] **The granularity question**: per-fn? Per-module? Per-DB?
  Per-trace-replay-boundary?
- [ ] **The interaction with parked frames**: a frame parked on a
  Pending wakes when materialized. What about a frame *already in
  the middle of executing* an old body when a new body is
  published? Continue with old body until current frame exits, or
  preempt? Need to decide.
- [ ] **The contract**: are bodies updated atomically? Are tests
  re-run? Are dependents invalidated?
- [ ] **Connection to SCM branch ops**: switching branches is a
  bulk hot-reload — same machinery.
- [ ] **Connection to the viewer**: the viewer should hot-reload
  too — fn states update live as materializations complete.
- [ ] **Open question**: how does this play with the conflicts +
  resolutions system (B2)? A reload that would break callers
  should trigger conflict-resolution, not silently break them.
- [ ] Commit

## B7 — COMPOSABLE-MVU.md (apps infra sketch)

Deliverable: `pdd-thinking/COMPOSABLE-MVU.md`. Tight — the
viewer + traces + apps experience all sit on top of this; the
sketch is "what's the right substrate?" not "here's an
implementation."

Read first (treat critically — user warned these are old):
- `~/vaults/Darklang Dev/05.Implementation/CLI/Apps/README.md`
- `~/vaults/Darklang Dev/05.Implementation/CLI/Apps/dl-frp-mvu.md`
- `~/vaults/Darklang Dev/05.Implementation/CLI/Apps/dl-mvu-frp-impl.md`
- `~/vaults/Darklang Dev/05.Implementation/Future Environments/Canvas UIs/dl-composable-ui.md`
- `~/vaults/Darklang Dev/05.Implementation/Future Environments/Canvas UIs/UIs.md`

Structure:

- [ ] **Why MVU as the substrate**: deterministic, replayable
  state evolution (Model + update fn + view fn). Pairs naturally
  with traces — every Msg → Model transition is recorded; replay
  is free. Pairs naturally with hot-reload — swap the update or
  view fn, keep the Model.
- [ ] **Composable**: apps nest. A bigger app's Model contains
  sub-app Models; bigger update routes Msgs to sub-update fns;
  bigger view composes sub-views. Standard Elm/Bonsai-shape stuff.
- [ ] **What composes**: Models compose by product (record/struct).
  Msgs compose by sum (variant). Views compose by parent-passes-
  child-view. Effects/commands compose by interleaving.
- [ ] **Distinguish from React-style**: explicit Msg → update.
  No hidden state. Time-travel debugging falls out.
- [ ] **What old vault thoughts say** — summary of the
  `dl-frp-mvu.md` and `dl-mvu-frp-impl.md` framings, what feels
  still-right, what feels dated.
- [ ] **Connection to PDD viewer**: the in-focus-fn view
  (sketched in B8) is an MVU app. PDD events arrive as Msgs.
  Materializations update the Model. Hot-reload swaps the body
  of a refined fn but keeps user navigation state.
- [ ] **Connection to traces**: a Msg log is a trace. Replaying a
  trace = re-applying Msgs to the initial Model. Diffing two
  traces = aligning their Msg sequences.
- [ ] **Connection to events (B4)**: MVU Msgs can come from
  event-stream subscriptions. The event bus feeds the update
  loop.
- [ ] **F#-side primitives** vs **Dark-side composition**: F#
  provides the runtime (Model storage, update dispatch, view
  rendering) — small surface. Dark provides the apps. Sketch the
  thin F# substrate.
- [ ] **Open questions**: how does this play with multiple
  concurrent apps (e.g., the PDD viewer + a SCM view + the user's
  own app)? Are they truly separate Models, or one big composed
  Model?
- [ ] Commit

## B8 — VIEW-SKETCHES.md (high-level pretty sketches)

Deliverable: `pdd-thinking/VIEW-SKETCHES.md`. User asked for
"various versions of [the in-progress fn view/experience] at
various points in time. High-level pretty sketches. Don't need
anything real yet."

Read first:
- FRONTIER.md §"Recursive live-development experience" — the
  vibe is established there; this doc draws the actual views

Sketch with ASCII (or descriptive prose if ASCII gets unwieldy)
the in-focus-fn view at multiple moments:

- [ ] **t=0 — just after the user typed a prompt.** What does the
  viewer show? Maybe just: prompt text, the top-level fn skeleton
  (name + sig, no body yet), a "decomposing..." indicator.
- [ ] **t=1 — decompose has produced a Dark expression.** Shows
  the expression. Sub-fns marked Pending. Visible signatures.
- [ ] **t=2 — materializations in flight.** Each Pending sub-fn
  has a status badge: ⋯ in-progress, ✓ real, ▼ fake, ↻ cached, ✗
  failed. Maybe live LLM-call timings.
- [ ] **t=3 — first eval running.** Top-level fn executes; values
  start propagating. Trace events stream in a side panel.
- [ ] **t=4 — refining.** User saw a body and wants it better.
  Triggers a refine; viewer shows the old body, the new body, and
  the score-delta.
- [ ] **t=5 — committed.** Some fns promoted to hashes. Other
  callers' refs updated. Viewer reflects this.
- [ ] **The dive-in mechanic**: clicking a fn shows its body, its
  trace, tests, dependents. Clicking a trace event jumps to that
  step. Clicking a Pending shows the materialization attempts.
- [ ] **Multiple zoom levels** (whole-program → fn → expression →
  value) — sketch what the navigation feels like.
- [ ] **Note**: this doc is intentionally vague on tech. It's a
  *visual brief* for whoever builds the viewer eventually.
- [ ] Commit

## B9 — Decision: build-serve-expr.py

Tiny bucket — answer the deferred question and act.

- [ ] Re-check: is `build-serve-expr.py` still referenced? (Only by README's darklang.com block, which documents a workflow using sunset `dark pdd run`.)
- [ ] **Decide**: delete the script + the README darklang.com block (option b — both are dead workflow), OR keep both as historical evidence (option a).
- [ ] Default unless told otherwise: **delete both**. The spike's darklang.com demo was a moment-in-time artifact; FRONTIER captures the "live HTML view in Dark" target.
- [ ] Commit

## B10 — Cross-reference + tidy

- [ ] Update FRONTIER.md to point at the new sketches (CONFLICTS-AND-RESOLUTIONS, SYNC-AND-STABILITY, EVENT-STREAMS-AND-PARKING, CAPABILITIES, HOT-RELOAD, COMPOSABLE-MVU, VIEW-SKETCHES) and prune duplicate content
- [ ] Update README.md "How to enter" pointer list to include the new sketches
- [ ] Verify no dangling cross-references (grep for filenames)
- [ ] Re-run snapshot; record final file count + LOC
- [ ] Append "After" section below with numbers
- [ ] Commit

## B11 — Propose pushing the branch (end-goal)

User's stated end goal: push the branch with notes so they can
pick the topic back up later. Memory says never push pdd — so
this is a propose-to-user step, not a unilateral push.

- [ ] Set this file's `NEXT:` to `DONE`
- [ ] Surface the proposal to the user with a tight summary:
  - total LoC + file count post-sketch-loop
  - the new docs added (CONFLICTS-AND-RESOLUTIONS, SYNC-AND-
    STABILITY, EVENT-STREAMS-AND-PARKING, CAPABILITIES,
    HOT-RELOAD, VIEW-SKETCHES)
  - the build-serve-expr.py decision outcome
  - ask: ready to push the pdd branch? `git push -u origin pdd`?
- [ ] Wait for user confirmation before any push.
- [ ] Final commit (just this loop.md update if nothing else changed).

---

## After

*Filled in by B10 + B11.*
