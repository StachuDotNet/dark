# Roadmap — Toward the Distributed Substrate

The deliverable of the substrate-roadmap design loop. Answers:

1. **What chunks are needed?**
2. **What order can they be done in?** (with blockers + parallel tracks)
3. **When does package bootstrapping ship?**
4. **When does stability + sharing ship?**
5. **What's MVP-cohabitation?** (smallest demo proving the substrate works)

Cross-link to substrate sketches in this directory (`COHABITATION.md`,
`CONFLICTS-AND-RESOLUTIONS.md`, etc.) for the underlying design.

---

## Foundational constraints

**Constraints, not preferences.** Design choices that violate these
must be rejected even if convenient.

- **AI involvement is wholly opt-in.** Darklang must work fully
  without any AI. PDD, the materializer, the LLM agent, the
  prompt daemon — every AI-flavored feature is opt-in, behind
  an explicit grant from the user. Someone running Dark for the
  first time should see no LLM call, no prompt for an API key,
  no degraded experience for refusing. The substrate is
  AI-friendly, not AI-mandatory.
  - Implication: capability tags include AI ones (`CapInvokeLLM`,
    `CapSendSecret`) — denied by default.
  - Implication: every chunk in the roadmap must have a non-AI
    path. If a feature only works with AI on, it's not a
    Darklang feature — it's a separate experience layered on top.
  - Implication: the "agent is just an app" framing in
    `COHABITATION.md` is correct because agents are *opt-in
    inhabitants*, not foundational ones.
- **Local-first.** (From ethos.) Software runs on your machine;
  no cloud-mandatory features.
- **Open + transparent.** (From ethos.) Show what the system is
  doing; default to making things inspectable.

## How to read this doc

The roadmap is built up across loop iters. Each iter appends to or
refines sections below.

**Critical reality check** (do not skip): the sketches in this
directory were written from spike-era recollection + vault notes.
**The actual state of `main` is much further along than the sketches
imply.** This roadmap is grounded in what `main` *actually* contains
as of 2026-05-20 — not in what the sketches assumed.

Read this section first; then jump to the section that answers
your question.

---

## Reality check: what `main` actually has today

Recorded T1 (2026-05-20). Confirmed via `git show main:...`.

**SQLite schema (18 tables already in `backend/migrations/schema.sql`):**

```
system_migrations_v0    -- bookkeeping
accounts_v0             -- agents/users; Darklang/Stachu/Paul/Feriel seeded
branches                -- branch model; main pre-seeded; parent_branch_id,
                          base_commit_hash, archived_at, merged_at
commits                 -- hash-keyed; author via account_id; per-branch
package_ops             -- branch-scoped; commit_hash NULL = WIP; applied flag;
                          propagation_id for PropagateUpdate
branch_ops              -- content-addressed; idempotent op replay
package_types / values / functions / blobs   -- the content store
locations               -- name → ID mapping
deprecations            -- soft-delete
package_dependencies    -- the dependency graph (already explicit!)
traces / trace_fn_calls -- tracing storage
user_data_v0 / toplevels_v0 / scripts_v0     -- legacy/transitional
```

The schema uses **kill-and-fill** (rebuild on schema change rather
than incremental migrations) — read the schema's preamble for
context.

**F# substrate (`backend/src/`):**

- `LibDB/PackageManager.fs` — package lookup/search
- `LibDB/PackageOpPlayback.fs` — apply ops to package tables
- `LibDB/BranchOpPlayback.fs` — apply ops to branch tables
- All the LibExecution / LibParser / LibSerialization machinery
  we know about.

**Dark-side code (`packages/darklang/`):**

- `cli/scm/` — branch, commit, merge, rebase, discard, log,
  showCommit, status. **All in Dark.** SCM commands aren't F#.
- `cli/commands/agent.dark` — agent command
- `cli/prompt.dark` — prompt handling
- `llm/agent.dark` + `llm/examples/*.dark` — Dark-level LLM agent
  with worked examples (code-agent, git-commit, research, etc.)
- `languageServerProtocol/` — LSP plumbing
- `languageTools/packageManager.dark` — Dark-side PM

