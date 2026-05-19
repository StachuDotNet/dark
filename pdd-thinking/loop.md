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

**NEXT:** `B9` — Decision: build-serve-expr.py

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

- [x] **Why capabilities first** + framing (LLM footgun)
- [x] **Capability tags** sketched as a sum (incl. CapSendSecret + CapReadDB/CapWriteDB split)
- [x] **Where checked**: at Apply for builtin calls; pseudocode shown
- [x] **`BuiltInFn` declarations**: default `{CapPure}`, opt-in the impure
- [x] **Result type**: Granted | Denied | DeniedAsk
- [x] **Denial → conflict-resolution**: routes through B2 dispatch
- [x] **Grant model**: 5-layer (per-invocation / interactive / session / install / system floor)
- [x] **Interactive grants over the event bus**: 7-step flow connecting B2 + B4
- [x] **LLM-prompt side**: generate-prompt filtered by granted caps; belt-and-suspenders
- [x] **User-defined fn effective caps**: walkExprs computation; 3 sub-open-questions
- [x] **Sequencing**: 10-step integration order (caps land before PDD wiring)
- [x] **What this unlocks**: safe LLM-code exec, auditable trust, composable trust, sandboxing-built-in, cap-aware refactoring
- [x] **Open questions beyond effective-caps**: per-user-vs-session, cap-state sync, mid-session revocation, granularity, network targets, package-store caps, test mode
- [x] **Compared to existing systems** (Joe-E, Pony, WASM Component Model — we're cap-tags-on-builtins, not value-passing)
- [x] Commit

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

- [x] **What hot-reload is for**: recursive live-dev; runtime picks up changes without restart. Framed as a consumer of `BodyChanged hash` (no separate subsystem).
- [x] **What triggers a reload**: local edit, refine, materialization, SCM commit, remote sync, branch switch, bootstrap (all reduce to one event kind)
- [x] **Granularity**: per-fn-by-location for user code; per-hash for committed; falls out for free from PackageID/Package(hash) model
- [x] **Parked-frames interaction**: trivial wake-on-event case + the harder mid-execution case (finish-then-update default, preempt option, both-race niche)
- [x] **The contract**: atomic update, causal ordering, idempotence, trace recording. NOT auto-rerun-tests, NOT auto-flush-caches.
- [x] **SCM branch ops**: bulk reload via batched BodyChanged with transaction-end markers
- [x] **Viewer**: heavy subscriber to BodyChanged + FrameParked/Woken + ConflictResolved
- [x] **Capabilities interaction**: cap surface might change; flow into B5 dispatch (AskHuman if caps grew)
- [x] **Conflicts interaction**: type/sig changes emit Conflict.TypeMismatch via B2; dispatched (substitute/park/ask/fail)
- [x] **Open questions**: cross-instance latency, preempt knob, GC of old bodies, hot-reload of runtime itself, type-change migration as separate flow
- [x] Commit

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

- [x] **Why MVU as the substrate**: 3 properties — pairs with traces, pairs with hot-reload, pairs with event streams
- [x] **The shape**: `App<Model, Msg>` record (init/update/view/effects)
- [x] **Composable**: Models by product, Msgs by variant, view assembles, effects interleave
- [x] **"Default view per thing"**: polymorphic `Stdlib.UI.view` per type; package-store-overridable
- [x] **Mapping spike's PDD viewer**: hand-written F# → Dark MVU app with subscribe-via-effects
- [x] **F# substrate vs Dark composition**: ~500 LoC F# runtime (Elmish loop + effects executor + view adapter + Model persistence); rest is Dark
- [x] **Msg log = trace**: same artifact, two consumption patterns; replay scrubber as slider over Msg log
- [x] **Events (B4) bridge**: subscriptions are Effects; emits become Msgs
- [x] **Multiple concurrent apps**: hybrid root with `List<RunningApp>` + typed sub-Models
- [x] **What this unlocks**: PDD viewer + trace inspector + SCM UI + user apps + free hot-reload + polymorphic views
- [x] **Old vault thoughts**: synthesized into "default view per thing" framing; bookmarks captured
- [x] **Open questions**: render target, effects discipline, trace replay with different update fn, real-time collab, state persistence, Stdlib.UI content
- [x] **Compared to Elm / Bonsai / React / SwiftUI**: closest to Bonsai (composable as values); differs from React (no implicit state)
- [x] Commit

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

- [x] **Layout principle** at top: 3-region (top strip / focus + dive-in / event timeline)
- [x] **t=0** prompt-received sketch
- [x] **t=1** decompose-produced sketch (5 fn refs annotated with status)
- [x] **t=2** materializations-in-flight (with dive-in showing parseRows detail + Refine/Pin actions)
- [x] **t=3** first-eval-running (inline values propagating; pause/skip controls)
- [x] **t=4** refining (diff view + score delta + Accept/Reject)
- [x] **t=5** committed (hash-stamped; commit-preview in dive-in; ready-to-send)
- [x] **Dive-in mechanic** described (8 click-targets: fn / event / value / hash / Pending / cap / conflict)
- [x] **Multiple zoom levels** (whole-program ↕ module ↕ fn ↕ expression ↕ value) with status badges propagating up
- [x] **Concurrent threads** sketch (sessions strip)
- [x] **What the viewer is NOT** (not IDE, test runner, SCM tool, deploy surface)
- [x] **Implementation vagueness** preserved as intended (note at end)
- [x] **Aspirational closing**: viewer as cockpit; system produces decisions, not source files
- [x] Commit

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
