# Substrate-Roadmap Design Loop

> **PREAMBLE FOR THE LOOPER (read this first, every iter):**
>
> You are an iter of a 10-minute self-paced loop. Your job is to
> design the roadmap to make Darklang **the distributed software
> substrate** described in `COHABITATION.md` — built on the 7-pillar
> ethos (Local-First / Accessible / Open / Immediate / Malleable /
> Composable / Simple).
>
> The user is *itching* for two specific milestones:
>
> 1. **Package bootstrapping** — removing `.dark` files from the repo
>    (see `SYNC-AND-STABILITY.md` and vault `Package Bootstrapping/`)
> 2. **Stability and sharing** — cross-instance/coworker sync working
>    end-to-end (see `SYNC-AND-STABILITY.md` and vault `Sync and Distribution/`)
>
> These should be **landed milestones**, not eternal targets. The
> roadmap should answer **when** each gets done and **what blocks
> what** to get there.
>
> **Each iter:**
>
> 1. Find the next-unchecked TODO (or batch of related TODOs) below.
> 2. **Ground every batch in `main`'s reality, not in what the
>    sketches imply.** This branch (`pdd`) carries spike code the
>    user has disowned as wrong-direction; the design docs live
>    here, but the **authoritative current code state is `main`**.
>    Plans must reflect what *actually* exists, not what's
>    documented.
>
>    **At the start of every batch**, spend ~30 seconds checking
>    main's relevant code. Don't skip this — sketches can be
>    months out of sync; plans built on stale assumptions will
>    misroute the roadmap.
>
>    Useful one-liners:
>      - `git ls-tree -r main --name-only | grep -E '<pattern>'`
>        — find files matching a pattern in main
>      - `git show main:path/to/file.fs | head -80`
>        — read a specific file as-of main
>      - `git show main:path/to/file.fs | grep -nE '<symbol>'`
>        — find a symbol in main
>      - `git log main --oneline -- path/to/dir | head -10`
>        — recent changes in main to a dir
>      - `git diff main..HEAD -- backend/ | wc -l`
>        — total LoC delta vs main (mostly for awareness)
>
>    When in doubt about *anything* the codebase claims to do:
>    confirm against main. Don't assume.
> 3. Read the 1-3 most-relevant vault notes for that TODO (do NOT
>    deep-dive endlessly — skim the load-bearing claims, then think).
> 4. Do the thinking. Produce concrete output:
>    - Most TODOs append a section to **`ROADMAP.md`** (the growing
>      deliverable; create it the first iter).
>    - Some TODOs produce their own new doc (e.g. `IDENTITY.md`,
>      `MIGRATION.md`). The TODO says which.
>    - Some TODOs update an existing substrate sketch.
> 5. Tick the TODO(s) off.
> 6. Commit with a short message naming the TODO(s) processed.
> 7. Schedule the next wake at 10 min (or stop if all TODOs done +
>    the validation pass at the end is also done).
>
> **Batch sizing:** prefer batching 2-4 related TODOs into one iter
> when they share context (e.g. "design the identity model" + "decide
> agent attribution" — same vault notes, same design space). Don't
> batch unrelated items.
>
> **The headline ordering goal:** by the time the loop is done,
> `ROADMAP.md` should clearly answer:
>
> - What chunks are needed?
> - What order can they be done in (with explicit blockers/parallel
>   tracks)?
> - When does **package bootstrapping** ship? (target: a concrete
>   phase number in the roadmap)
> - When does **stability and sharing** ship? (same)
> - What's the smallest end-to-end demo that proves cohabitation
>   works? (i.e., what's "MVP-cohabitation"?)
>
> **HARD CONSTRAINTS** (do not propose designs that violate these):
>
> - **AI is wholly opt-in.** Every AI-flavored feature requires
>   an explicit user grant. Dark must work fully without any AI —
>   no LLM call by default, no degraded experience for refusing.
>   PDD is one opt-in feature; the substrate isn't built around
>   it. Every roadmap chunk needs a non-AI path; flag if any
>   chunk implicitly assumes AI.
> - **Local-first.** No cloud-mandatory features.
> - **Open + transparent.** Default to inspectable.
>
> See ROADMAP §"Foundational constraints" for the full list.

## Status

**NEXT:** `T3 + T4 + T5 + T6` (bootstrap focus batch)

## Reference docs in this directory

You'll consult these heavily. Treat as inputs, not to be rewritten
unless a TODO says so.

- `README.md` — what's live in code
- `CLAIMS.md` — the 5 claims, reframed
- `ALGORITHM.md` — high-level recursive-coding-agent sketch
- `WRAP-UP.md` — spike retro + integration plan
- `FRONTIER.md` — speculative + source-code thoughts
- `CONFLICTS-AND-RESOLUTIONS.md` — substrate sketch (headline)
- `SYNC-AND-STABILITY.md` — substrate sketch
- `EVENT-STREAMS-AND-PARKING.md` — substrate sketch
- `CAPABILITIES.md` — substrate sketch
- `HOT-RELOAD.md` — substrate sketch
- `COMPOSABLE-MVU.md` — substrate sketch
- `VIEW-SKETCHES.md` — visual brief for the in-progress fn viewer
- `GRAPH-PROJECTION.md` — graph framing for the data layer
- `COHABITATION.md` — the unifying vision + ethos alignment

## Vault paths worth dipping into

(Use `find ~/vaults/Darklang\ Dev -type d` if you need to navigate.
Prefer dated/recent material; the LibMatter specs and the
2025-11-12 ux-thinking doc are most-current; `-old.md` suffixes
are suspect.)

- `~/vaults/Darklang Dev/04.Ethos/` — the 7 pillars + meta
- `~/vaults/Darklang Dev/05.Implementation/Sync and Distribution/`
- `~/vaults/Darklang Dev/05.Implementation/CRDTs/`
- `~/vaults/Darklang Dev/05.Implementation/Package Bootstrapping/`
- `~/vaults/Darklang Dev/05.Implementation/Purity, Effects, and Sandboxing/`
- `~/vaults/Darklang Dev/05.Implementation/Accounts and Auth/`
- `~/vaults/Darklang Dev/05.Implementation/Ops and Playback/`
- `~/vaults/Darklang Dev/05.Implementation/WIP and Unorganized/specs/LibMatter/`
- `~/vaults/Darklang Dev/05.Implementation/AI/`
- `~/vaults/Darklang Dev/05.Implementation/Execution/`
- `~/vaults/Darklang Dev/05.Implementation/Queues, Workers, Feeds/`
- `~/vaults/Darklang Dev/05.Implementation/Remote Access and Control/`
- `~/vaults/Darklang Dev/05.Implementation/Networking and Internet/`
  - **`Tailscale.md` is highly relevant + recent.** Vault stance: lean on Tailscale for peer addressing + identity + TLS + auth headers; don't build a networking stack. Read for T22b.
- `~/vaults/Darklang Dev/05.Implementation/Future Environments/`

---

# TODO list

## Phase A — Roadmap skeleton (do first)

These bootstrap the deliverable. Don't skip; everything else
appends to or references the structure laid down here.

- [x] **T1: Create ROADMAP.md.** Done. Includes critical
  "Reality check: what main actually has today" section
  documenting the schema (18 tables, kill-and-fill, accounts_v0
  seeded, branches/commits/package_ops/branch_ops all exist),
  the F# substrate (PackageManager.fs + Op*Playback.fs), and the
  Dark-side code (cli/scm/* all in Dark, llm/agent.dark, etc.).
- [x] **T2: Enumerate chunks.** 15-row table (C1-C15) with status
  per chunk: 4 EXISTS, 6 PARTIAL, 4 NEEDS-DESIGN/BUILD, 1
  DEFERRED. Key finding: the substrate is **more than half there**.
  Roadmap focuses on the partials + needs-build, in dep order.

## Phase B — Bootstrapping focus (the user is itching)

- [ ] **T3: Read vault `Package Bootstrapping/` and `Sync and
  Distribution/` thoroughly.** Capture in `BOOTSTRAP.md` (new):
  current state ("how does an install work today, exactly"),
  target state (snapshot-based, no `.dark` parse on first run),
  delta between them. Include open questions on snapshot
  granularity, signing/trust, upgrade migrations.

- [ ] **T4: Design the snapshot format.** What goes in the
  content snapshot (SQLite blob or other)? Include: package_fns
  table + locations table + dependencies. Exclude: WIP, traces,
  per-user state. Specify the bootstrap protocol (GET
  /data.db + GET /events?since=ts). Append to BOOTSTRAP.md.

- [ ] **T5: Sequence the bootstrap work.** What concrete commits
  in what order get `.dark` files out of the repo? Aim for
  ~6-10 named PRs / branches with explicit dependencies. Include
  the LibParser-only-at-edit-time decoupling. Append to
  BOOTSTRAP.md.

- [ ] **T6: Decide which phase bootstrapping ships in.** Look at
  the chunk-order from T2 — does bootstrap depend on conflicts +
  sync + ops being solid first? Or can a v0 bootstrap (no sync,
  just snapshot-replace-on-install) ship earlier as a separate
  track? Pick + justify. Append to ROADMAP's "Bootstrapping
  milestone" section + cross-link to BOOTSTRAP.md.

## Phase C — Stability + sharing focus (other itch)

- [ ] **T7: Read vault `Sync and Distribution/` + `CRDTs/` + the
  recent `dl-2025-11-12-ux-thinking.md` thoroughly.** Capture
  in `STABILITY-AND-SHARING.md` (new): what "stable" means
  concretely (named, hashed, validated, durable); what "sharing"
  means (events crossing the wire to who, with what permissions);
  the namespace-owner model from 2025-11-12; the approval flow.

- [ ] **T8: Design the wire protocol.** What endpoints does
  `matter.darklang.com` expose? What's the auth shape? What's the
  schema for the events crossing the wire (PackageOpEvent,
  BranchOpEvent, etc.)? Reuse the LibMatter spec; refine where
  unclear. Append to STABILITY-AND-SHARING.md.

- [ ] **T9: Sequence the sharing work.** Concrete steps from
  "local-only" → "ocean and stachu both push to matter.darklang.com"
  → "any user can join." Identify blockers. ~6-10 named steps.
  Append to STABILITY-AND-SHARING.md.

- [ ] **T10: Decide phase for stability+sharing.** Same shape as
  T6: pick a phase, justify, cross-link. Append to ROADMAP.

## Phase D — Identity + permissions (gates everything)

- [ ] **T11: Read vault `Accounts and Auth/` + `Purity, Effects,
  and Sandboxing/`.** Capture in `IDENTITY.md` (new): the
  agent-and-human identity model from COHABITATION + how it maps
  onto Accounts and Auth + how it threads through CAPABILITIES.
  Sketch the Agent data shape (id, identity, currentGoal, plan,
  permissionSet, traceHead — from COHABITATION).

- [ ] **T12: Design permission delegation.** A human owns
  namespace `User.Stachu`. They run an agent. The agent acts
  *as* them with bounded caps. How is the delegation modeled?
  What's recorded in the trace? How is it revoked? Append to
  IDENTITY.md.

- [ ] **T13: Decide phase for identity.** Identity gates sharing
  (T7-T10) and capabilities-meaningful (T15). Where does it land?
  Append to ROADMAP.

## Phase E — Substrate-piece deepening (each sketch → design-grade)

For each sketch that hasn't been refined enough, an iter that
turns it into a design-grade doc with concrete F# substrate
changes, concrete Dark code shapes, concrete SQLite schema where
applicable.

- [ ] **T14: Deepen CONFLICTS-AND-RESOLUTIONS.** Add: precise
  Conflict variants list, the ConflictDispatch signature on
  ExecutionState, concrete examples for each variant, the
  recording table schema (conflict_log? resolution_log?). Update
  the sketch in place; mark "v0 design" at top.

- [ ] **T15: Deepen CAPABILITIES.** Add: the install-time grant
  UX, the per-session granted set storage, the per-builtin
  declaration site, the conflict-routing impl, the audit-log
  schema. Update sketch in place.

- [ ] **T16: Deepen EVENT-STREAMS-AND-PARKING.** Add: the F#
  Stream<T> impl shape, the scheduler's wait-list data
  structure, the Promise<T> / `!` surface in Dark. Persistence
  question — durable streams vs ephemeral. Update sketch in
  place.

- [ ] **T17: Deepen HOT-RELOAD.** Add: the dependency-tracking
  index needed for "find all dependents of fn X." Concrete API.
  Update sketch in place.

- [ ] **T18: Deepen COMPOSABLE-MVU.** Add: the F# Elmish-loop
  runtime sketch (the ~500-LoC substrate). The View tree types.
  Dark-side `Stdlib.UI` primitive list. Update sketch in place.

## Phase F — Cross-cutting design

- [ ] **T19: SQLite schema for the substrate.** One doc:
  `SCHEMA.md` (new). All tables: package_fns / package_types /
  package_values / locations / patches / patch_parts / branches /
  branch_patches / ops / events / agents / capabilities_log /
  conflicts_log / trace_events. Per table: columns + indexes +
  invariants. Mark which tables exist today vs new.

- [ ] **T20: The F#-vs-Dark line.** One doc: `F-SHARP-VS-DARK.md`
  (new). For each subsystem (interpreter, parser, materializer,
  PM, sync, caps, events, UI), what's the irreducible F# bit and
  what's Dark code on top. Per FRONTIER's "what F# should stop
  knowing" but more concrete. The line moves over time; capture
  v1 split and v2 (post-Dark-interpreter-in-Dark) split.

- [ ] **T21: Migration path from current state.** One doc:
  `MIGRATION.md` (new). Starting state = today's `main` branch
  (CLI runs on `.dark` files + the existing LibMatter ops + the
  in-progress PDD spike, ignored). Ending state = the substrate
  per ROADMAP. List the ordered transitions in feature-shippable
  chunks (no "big bang" rewrites). Include rollback notes.

- [ ] **T22: Agent runtime.** How does an agent actually *run*?
  Is it a process? A thread? A frame on the same VM as the
  user's CLI? What does spawning + observing + cancelling look
  like? One section in COHABITATION.md (extend) or a new
  `AGENT-RUNTIME.md`. (Decide which based on size.)

- [ ] **T22b: Remote access + control.** One doc:
  `REMOTE-ACCESS.md` (new). Cover: (i) the goal — reach Dark
  instances you own / have permission for, across devices,
  peer-to-peer (Plan 9 vibes); (ii) the vault stance — lean on
  Tailscale (`Networking and Internet/Tailscale.md`) rather than
  build a networking stack; (iii) how Tailscale primitives map
  to substrate needs (peer addressing via MagicDNS, identity via
  WhoIs + HTTP headers, TLS via `tailscale serve`, public surface
  via `tailscale funnel`, ACL via tags+grants); (iv) what Dark
  still has to build on top (the wire protocol from T7-T9, the
  identity-to-tailnet-user binding from T11-T12, app-level
  permissions for "this remote agent can run on my instance");
  (v) deployment shape — every user's machine is a peer; one
  user can have N peers; matter.darklang.com is a special peer;
  (vi) how this interacts with the cohabitation model — agents
  spawned remotely, viewers attached remotely, sessions
  spanning multiple peers. Append a row to ROADMAP §"Chunks
  needed" if not already there (C16). Decide phase ordering —
  this likely lands *with or after* sharing (T10) since both
  rely on the wire protocol + identity.

## Phase G — Validate + decide

- [ ] **T23: MVP-cohabitation demo.** Define the smallest demo
  that proves the substrate works for cohabitation. Probably:
  "Stachu and Ocean each on their own machine, on a shared
  branch, with one agent helping; an op produced by Stachu shows
  up in Ocean's viewer within 5s; if Ocean is also editing, a
  conflict surfaces and gets resolved." Specify the demo
  precisely + what it requires. Append to ROADMAP.

- [ ] **T24: Phase ordering.** Lay out Phases 0/1/2/3/… in
  ROADMAP. Each phase has: what's in it, what it unlocks, what
  blocks the next phase. Make sure bootstrapping (T6) and
  stability+sharing (T10) have explicit phase numbers, not "TBD."

- [ ] **T25: Critical path.** What's the absolute shortest
  sequence to MVP-cohabitation? Skip phases not on it. Identify
  which work can be parallel. Append to ROADMAP.

- [ ] **T26: Risks audit.** What kills this? What's hard? What's
  optimistic? What's tested vs hypothetical? Append to ROADMAP.
  Be honest, not bullish.

- [ ] **T27: Open decisions.** List every decision the substrate
  needs to lock that isn't yet locked. Per decision: options,
  recommendation, blocker if any. Append to ROADMAP.

## Phase H — Final pass

- [ ] **T28: ROADMAP polish pass.** Re-read end-to-end. Trim
  redundancy. Make sure each section is self-contained. Make
  sure the user can read just ROADMAP without consulting other
  docs (cross-references are fine but the spine should stand
  alone). Set status to `DONE`.

- [ ] **T29: Set up the next loop.** Based on what the roadmap
  reveals, what's the *first* sub-loop the user might want next?
  (e.g., "land Phase 0 in code" or "validate the bootstrap demo
  end-to-end.") Don't actually create it — but note it in
  ROADMAP §"What comes after this loop" so the user knows.

- [ ] **T30: Final commit + surface to user.** Final commit with
  full stats (LoC added across new docs, # of new docs, etc.).
  Surface to user with: "ROADMAP.md is ready; here's the headline
  answer to your 4 questions (chunks / order / bootstrap-when /
  share-when). Want me to print it?"

---

## How to choose batches

The TODOs are roughly ordered by dependency. Some batching guides:

- **T1+T2** together — both bootstrap the roadmap structure
- **T3+T4+T5** together — all about bootstrap design
- **T7+T8** together — both about wire protocol
- **T22+T22b** can chain — agent runtime + remote access share concerns (process model, peer addressing, cross-instance identity)
- **T11+T12** together — both about identity
- **T19+T20** together — both schema/architecture, similar reading
- **T23+T24+T25** together — closing strategic synthesis
- **T26+T27** together — both risk-shape
- **T28+T29+T30** together — wrap-up

Otherwise: pick the next unchecked + maybe one related.

## How to handle uncertainty

If a TODO requires a decision and there's no clear answer:

1. Look at vault for prior thinking; prefer dated recent
2. Look at substrate sketches for stated stance
3. If still unclear: write the doc with "Open decision:" and
   present 2-3 options + a recommendation. Add the open decision
   to ROADMAP §"Open decisions." Move on.

Don't get stuck. The loop's job is to produce a *roadmap*, not
to settle every open question.

---

## After

*Filled in by T30.*