**Implications for the roadmap:**

- "Build conflicts+resolutions" is *partly already done*. The
  package_ops + branch_ops + commit-hash structure is the
  foundation. What's missing is the *dispatch policy* —
  detection exists; the resolution choice flow doesn't.
- "Build sync" → the ops are content-addressed and idempotent
  (per branch_ops `id TEXT PRIMARY KEY = content hash`). The
  *wire protocol* + identity-across-instances is what's missing.
- "Identity + accounts" → already a table; just not used much.
- "Agent as an app" → already in Dark (`llm/agent.dark`).
  Becoming-substrate is about *integrating it with caps + the
  event bus*, not building from scratch.
- "PM is an app" → it's already in `packages/darklang/cli/scm/`
  and `packages/darklang/languageTools/packageManager.dark`. The
  substrate-app shape exists.
- "Composable MVU" — needs confirmation; `cli/` has Elmish-shaped
  code worth verifying.
- "Removing `.dark` files" → this is the explicit goal of
  `Package Bootstrapping/` vault. The schema is ready (kill-and-
  fill + the package tables); the bootstrap protocol isn't.

The roadmap below is **gap-to-substrate-grade**, not
build-from-zero.

---

## Chunks needed

Recorded T2. Each chunk is a coherent unit of substrate work.
Status reflects `main` as of 2026-05-20.

| # | Chunk | Role | Status on main | Substrate sketch |
|---|---|---|---|---|
| C1 | **Schema + ops storage** | The 18-table SQLite store with kill-and-fill migrations; package_ops + branch_ops as event log | **EXISTS** (mature) | (covered indirectly by SYNC + CONFLICTS) |
| C2 | **Op execution (LibMatter equiv)** | PackageOpPlayback + BranchOpPlayback; validate-then-apply | **EXISTS** (in F#) | CONFLICTS-AND-RESOLUTIONS |
| C3 | **Conflict detection** | Detecting op-vs-op + location collisions before apply | **PARTIAL** — likely some validation; needs design audit | CONFLICTS-AND-RESOLUTIONS |
| C4 | **Conflict resolution dispatch** | The policy layer (auto rule → policy → park+ask → fail) for both SCM and runtime conflicts | **NEEDS DESIGN** | CONFLICTS-AND-RESOLUTIONS |
| C5 | **Identity model** | accounts_v0 + delegation + per-agent permissions | **PARTIAL** (table exists, seeded; not deeply used) | COHABITATION + (new) IDENTITY.md |
| C6 | **Capabilities** | Cap tags on builtins; cap check at Apply; grant model | **NEEDS DESIGN + IMPL** | CAPABILITIES |
| C7 | **Sync wire protocol** | matter.darklang.com endpoints; auth; SyncEvent schema | **PARTIAL** (ops are content-addressed; protocol unspecified) | SYNC-AND-STABILITY |
| C8 | **Package bootstrapping** | Snapshot-based install; remove `.dark` files from repo | **NEEDS DESIGN + EXECUTION** | SYNC-AND-STABILITY + (new) BOOTSTRAP.md |
| C9 | **Event streams + parking** | Typed streams in F#; scheduler parking on event selectors; promises in Dark | **NEEDS DESIGN** | EVENT-STREAMS-AND-PARKING |
| C10 | **Hot-reload semantics** | Body-changed event flow; mid-execution policy; SCM-branch-switch as bulk reload | **PARTIAL** (some reload exists for dev) | HOT-RELOAD |
| C11 | **Composable MVU runtime** | Elmish-loop F# substrate (~500 LoC); View tree; effects executor | **PARTIAL** — Dark CLI has Elmish-shape code; audit needed | COMPOSABLE-MVU |
| C12 | **Agent runtime + integration** | Agent-as-an-app; intent data model; agent gets caps via the same flow | **PARTIAL** (Dark agent.dark exists; not substrate-integrated) | COHABITATION (+ T22 deepening) |
| C13 | **The PDD materializer** | LLM-driven body materialization for Pending fns; eventually a Dark fn | **DEFERRED** (spike on pdd branch; not on main) | ALGORITHM |
| C14 | **Viewer (in-focus-fn UI)** | The cockpit; MVU app over the event bus | **NEEDS BUILD** | VIEW-SKETCHES |
| C15 | **matter.darklang.com server** | Central instance for sharing; hosts snapshot + event stream | **NEEDS BUILD** | SYNC-AND-STABILITY |
| C16 | **Remote access + control** | Reach Dark instances you own / have permission for, across devices, peer-to-peer. Vault says lean on Tailscale (peer addressing + identity + TLS + auth headers all free). | **NEEDS DESIGN** | (new) REMOTE-ACCESS.md |

**Counts:** 4 EXISTS-or-mature · 6 PARTIAL · 5 NEEDS-DESIGN/BUILD · 1 DEFERRED.

The substrate is **more than half there**. The roadmap focuses on
the 6 partials + 5 needs-design/build, in dependency order.

---

## Order + blockers + parallel tracks

Filled by T25 (critical path).

The substrate is structured so that two largely-independent
tracks run in parallel:

```
TRACK A (LOCAL)                    TRACK B (NETWORKED)
─────────────────                   ─────────────────────
Phase 0 (audits)                    [waits for Phase 2]
   ↓
Phase 1 (bootstrap)
   .dark files gone
   ↓
Phase 2a (humans + dispatch)        Phase 3 (sharing + viewer)
   identity binding ──────────────→ depends on identity binding
   cap-check infra                  + dispatch infra
   conflict dispatch
   ↓
Phase 2b (agents + LLM gate) ─────→ Phase 3 (agent identity sync)
   ↓
                                    Phase 3 P3-11
                                    MVP-COHABITATION DEMO
                                    ↓
Phase 4 (frontier)                  Phase 4 (frontier)
   hot-reload, RTE migration        WebSocket, p2p, public funnel
```

**Track A** is local: bootstrap, dispatch, caps, agents.
Runs without network. Can ship on a single machine.

**Track B** depends on Track A's primitives (identity, dispatch,
caps) but adds the wire protocol + matter.darklang.com.

