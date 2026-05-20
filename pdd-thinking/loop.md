# Substrate-Roadmap Design Loop

> **PREAMBLE FOR THE LOOPER (read this first, every iter):**
>
> You are an iter of a **5-minute** self-paced loop. Your job is to
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
> 7. Schedule the next wake at **5 min** (or stop if all TODOs done +
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

**NEXT:** `T23 + T24 + T25` (strategic closing — MVP demo + phase plan + critical path)

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

- [x] **T3+T4+T5+T6: Bootstrap focus batch — all done.**
  Headline finding from main check: `LibDB.Seed.export` +
  `growIfNeeded` already exist; kill-and-fill schema mature;
  remaining work is decoupling end-user bootstrap from
  `LoadPackagesFromDisk`. BOOTSTRAP.md produced (220 LoC):
  current state walkthrough, target state, snapshot format
  (Option A vs B), 9-step work sequencing (bootstrap-1 through
  bootstrap-9) with dep graph, **phase decision: Phase 1 (local,
  ~2-3 wks) + Phase 3 (networked, depends on T7-T10)**,
  6 open questions (Q-bs-1 to Q-bs-6) added to ROADMAP §Open
  decisions. ROADMAP §Bootstrapping milestone now populated.

## Phase C — Stability + sharing focus (other itch)

- [x] **T7+T8+T9+T10: Sharing batch — all done.** From 30-sec
  main check: no sync code on main; HTTP server/client builtins
  exist; package_ops table has propagation_id for PropagateUpdate;
  Dark-side `cli/scm/commit.dark` + `status.dark` already mature.
  STABILITY-AND-SHARING.md produced (~320 LoC): stable + sharing
  definitions (3 modes: self/pair/public); namespace-ownership
  model from 2025-11-12; **wire protocol — 6 endpoints (snapshot,
  events, whoami, live ws) + 5 SyncEvent variants** leaning on
  Tailscale per vault stance; replication topology (matter.darklang.com
  + optional p2p); **share-1 through share-10** sequencing with
  dep graph; **phase decision: Phase 3** (share-1 belongs to
  Phase 2 alongside identity; share-9 + share-10 are the
  goal-line). 6 open decisions (Q-ss-1 to Q-ss-6) appended to
  ROADMAP. ROADMAP §Stability+sharing milestone populated.

## Phase D — Identity + permissions (gates everything)

- [x] **T11+T12+T13: Identity batch — all done.** Main check:
  accounts_v0 + 4 seeded humans + login.dark exist but no auth,
  no agent kinds, no delegation, no external-id binding. 53
  account_id refs across backend. IDENTITY.md (~320 LoC):
  - `IdentityKind = Human | Agent of owner: AccountId` typed sum
    plus `TrustProfile = Untrusted | Basic | Trusted | System`.
  - **`account_identities` table** for binding to Tailscale /
    OAuth / tokens (multiple identities → one account).
  - **`Agent` shape**: account + currentGoal + plan + traceHead
    + status (Running/Paused/Done/Failed/Revoked).
  - **Delegation contract** with triple-intersection
    effective_caps (agent.trust ∩ delegation.caps ∩ owner.session).
  - Sub-delegation chain (must be ⊆ parent; revoked transitively).
  - Delegations are content-addressed ops on the wire (sync just
    like package ops).
  - Cap-denials route through B2 dispatch to the agent's owner.
  - Phase decision: **Phase 2 (Identity + caps + conflicts)**,
    optionally split 2a (humans + sharing-blocking) / 2b
    (agents + PDD-blocking).
  - AI-opt-in honored: agent identities are opt-in; reverting
    Phase 2b leaves a fully human-only Darklang.
  - 6 open decisions Q-id-1 to Q-id-6 added to ROADMAP.

## Phase E — Substrate-piece deepening (each sketch → design-grade)

For each sketch that hasn't been refined enough, an iter that
turns it into a design-grade doc with concrete F# substrate
changes, concrete Dark code shapes, concrete SQLite schema where
applicable.

- [x] **T14: Deepen CONFLICTS-AND-RESOLUTIONS.** Marked "v0
  design" at top. Added: (a) "What exists on main today" —
  grounded against `LibDB/Rebase.fs` RebaseConflict + Merge.fs
  + ProgramTypes.fs:670-722 Constraints comments. (b) New
  schema: `conflicts_v0` + `conflict_resolutions_v0` tables,
  content-addressable + syncable. (c) Concrete F# shape:
  `ConflictDispatch = Conflict -> CallContext -> Ply<Resolution>`
  field on ExecutionState; `CallContext` carries agent +
  delegation + tolerance + capsGranted + callSite + trace.
  3-layer dispatch chain (auto-rule → policy → park+ask).
  `whoToAsk` routes per IDENTITY: agent-cap-deny → owner;
  SCM-op-conflict-in-Y's-namespace → Y. (d) Concrete examples:
  FnNotFound, SCM op-vs-op (mapped to existing RebaseConflict),
  CapabilityDenied (new), SyncDivergence (post-share-5).

- [x] **T15: Deepen CAPABILITIES.** v0 design grade. Main check:
  no caps on main, but `Previewable` enum is adjacent + Builtins
  already split into 9 assemblies (Pure / Http.Client /
  Http.Server / Random / Time / Cli / CliHost / Language /
  Matter). Cap retrofit = per-assembly defaults + per-fn
  overrides, not from-scratch annotation. Added: (a) what
  exists on main; (b) **per-assembly default caps table** + 4
  new cap tags (CapBindPort, CapWriteStdout, CapReadStdin,
  CapInvokeLLM-as-AI-opt-in-gatekeeper); (c) **schema**:
  `capability_grants_v0` + `capability_log_v0` tables (audit
  pivots fall out); (d) **F# integration** at Apply for builtin
  calls — concrete code sketch wiring cap-check into the
  conflict dispatch from T14; (e) BuiltInFn gets a
  `capabilities` field + per-assembly helper defaults;
  (f) install-time grant UX flow with AI-opt-in honored
  (CapInvokeLLM never auto-prompts); (g) Previewable vs
  Capability orthogonality clarified (don't conflate).

- [x] **T16: Deepen EVENT-STREAMS-AND-PARKING.** v0 design grade.
  Big finding from main check: `backend/src/LibExecution/Stream.fs`
  already exists (~292 LoC) — but it's for **data streams**
  (lazy, single-consumer, pull-based, IO body iter). The
  event-coordination substrate is different (push, multi-sub,
  control-plane). **Renamed to `EventBus<T>`** to avoid the
  collision. Added: (a) reality check + Stream-vs-EventBus
  comparison table; (b) concrete F# EventBus<T> shape +
  Subscription + waitForOne; (c) `RuntimeBuses` record on
  ExecutionState (8 system buses); (d) `Scheduler` with parked-
  frame wait-list + EventSelector sum; (e) 8-step park-and-wake
  walkthrough; (f) Dark-side Promise<T> + `!` compiling to
  `waitForOne`; (g) persistence-per-bus table (which durable to
  which sqlite table); (h) Ply coexistence (park inserts inside
  a Ply, doesn't replace).

- [x] **T17: Deepen HOT-RELOAD.** v0 design grade. Big main-check
  finding: `package_dependencies` table + 4 indexes (incl partial
  index for the location-dependents query); `PropagateUpdate` op
  + `RevertPropagation` op already in PT enum; `Queries.fs` has
  the dependent-finder SQL; `PackageManager.fs` has caches with
  explicit invalidation TODOs. **The dependency-tracking index
  isn't something we build — it exists.** Hot-reload work is
  *connecting* existing dep-tracking → event-bus publication.
  Added: reality-check section, publish-on-op-apply F# sketch
  (PackageOpPlayback.fs gets the publish step), subscriber-side
  cache invalidation + frame-policy, type-sig-change /
  cap-surface-change → B2 conflict dispatch routing, branch-
  switch as bulk reload with transactionEnd markers, "what's
  NOT hot-reload" disambiguation (schema migrations / identity
  changes / config — separate machinery).

- [x] **T18: Deepen COMPOSABLE-MVU.** v0 design grade. Huge
  main-check finding: **MVU framework already exists** at
  `packages/darklang/cli/`. `SubApp` type with onKey/onDisplay/
  onSave + SubAppAction sum + Page sum + AppState fat record +
  apps/{outliner, review, views} all real and working.
  Substrate work is *evolution*, not greenfield. Added: (a)
  what exists on main; (b) 6-step evolution path (Msg type →
  structured View → subscriptions → Effects channel → real
  composition via Spawn → trace replay via Msg-log replay);
  (c) F# substrate sketch (~500 LoC Loop/tick/applyEffects/
  RenderTarget); (d) **before-vs-after migration table**
  showing each existing field's evolved form; (e) per-app
  migration is mechanical and incremental (each moves when
  convenient).

## Phase F — Cross-cutting design

- [x] **T19+T20 batch: SCHEMA.md + F-SHARP-VS-DARK.md.** Both done
  in one iter (same main-check data).
  
  SCHEMA.md: 18 tables on main inventoried by role
  (bookkeeping / branches+commits+ops / projections / traces /
  legacy); 7 new tables proposed across substrate sketches
  (account_identities, delegations, conflicts_v0,
  conflict_resolutions_v0, capability_grants_v0,
  capability_log_v0) + 1 column add (package_ops.delegation_id);
  cross-table relationships graph; invariants (one-concern-per-
  table, content-addressed where possible, append-only logs
  separate from mutable state, NULL conventions, indexes match
  query shape, kill-and-fill stays); 5 open decisions Q-sch-1
  to Q-sch-5.
  
  F-SHARP-VS-DARK.md: 11 F# projects + ~16 Dark subdirs on main
  (much more Dark-side than sketches assumed — scm/tracing/
  llm/cli all already Dark). v1 split table per subsystem
  (interpreter / storage / identity / bootstrap / apps /
  materializer / tracing). v2 split (post Dark-in-Dark, years
  out — just leaves space). 'What stays F# forever' list
  (Prelude, bottom-of-interpreter, DB driver, tree-sitter,
  network primitives, cap-check at Apply, scheduler
  primitives). Migration shape: gradual + per-subsystem; 3
  examples. 4 open decisions Q-fd-1 to Q-fd-4.

- [x] **T21: MIGRATION.md.** ~400 LoC. Ordered shippable-chunk
  plan from main (commit 61cb32ac7) to substrate state. 5 phases
  with rollback notes per chunk: **P0** readiness (4 chunks —
  audit + verify Seed.export + inventory RTE/cap sites), **P1**
  bootstrap (6 chunks — separate build-seed CLI, CI builds seed,
  first-run detection, relocate LoadPackagesFromDisk to
  LibBuildTools, delete packages/*.dark, edit-time-only LibParser),
  **P2a** humans+conflict-dispatch (10 chunks — Conflict types,
  dispatch field, Rebase.getConflicts migration, Capability field
  on BuiltInFn, per-assembly cap annotation, install-time grant UX),
  **P2b** agents+LLM-gating (5 chunks — kind column, agent
  spawn/list/revoke, triple-intersection cap-check,
  CapInvokeLLM/CapSendSecret as AI-opt-in gatekeepers, first
  errors-as-conflicts migration), **P3** sharing+remote-access+
  viewer (13 chunks — EventBus, sync endpoints, autosync,
  approval ops, matter.darklang.com deploy, MVP-cohabitation
  goal-line as P3-11, viewer app, remote-access doc), **P4**
  frontier (7 chunks — agent runtime, hot-reload subscriber,
  WebSocket, public funnel, multi-peer p2p, remaining RTE
  migrations, materializer-in-Dark). Cross-cutting principles
  (AI-opt-in, local-first, reversibility, tests, kill-and-fill,
  reality grounding) maintained across phases. 6 open
  decisions Q-mig-1 to Q-mig-6.

- [x] **T22+T22b: combined REMOTE-ACCESS.md.** Both done in one
  iter (they share concerns: process model + peer addressing +
  cross-instance identity). Main check: `packages/darklang/llm/
  agent.dark` exists (provider-agnostic agent framework over
  Anthropic/OpenAI/Ollama with tool-call loop + retries) but no
  long-running daemon; no peer access on main. Tailscale.md
  vault stance: lean on Tailscale, don't build a network
  stack. **Doc covers**: agent runtime as long-running thread
  (AgentProcess + AgentThreadHandle types; spawn/observe/cancel/
  revoke flow; sub-agents per IDENTITY); wire endpoints
  (POST /exec, GET /agents, POST /agent/<id>/cancel, GET
  /devices); `dark on <peer> <cmd>` flow with Tailscale-User-
  Login auth; offline-queue resilience via package_ops with
  target_peer; new caps (CapRemoteExec / CapRemoteObserveAgents /
  CapRemoteControlAgents / CapPeerSync) defaulting to
  same-owner=granted, others=denied; cross-instance agent
  identity (delegation ops sync; major recognizes csv-helper
  by account_id); what's out-of-scope for v1 (F# tsnet
  binding, public funnel, multi-tenant cross-org isolation,
  agent state migration); 7-chunk sequencing RA-1 to RA-7
  across Phase 2b-3-4; 6 open decisions Q-ra-1 to Q-ra-6.

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
