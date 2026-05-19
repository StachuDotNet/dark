# Substrate Sketches — Loop Driver

Following the consolidation, sketch the three substrate concepts
PDD really needs: **conflicts+resolutions** (with strong emphasis
on sync, "stability and sharing," and bootstrapping without `.dark`
files), event streams + parking, and capabilities.

Each bucket is one iter (~10 min). The loop self-drives — reads
the `NEXT:` line, executes that bucket, ticks it off, advances.

## Status

**NEXT:** `B2` — Sketch CONFLICTS-AND-RESOLUTIONS.md (the core model)

## Vault notes worth reading

The user warned: some are old/dumb. Rely on more recent thoughts.
`-old` suffixes and undated docs are suspect.

**Conflicts / Ops / Sync / Bootstrap:**
- `~/vaults/Darklang Dev/05.Implementation/Constraints/dl-constraints-conflicts-old.md` (likely stale)
- `~/vaults/Darklang Dev/05.Implementation/Package Bootstrapping/README.md`
- `~/vaults/Darklang Dev/05.Implementation/Package Bootstrapping/dl-bootstrapping.md`
- `~/vaults/Darklang Dev/05.Implementation/Package Bootstrapping/Dogfooding, Bootstrapping.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/README.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/Ops and Conflicts.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/Source Control.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/more thoughts on Source Control.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/dl-scm-distribution.md`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/dl-2025-11-12-ux-thinking.md` (recent — trust more)
- `~/vaults/Darklang Dev/05.Implementation/WIP and Unorganized/specs/LibMatter/` (full set: README, ops.md, ops-hierarchy.md, conflicts.md, sync.md, sync-model.md, db-schema.md, garbage-collection.md)
- `~/vaults/Darklang Dev/05.Implementation/WIP and Unorganized/specs/flows/conflict-resolution.md`
- `~/vaults/Darklang Dev/91.Ocean/Research/Blog posts/Conflict-free Replicated Data Types (CRDTs).md`

**Events / Parking / Async / Runtime:**
- `~/vaults/Darklang Dev/05.Implementation/Execution/Design - Async Execution.md`
- `~/vaults/Darklang Dev/05.Implementation/Execution/Runtime.md`
- `~/vaults/Darklang Dev/05.Implementation/Execution/dl-execution.md`
- `~/vaults/Darklang Dev/05.Implementation/Execution/dl-runtime-obs.md`
- `~/vaults/Darklang Dev/05.Implementation/Execution/Interpreter/interpreter rewrite/bonus, follow-ups/parallelism.md`

**Capabilities:**
- `~/vaults/2026-04-19_20darklang_20advisor_20call_20notes.md` (brief mention)
- (No explicit capability spec in vaults — synthesize from first principles + draw from feedback)

## Loop protocol

1. Read this file. Find `NEXT:` line.
2. Execute that bucket's unchecked items. For sketch buckets, prefer
   reading 2-3 of the most-relevant vault notes first (skim, not
   exhaustive), then write.
3. Tick items as done.
4. Update `NEXT:` line. Set to `DONE` when all buckets done.
5. Commit. Bucket-named message.
6. Schedule next wake (10 min). Or stop if `DONE`.

---

## B1 — Setup (this iter)

- [x] Snapshot current state (1449 LoC across 9 .md files incl feedback/archive)
- [x] Survey vault paths; list above
- [x] Write this loop.md driver
- [x] Commit

## B2 — CONFLICTS-AND-RESOLUTIONS.md (core model)

Read first:
- LibMatter `conflicts.md` + `ops.md` + `ops-hierarchy.md`
- `Ops and Playback/Ops and Conflicts.md`
- `flows/conflict-resolution.md`

Sketch the doc. Structure:

- What is a conflict (in LibMatter sense + extended to runtime sense)
- Where conflicts arise: SCM (op vs op), runtime (Pending unresolvable), capability (denied), human-input (timeout), type-mismatch on materialization, etc.
- The resolution policy: who decides, in what order. Hierarchy of resolvers (auto rules → policy → human → fail).
- Resolution outcomes: substitute default, park, ask human, retry, fail loudly.
- Integration with LibExecution: conflicts as a low-level primitive used by Interpreter + LibMatter + future-PDD.
- Sketch a type signature for the conflict-resolution dispatch.
- Examples: how does today's `FnNotFound` map to a conflict? How does Pending unresolved map? How does op-vs-op conflict map?
- Lead into B3 (sync) by ending on: "this primitive is what makes sync feasible."

- [ ] Read vault notes
- [ ] Write CONFLICTS-AND-RESOLUTIONS.md
- [ ] Commit

## B3 — SYNC-AND-STABILITY.md (sync, sharing, bootstrap-without-.dark)

Read first:
- LibMatter `sync.md` + `sync-model.md` + `db-schema.md`
- `Ops and Playback/Source Control.md` + `more thoughts on Source Control.md`
- `Ops and Playback/dl-scm-distribution.md`
- `Package Bootstrapping/README.md` + `dl-bootstrapping.md` + `Dogfooding, Bootstrapping.md`
- `dl-2025-11-12-ux-thinking.md` (recent)
- (skim) CRDTs blog post if relevant

Sketch:

- "Stability" = a thing is named, hashed, validated, and persistent
  enough that other consumers can rely on it.
- "Sharing" = handing a stable thing to another instance / human /
  agent, with the conflict-resolution machinery handling
  disagreements.
- Sync model: content-addressable package items + locations table
  with branch overlays. Sketch what crosses the wire (ops or
  content?). Connect to LibMatter ops.
- **Removing `.dark` files: package bootstrapping.** Today, a new
  Darklang instance bootstraps from `.dark` source files that get
  replayed/parsed/installed. The target: bootstrap from a content
  snapshot (a SQLite blob or equivalent) — no source files to
  parse on first run. Same content-addressable model that powers
  sync also powers bootstrap. Note any open questions about: what
  ships in the snapshot, how upgrades work, how local edits diff
  against the snapshot.
- How PDD fits: WIP refers by location; on commit, by hash.
  Synced PDD work flows over the same channel as hand-authored
  package items.
- How conflicts (B2) gate sync: a sync that would introduce a
  conflict triggers the resolution policy rather than just
  failing.
- WIP sync tension (carried from feedback): does WIP sync, or stay
  local? Likely: WIP stays local by default, but can be
  promoted-to-shared on demand.

- [ ] Read vault notes
- [ ] Write SYNC-AND-STABILITY.md
- [ ] Commit

## B4 — EVENT-STREAMS-AND-PARKING.md

Read first:
- Execution/`Design - Async Execution.md`
- Execution/`Runtime.md` + `dl-execution.md`
- `parallelism.md`
- Maybe: `dl-runtime-obs.md` (for observability)

Sketch:

- Event streams as a low-level primitive in LibExecution. Streams
  are typed; subscribers register interest. Streams compose into
  graphs (waiters, joins, fan-out).
- Concrete event kinds: materialization-done, capability-denied,
  fn-not-found-but-still-resolvable, sync-changed-this-fn,
  human-asked-question, etc.
- Parking: a frame parks on an event-stream subscription. The
  scheduler runs other ready frames. When a relevant event fires,
  parked frames wake.
- Connect to async execution model: parking is the F#-level
  primitive; Dark-level code uses higher-level abstractions
  (futures? channels? something Dark-native).
- Tie back to PDD: a Pending materialization parks on a "this name
  was resolved" event. A capability request parks on "approved"
  or "denied." A SCM sync parks on "remote agrees."
- Tie back to conflicts: conflicts can emit events (e.g.
  "conflict needs human"), which other parts of the system
  subscribe to.
- F# substrate sketch: what does this look like in F# code? Tight
  signature for the event-stream primitive.

- [ ] Read vault notes
- [ ] Write EVENT-STREAMS-AND-PARKING.md
- [ ] Commit

## B5 — CAPABILITIES.md

Read first:
- `2026-04-19_20darklang_20advisor_20call_20notes.md` (brief)
- (Capability vault material is thin. Synthesize from CLAIMS,
  FRONTIER, the feedback, and security-first first principles.)

Sketch:

- Why capabilities first (per feedback): LLM-generated code will
  try to do bad things; ungated runtime is a footgun. PDD layers
  on top of caps, not before them.
- Capability model: a `Capability` is a tag (`CapPure`,
  `CapReadFile`, `CapWriteFile`, `CapReadNet`, `CapWriteNet`,
  `CapReadEnv`, `CapReadTime`, `CapReadRandom`, `CapWriteDB`,
  `CapExec`, `CapSendSecret`, `CapAny` (forbidden in production)).
- Each `BuiltInFn` declares its caps. Default everything to
  `{CapPure}` then opt-in the impure ones.
- Cap-check happens at the call site in `Apply` for builtin calls.
- Result of cap-check: `Granted | Denied of reason | DeniedAsk`.
- Denial flows into the conflict-resolution system (B2): the
  resolution may substitute a default, park-and-ask-human, or
  fail.
- Grant model: install-time defaults (cap profile per install),
  per-session granted set, per-invocation `--allow`/`--deny`, and
  interactive `--ask` mode.
- LLM-side: the generate-prompt sees only granted caps. Belt-and-
  suspenders with the runtime gate.
- "Capabilities first" sequencing: this lands before PDD. PDD
  hooks into it (a materialization that needs new caps surfaces a
  grant request via the same flow).
- Open questions: how do user-defined fns declare effective caps?
  (Sum of cap-uses in body? Explicit annotation? Inferred?)

- [ ] Read vault notes
- [ ] Write CAPABILITIES.md
- [ ] Commit

## B6 — Cross-reference + tidy

- [ ] Update FRONTIER.md to point at the new sketches and remove
  duplicated content (its Pre-PDD section now points at
  CAPABILITIES; conflicts+resolutions section points at the new
  sketch; event-streams section points at EVENT-STREAMS-AND-PARKING)
- [ ] Update README's "How to enter" pointer list
- [ ] Verify cross-references work; no dangling links
- [ ] Final LOC + file count
- [ ] Set status to `DONE`
- [ ] Commit

---

## After

*Filled in by B6.*