**Parallelism** is real: while Phase 2a is being implemented,
Phase 1 can ship; Phase 3 design work can be ongoing (it's been
done in this loop). Multiple devs / multiple weeks of focused
work can run in parallel within and across phases.

---

## Phase plan

Provisional ordering from earlier docs, now locked. Filled by
T24.

### Phase 0 — Readiness (1 week, parallel-able)

Audit + doc-comment pass. No code change. Can run alongside
Phase 1.

- Per MIGRATION P0-1 to P0-4.

### Phase 1 — Bootstrap (~2-3 weeks)

Removes `.dark` files from the repo. Local-only; no identity /
sharing / cap dependency.

- Per MIGRATION P1-1 to P1-6.
- Outcome: snapshot-based install; LibParser edit-time-only;
  CI faster; repo smaller.
- **Lands the user's first itch.**

### Phase 2 — Identity + capabilities + conflicts (~4-6 weeks)

The bridging phase. Two sub-phases.

**Phase 2a — Humans + dispatch foundation** (~2-3 weeks)
- Per MIGRATION P2a-1 to P2a-10.
- Conflict types + dispatch field (default = FailLoudly preserves
  behavior)
- conflicts_v0 + conflict_resolutions_v0 tables
- Capability field on BuiltInFn (default empty)
- Cap-check in Apply (fast-path empty set)
- Per-assembly cap annotations
- Install-time grant UX
- **Outcome**: identity + cap-check + dispatch wired; existing
  behavior preserved by defaults.

**Phase 2b — Agents + LLM gating** (~2 weeks)
- Per MIGRATION P2b-1 to P2b-5.
- `kind=Agent` accounts + delegations + per-agent caps
- `CapInvokeLLM` + `CapSendSecret` as AI-opt-in gatekeepers
- First errors-as-conflicts migration (DivideByZero as pattern)
- **Outcome**: agents become first-class but opt-in. AI-features
  gated.

