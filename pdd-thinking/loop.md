# Substrate Sketches — Loop Plan

> **PREAMBLE FOR THE LOOPER (read this first, every iter):**
>
> You are an iter of a 10-minute self-paced loop. Your job is to
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
> 8. Schedule the next wake (10 min). If `NEXT: DONE`, run B7
>    verify and **stop scheduling**.
>
> **The headline:** conflicts+resolutions (B2) is the most important
> sketch — **it's the gating piece for the SCM + sync work the user
> needs to take care of in the broader project**, not just a PDD
> concern. B3 (sync + stability + bootstrap-without-.dark-files)
> rides directly on top of it. Lean weight there; if the iter runs
> short, prioritize B2 depth over B4/B5.

## Status

**NEXT:** `B2` — Sketch CONFLICTS-AND-RESOLUTIONS.md

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

**Capabilities:**
- `~/vaults/2026-04-19_20darklang_20advisor_20call_20notes.md` (brief — has the "no safety, capabilities model, builtins are the only impure boundary" line)
- No explicit capability spec found in vaults — synthesize from FRONTIER + feedback + first principles

## In-conversation source thoughts to address

These are the user's thoughts from this session, copied here so the
looper can verify nothing is dropped. **Every item below must be
addressed by some bucket.**

- [ ] **Sketch capabilities** — covered by B5 (CAPABILITIES.md)
- [ ] **Sketch conflicts+resolutions system** — covered by B2 (CONFLICTS-AND-RESOLUTIONS.md). User said *especially* focus here.
- [ ] **Sketch event/parking substrate** — covered by B4 (EVENT-STREAMS-AND-PARKING.md)
- [ ] **How conflicts+resolutions leads into syncing** — covered by B3 (SYNC-AND-STABILITY.md)
- [ ] **"Stability and sharing"** as the right framing — covered by B3
- [ ] **Removing .dark files from the codebase / package bootstrapping** — covered by B3
- [ ] **Rely on more recent vault thoughts; some notes are old/dumb** — protocol applied in every read-vault step
- [ ] **Is `build-serve-expr.py` still referenced/needed?** — covered by B6 (decide + act)
- [ ] **Sketches go in `.md` files** — every B2-B5 produces an `.md`
- [ ] **The system runs as a loop, 10 min cadence, for ~an hour** — set up B1; will end naturally at B7

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

- [ ] **What is a conflict** (LibMatter sense + extended runtime sense)
- [ ] **Where conflicts arise**: SCM op-vs-op, runtime Pending unresolvable, capability denied, human-input timeout, type-mismatch on materialization, sync disagreement
- [ ] **The resolution dispatch**: hierarchy — auto rules → policy → human → fail loudly. Who decides, in what order.
- [ ] **Resolution outcomes**: substitute default / park-and-wait / ask human / retry with different strategy / fail loudly / pick-a-side
- [ ] **LibExecution integration**: conflicts as a low-level primitive used by Interpreter + LibMatter + PDD + sync
- [ ] **F#-shaped type signature** for the resolver dispatch (kept high-level, not committal)
- [ ] **Examples** mapped to today's behavior: how `FnNotFound` becomes a conflict, how Pending unresolved is one, how op-vs-op is one
- [ ] **Forward link to B3**: "this primitive is what makes sync feasible"
- [ ] Commit

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

- [ ] **Stability** defined: a thing is named, hashed, validated, and persistent enough for other consumers to rely on
- [ ] **Sharing** defined: handing a stable thing to another instance / human / agent, conflicts-machinery handling disagreement
- [ ] **Sync model**: content-addressable package items + locations-table-with-branch-overlay. What crosses the wire — ops or content?
- [ ] **Removing `.dark` files (package bootstrapping)**: today's bootstrap replays `.dark` source files; the target is to bootstrap from a content snapshot (SQLite blob or equivalent) — no source-file parsing on first run. Same content-addressable model that powers sync powers bootstrap.
- [ ] **Open questions on bootstrap**: what ships in the snapshot, how upgrades layer on, how local edits diff against the baseline
- [ ] **How PDD fits**: WIP refs by location; on commit, refs become by hash; synced PDD work flows over the same channel as hand-authored items
- [ ] **Conflicts gate sync**: a sync that would introduce a conflict triggers the resolution policy (B2) instead of failing
- [ ] **WIP sync tension** (carried from feedback): does WIP sync, or stay local? Likely WIP stays local by default, with explicit promote-to-shared on demand
- [ ] **What this unlocks**: removing .dark files entirely; cross-instance share; pair-programming on the same package store; PDD-on-a-server materializing for many clients
- [ ] Commit

## B4 — EVENT-STREAMS-AND-PARKING.md

The deliverable is `pdd-thinking/EVENT-STREAMS-AND-PARKING.md`.

Read first (skim 2–3):
- `Execution/Design - Async Execution.md`
- `Execution/Runtime.md` + `dl-execution.md`
- `parallelism.md`
- (Optional) `dl-runtime-obs.md`

Doc structure:

- [ ] **Event streams as a primitive**: typed streams, subscribers register interest, streams compose into graphs (waiters, joins, fan-out)
- [ ] **Concrete event kinds**: materialize-done, capability-denied, fn-not-found-but-still-resolvable, sync-changed-this-fn, human-asked-question
- [ ] **Parking**: a frame parks on an event-stream subscription; scheduler runs other ready frames; relevant event wakes parked frames
- [ ] **F# substrate sketch**: tight signature for the event-stream primitive
- [ ] **Tie to PDD**: Pending materialization parks on "name was resolved"; cap request parks on "approved/denied"; SCM sync parks on "remote agrees"
- [ ] **Tie to conflicts**: conflicts emit events; subscribers (humans, agents, retry-engines) react
- [ ] **Async execution model**: parking is the F#-level primitive; Dark-level code uses higher-level futures/channels (sketch shape, don't commit)
- [ ] Commit

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

## B6 — Decision: build-serve-expr.py

Tiny bucket — answer the deferred question and act.

- [ ] Re-check: is `build-serve-expr.py` still referenced? (Only by README's darklang.com block, which documents a workflow using sunset `dark pdd run`.)
- [ ] **Decide**: delete the script + the README darklang.com block (option b — both are dead workflow), OR keep both as historical evidence (option a).
- [ ] Default unless told otherwise: **delete both**. The spike's darklang.com demo was a moment-in-time artifact; FRONTIER captures the "live HTML view in Dark" target.
- [ ] Commit

## B7 — Cross-reference + tidy

- [ ] Update FRONTIER.md to point at the new sketches (CAPABILITIES, CONFLICTS-AND-RESOLUTIONS, EVENT-STREAMS-AND-PARKING, SYNC-AND-STABILITY) and prune duplicate content
- [ ] Update README.md "How to enter" pointer list to include the 4 new sketches
- [ ] Verify no dangling cross-references (grep for filenames)
- [ ] Re-run snapshot; record final file count + LOC
- [ ] Set status to `DONE`
- [ ] Append "After" section below with numbers
- [ ] Final commit

---

## After

*Filled in by B7.*
