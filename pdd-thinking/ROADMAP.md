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

*Filled in by T6 (bootstrap phase) and T10 (sharing phase) and
T24 (overall phase ordering) and T25 (critical path).*

---

## Phase plan

*Filled in by T24.*

---

## Bootstrapping milestone

*Filled in by T6. Cross-links to BOOTSTRAP.md.*

---

## Stability + sharing milestone

*Filled in by T10. Cross-links to STABILITY-AND-SHARING.md.*

---

## MVP-cohabitation demo

*Filled in by T23.*

---

## Open decisions

*Accumulates as TODOs surface decisions worth flagging. Filled
across many iters; final pass in T27.*

---

## Risks

*Filled in by T26.*

---

## What comes after this loop

*Filled in by T29.*