### Phase 3 — Sharing + remote access + viewer (~4-6 weeks)

The **user's primary milestone**. P3-11 = MVP-cohabitation demo.

- Per MIGRATION P3-1 to P3-13.
- EventBus<T> in F#
- Sync endpoints (Dark HTTP handlers)
- Autosync cron + ApprovalRequest ops + approve CLI
- Deploy matter.darklang.com
- Bootstrap-from-network (BOOTSTRAP bootstrap-8)
- PDD viewer app (behind CapInvokeLLM)
- Remote-access primitives + `dark on <peer>` (per
  REMOTE-ACCESS.md)
- **Outcome**: two humans on two machines sharing a substrate.
  matter.darklang.com hosts. Both itches scratched.

### Phase 4 — Frontier (open-ended)

Per MIGRATION P4-1 to P4-7.

- Long-running agent runtime
- Hot-reload via BodyChanged subscriber + mid-execution policy
- WebSocket sync live-push
- Public funnel for matter.darklang.com
- Multi-peer p2p sync topologies
- Remaining ~55 raiseRTE sites migrated to dispatch
- Materializer-as-Dark-fn (`Stdlib.PDD.materialize`)
- Composable MVU evolution (Msg type, Effects channel,
  subscriptions, View tree)
- View sketches realized as a real app

### Timeline (very approximate)

- Phase 0: week 0 (parallel)
- Phase 1: weeks 1-3 (bootstrap)
- Phase 2a: weeks 3-5 (humans)
- Phase 2b: weeks 5-7 (agents)
- Phase 3: weeks 7-13 (MVP-cohabitation goal-line)
- Phase 4: weeks 13+ (open-ended frontier)

**~13 weeks to MVP-cohabitation** in optimistic-but-realistic
focused-work terms. ~3 months from start to demo. Could compress
or stretch by 30-50%.

---



---

## Critical path to MVP-cohabitation

Per T25. The minimum sequence of work to hit P3-11 (the demo).
Skip phases not on it; parallelize what can be parallel.

The critical path is **all of Phase 1 + Phase 2a + Phase 3.1-3.11**
(skipping Phase 2b for the demo's basic human-only variant —
agents are the optional step at t=15-30 in the demo script).

```
[Phase 1 bootstrap]           [Phase 2a humans+dispatch]
P1-1 build-seed CLI           P2a-1 account_identities
   ↓                              ↓
P1-2 CI builds seed           P2a-2 Conflict + dispatch field
   ↓                              ↓
P1-3 first-run install        P2a-3 conflicts_v0 schema
   ↓                              ↓
P1-4 LoadPM relocate           P2a-4 Rebase → Conflict.OpVsOp
   ↓                              ↓
P1-5 delete .dark             P2a-5 Capability field
   ↓                              ↓
P1-6 LibParser edit-only       P2a-6 capability_grants_v0
                                  ↓
                              P2a-7 cap-check in Apply
                                  ↓
                              P2a-8 Pure cap annotation
                                  ↓
                              P2a-9 Http.Client + CliHost caps
                                  ↓
                              P2a-10 install-time grant UX

         [Phase 1 + Phase 2a converge at this point]
                              ↓
                         [Phase 3 share work]
                              ↓
                         P3-1 EventBus F# infra
                              ↓
                         P3-2 conflicts/cap → persistence-bus
                              ↓
                         P3-3 GET /sync/snapshot
                              ↓
                         P3-4 GET /sync/events
                              ↓
                         P3-5 GET /sync/whoami + Tailscale
                              ↓
                         P3-6 POST /sync/events
                              ↓
                         P3-7 autosync cron
                              ↓
                         P3-8 ApprovalRequest ops + CLI
                              ↓
                         P3-9 deploy matter.darklang.com
                              ↓
                         P3-10 bootstrap-from-network
                              ↓
                         P3-11 onboard Ocean → MVP demo
```

**Off the critical path** (can land anytime after their deps):

- Phase 0 audits — pure parallel
- Phase 2b agent work — needed only for demo step t=15+
  (optional)
- P3-12 PDD viewer — optional polish for the demo
- P3-13 REMOTE-ACCESS docs + Tailscale-based peer-reach UX —
  optional polish
- Phase 4 entirely — frontier work

### Compression opportunities

If we wanted to ship MVP-cohabitation **faster**:

- **Skip Phase 2b entirely** for the demo. The basic two-human
  case works without agents. Reduces critical path to ~10 weeks.
- **Skip P2a-8/-9/-10 cap annotations** in the critical path —
  caps can stay empty (always-permitted) for the demo. Ship the
  *infrastructure* in P2a-2/-5/-7; defer real enforcement.
  Reduces by 1 week.
- **Single-user demo first** (Stachu + matter.darklang.com).
  Cuts P3-7/-8 (approval flow) from the critical path. Reduces
  by 1 week.

Aggressive compression gets MVP-cohabitation to **~8-9 weeks**.

### Critical-path risk concentration

The most-likely-to-slip chunks (rank ordered):

1. **P3-9 (deploy matter.darklang.com)** — first deploy of a
   real Dark instance behind Tailscale + sync. Lots of unknowns
   (DNS, auth, persistence, monitoring). Buffer 1-2 weeks.
2. **P2a-7 (cap-check in Apply)** — touching the hot path in
   the interpreter. Performance and correctness both at stake.
   Buffer 1 week.
3. **P3-6 (POST /sync/events with idempotent apply)** — first
   real test of the conflict dispatch's role in handling
   inbound state. Concurrent-safety edge cases. Buffer 1 week.

Add these buffers: realistic critical path is **~14-15 weeks**
to MVP-cohabitation in conservative-but-honest terms.

---

## Bootstrapping milestone

**Bootstrapping ships in Phase 1 (local) + Phase 3 (networked).**

Full design in `BOOTSTRAP.md`.

**Phase 1 — "Remove `.dark` files from the repo."** Local-only;
no sharing dependency. ~2-3 weeks of focused work. Lands work-
units bootstrap-1 through bootstrap-7. End state:
`packages/*.dark` is gone, the runtime path never invokes
LibParser, new installs do schema bootstrap + open the bundled
seed (carried alongside the binary). **Already half-built** —
`LibDB.Seed.export` + `LibDB.Seed.growIfNeeded` exist; the work
is decoupling them from the legacy `LoadPackagesFromDisk` runtime
path.

**Phase 3 — "Install Dark over the network."** Lands bootstrap-8
+ bootstrap-9. Depends on the sharing wire protocol (T7-T10).
End state: `dark install` fetches the seed from
matter.darklang.com; upgrades use the event stream rather than
fresh seeds.

Open decisions affecting bootstrap (carried below): snapshot file
naming (Q-bs-1), derived-data-in-seed yes/no (Q-bs-2),
versioning binding (Q-bs-3), signing (Q-bs-4, deferred to v2),
test-file fate (Q-bs-5), and fate of removed `.dark` files
(Q-bs-6, lock before bootstrap-7).

---

## Stability + sharing milestone

**Stability + sharing ships in Phase 3.** This is the user's
primary milestone.

Full design in `STABILITY-AND-SHARING.md`.

Lands work-units **share-1 through share-10**:

- share-1 — identity binding (Tailscale login ↔ account_id);
  technically belongs to Phase 2 alongside identity work
- share-2 + share-3 — GET /sync/snapshot + GET /sync/events
  (localhost-first, no auth)
- share-4 — Tailscale identity-binding via header injection
- share-5 — POST /sync/events with idempotent apply
- share-6 — autosync cron (Dark-side)
- share-7 + share-8 — approval-request ops + approve CLI
- share-9 — deploy matter.darklang.com (the central hosted instance)
- **share-10 — first 2nd-user onboarding** — the goal-line; MVP-
  cohabitation is roughly this.

Phase-4 deferred: share-11 (WebSocket live-push), share-12 (public
funnel), multi-peer p2p sync topologies.

The wire protocol leans on Tailscale (per the vault Tailscale.md
doc) for peer addressing + TLS + identity + ACL. Substrate doesn't
*require* Tailscale; it's the convenient default. **Network stack
isn't being built from scratch.**

Sharing depends on:
- Bootstrap Phase 1 (so the seed exists to share)
- Identity (T11-T12) for the account-id binding
- Conflict-resolution dispatch (T14) so arriving ops have a place
  to route disagreements

---

## MVP-cohabitation demo

The smallest end-to-end demo proving the substrate works. Filled
by T23.

### The scenario

**Two humans, two machines, one shared substrate, one optional
agent.**

- **Stachu** at his desktop (`major`).
- **Ocean** at her laptop.
- Both on the same Tailscale tailnet.
- Both have Dark installed via the seed (per Bootstrap Phase 1).
- Both linked their accounts via `dark link --tailscale`.
- `matter.darklang.com` is up, hosting the canonical snapshot +
  sync stream.

### What happens (the demo script)

```
t=0  Stachu edits a fn in his namespace (User.Stachu.Util.format).
     Commits. The commit is a normal package_op.

t=2  Ocean's autosync polls matter; sees the new commit; applies
     it. Her viewer shows "User.Stachu.Util.format updated by
     stachu, 2s ago."

t=5  Ocean tries to edit Stachu's fn (cross-namespace).
     Substrate emits an ApprovalRequest op instead of applying
     directly. The op syncs to Stachu.

t=8  Stachu's viewer surfaces the approval request. He reviews
     side-by-side, accepts.

t=10 ApprovalDecided op syncs back; Ocean's edit lands in
     Stachu's namespace.

t=15 (Optional, requires CapInvokeLLM granted by Stachu)
     Stachu spawns a csv-helper agent:
       dark agent spawn --caps CapInvokeLLM,CapReadFile \
                        --scope "User.Stachu.CSV.*"
     The delegation syncs to Ocean's instance.

t=18 Stachu does:
       dark on major run csv-helper.process inputs/big.csv
     (Pretend his desktop has the GPU.) Tailscale auth carries
     identity; major recognizes csv-helper; runs the call.
     Result streams back.

t=25 Ocean curious: `dark devices`. Sees Stachu's machines + her
     own + the running csv-helper agent with CapRemoteObserveAgents
     scoped to her (default for same-tailnet-paired-users).

t=30 Stachu revokes the agent. `dark agent revoke csv-helper`.
     Delegation revokes; sync propagates; Ocean's view updates.
```

### What this demo exercises

- **Bootstrap Phase 1** — both users running snapshot-based
  installs, no `.dark` parse on startup
- **Sharing (Phase 3 share-1 through share-10)** — Ocean's edit
  + the cross-namespace approval round-trip
- **Identity (Phase 2a + 2b)** — Tailscale-bound accounts,
  per-account scopes, agent kind
- **Capabilities (Phase 2a)** — CapInvokeLLM as the AI-opt-in
  gate; cross-peer caps for observation
- **Conflicts dispatch (Phase 2a)** — cross-namespace op produces
  an ApprovalRequest, surfaces to owner
- **Agent runtime + remote access (Phase 2b + Phase 3 + T22b)**
  — agent spawn + `dark on major run X` + delegation propagation
- **Event bus (Phase 3 P3-1+)** — viewer subscribes to
  materialization + bodyChanged + agent events
- **Hot-reload (Phase 4)** — Ocean's running session picks up
  Stachu's edit live (if she has any in-flight code referencing it)
- **AI-opt-in constraint** — the agent step is *optional*; the
  human-only part of the demo works without any LLM grant

### Acceptance criteria

A successful MVP-cohabitation demo:

- (a) Stachu's commit reaches Ocean's instance within 5 seconds
- (b) Ocean's cross-namespace edit produces an approval request,
  not a silent overwrite or hard fail
- (c) The agent (if spawned) runs scoped + caps work; remote
  execution via `dark on major run ...` lands the call with
  the agent's identity
- (d) Revocation propagates; Ocean's view reflects the change
  within sync latency
- (e) Tests pass throughout

### Why this is "MVP"

This is the smallest demo that proves the substrate's value
prop. It exercises the load-bearing primitives end-to-end.
**Does not require**: PDD materialization, the in-progress fn
viewer (`VIEW-SKETCHES`), public funnel, multi-peer p2p, or
hot-reload (those are Phase 4+).

A user seeing this demo should understand: "Darklang is where my
team works together on the same evolving code, with or without
agents, locally-first."

### Demo dependencies (what must be done first)

| Required | Source |
|---|---|
| Phase 1 complete (bootstrap) | BOOTSTRAP P1-1 to P1-6 |
| Phase 2a complete (humans + dispatch) | MIGRATION P2a-1 to P2a-10 |
| Phase 3 P3-1 to P3-11 | MIGRATION |
| Tailscale tailnet set up | external dep |
| matter.darklang.com hosted | MIGRATION P3-9 |
| (Optional) Phase 2b for the agent step | MIGRATION P2b-1 to P2b-5 |

P3-11 *is* the demo. Hitting that chunk is the goal-line.

---



---

## Open decisions

*Accumulates as TODOs surface decisions worth flagging. Final
pass in T27.*

### From T14 deepening + errors-as-conflicts framing

- **Q-cr-1** Parse-time errors: ParsePolicy struct (option a) vs
  uniform "syntactic Pending" placeholders (option b). (b) is
  more uniform; (a) less invasive. Lean (b) but worth a real
  call.
- **Q-cr-2** Which existing `raiseRTE` sites are
  configurable-conflicts vs "internal-invariant" must-fail. ~35
  + ~26 sites to triage.
- **Q-cr-3** Dispatch latency on the hot path. Measure on tight
  arithmetic loops; gate strict-mode short-circuit if needed.

### From T11-T13 (identity)

- **Q-id-1** Token format. Random opaque + revocation table
  (recommended) vs JWT-shaped. JWT is overkill.
- **Q-id-2** Multiple agents per delegation. Probably no —
  1-to-1; spawn N for N parallel workers.
- **Q-id-3** Cross-instance agent identity. Verified via
  delegation chain content-addressed + signed by owner.
- **Q-id-4** Ad-hoc LLM use without an agent. Probably attributed
  to human with "via LLM" tag, not a separate identity.
- **Q-id-5** Long-running agents on matter.darklang.com. Owner-
  offline-pause-on-cap-denial story needed.
- **Q-id-6** Identity for the substrate itself. The Darklang
  seeded account (uuid 0...001) serves this role. Confirm.

### From T7-T10 (stability + sharing)

- **Q-ss-1** Default sync target. Auto-sync to
  matter.darklang.com vs opt-in. Probably opt-in (git-style
  `remote add`).
- **Q-ss-2** Cross-namespace ops auto-approval-request vs
  fail-loudly. Per 2025-11-12: approval request. Confirm.
- **Q-ss-3** Sync granularity (per-branch, per-namespace,
  always-all). Likely per-branch with namespace filter.
- **Q-ss-4** Public funnel vs tailnet-only. Phase 4 decision.
- **Q-ss-5** Conflict resolution latency on arrival (immediate
  dispatch vs batched-on-next-interaction).
- **Q-ss-6** WIP cross-instance sync (local-by-default vs
  always-share). Hybrid: local default + `share-wip` opt-in.

### From T3-T6 (bootstrap)

- **Q-bs-1** Snapshot file naming (`seed.db` vs `data.db` vs
  `dark.db`). Probably keep both names with the export/runtime
  distinction.
- **Q-bs-2** Ship derived data in the seed, or regrow on import.
  Default to current behavior (strip derived, regrow); measure;
  flip if slow.
- **Q-bs-3** Version-binding between binary and seed. Embed
  expected seed-hash in binary?
- **Q-bs-4** Sign seeds? Deferred to v2.
- **Q-bs-5** Test-file `.dark` files stay (test inputs, not
  package source). Confirm.
- **Q-bs-6** Fate of `packages/*.dark` post-removal — archive
  repo? Tagged release? Documentation reference?

---

## Risks

*Filled in by T26.*

---

## What comes after this loop

*Filled in by T29.*
