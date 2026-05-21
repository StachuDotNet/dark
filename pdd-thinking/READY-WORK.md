# Ready Work — Comprehensive List + DAG

Items that are **ready to be worked on** — design exists in the
substrate docs; no big strategic call blocking; an engineer can
pick one up, scope a 1d-2wk PR, ship.

Compiled by re-reading every substrate doc in this directory.
Items come from: `BOOTSTRAP.md` (10 bootstrap-N chunks),
`STABILITY-AND-SHARING.md` (12 share-N chunks), `REMOTE-ACCESS.md`
(7 RA-N chunks), `MIGRATION.md` (P0-/P1-/P2a-/P2b-/P3- chunks,
~30 across phases), and the deepened substrate sketches' inline
"sequencing" notes.

Total: **40+ chunks** identified as ready. Grouped into 9 themes
below, with a DAG showing dependencies.

---

## Theme A — Audits + micro-PRs (truly leaf, design-trivial)

Each is half-day to one day. No real dependencies. Could be the
first thing committed.

| ID | Chunk | Source |
|---|---|---|
| **A1** | Audit `LoadPackagesFromDisk` callers (confirm only `LocalExec.fs` + `TestModule.fs`) | P0-1 / bootstrap-1 |
| **A2** | `LibDB.Seed.export/grow` round-trip CI test | P0-2 / bootstrap-2 |
| **A3** | Inventory `raiseRTE` sites with per-site doc-comment ("future Conflict variant") | P0-3 |
| **A4** | Inventory builtin assemblies' default caps with doc-comments | P0-4 |

---

## Theme B — Bootstrap track (`.dark` files gone)

6 sequential chunks. Local-only; no network/identity/caps
dependency. ~2-3 weeks total.

| ID | Chunk | Depends on |
|---|---|---|
| **B1** | `dark build-seed --output seed.db` CLI mode | A1 (audit) |
| **B2** | CI builds `seed.db` as release artifact | B1 |
| **B3** | First-run install detection (copy bundled seed if empty) | B2 |
| **B4** | Relocate `LoadPackagesFromDisk` → `LibBuildTools` project | B1 |
| **B5** | Delete `packages/*.dark` (tag release first) | B3 + B4 |
| **B6** | LibParser becomes edit-time only | B5 |

Source: BOOTSTRAP.md bootstrap-3 → -7, MIGRATION P1-1 → P1-6.

---

## Theme T — Tailscale + network demos

Network track. Starts with Tailscale builtins, ends with two
Dark instances talking.

| ID | Chunk | Depends on |
|---|---|---|
| **T1** | `Builtins.Tailscale` package: `status --json`, `serve` shell-out, header-parsing helpers | none |
| **T2** | **Ping/pong demo**: laptop A serves `/ping`, laptop B calls it via `https://A.tailnet/ping` | T1 |
| **T3** | `/devices` HTTP handler + `dark devices` CLI | T1 |

Source: REMOTE-ACCESS.md RA-1, RA-2; SELF-SYNC.md spike S1.

---

## Theme I — Identity foundation

Single-user multi-device first; multi-user later.

| ID | Chunk | Depends on |
|---|---|---|
| **I1** | `account_identities` table + Tailscale-login binding | T1 (uses Tailscale builtins) |
| **I2** | `dark link --tailscale` CLI command | I1 |

Source: IDENTITY.md, MIGRATION P2a-1, share-1.

---

## Theme C — Capabilities track

Per-assembly retrofit. Default = empty cap-set = no behavior
change. Real caps gated on later annotations.

| ID | Chunk | Depends on |
|---|---|---|
| **C1** | `capabilities` field on `BuiltInFn` + per-assembly defaults | none |
| **C2** | `capability_grants_v0` + `capability_log_v0` tables | none (schema-only) |
| **C3** | Cap-check inline at `Apply` for builtin calls | C1 + O1 (dispatch field, see below) |
| **C4** | Annotate `Builtins.Pure` with `{CapPure}` | C1 |
| **C5** | Annotate `Builtins.Http.Client` + `Builtins.CliHost` with their caps | C1 + grants table seeded |
| **C6** | Install-time grant UX prompt | C2 |

Source: CAPABILITIES.md, MIGRATION P2a-5 → P2a-10.

---

## Theme O — Ops, conflicts, resolutions

The unified dispatch primitive. Multiple SCM/runtime sources
collapse into it.

| ID | Chunk | Depends on |
|---|---|---|
| **O1** | `Conflict` + `Resolution` sum types + `ConflictDispatch` field on `ExecutionState` (default `FailLoudly`) | none |
| **O2** | `conflicts_v0` + `conflict_resolutions_v0` tables | none |
| **O3** | Migrate `Rebase.getConflicts` → emit `Conflict.OpVsOp` | O1 + O2 |
| **O4** | First `raiseRTE` migration (`Ints.DivideByZeroError`) → dispatch shim | O1 + O2 (+ E2 if Park outcome used) |
| **O5** | Migrate next 5-10 `raiseRTE` sites | O4 (pattern established) |

Source: CONFLICTS-AND-RESOLUTIONS.md, MIGRATION P2a-2 → P2a-4 + P2b-5.

---

## Theme E — Event streams, parking, trace resuming

The substrate's coordination primitive. Required for sync, hot-
reload, agents, and the "wait for human" flow.

| ID | Chunk | Depends on |
|---|---|---|
| **E1** | `EventBus<T>` F# primitive (publish / subscribe / waitForOne) | none |
| **E2** | Scheduler + parked-frame wait-list + `EventSelector` sum | E1 |
| **E3** | `Promise<T>` Dval + `!` compile-target builtin | E1 + E2 |
| **E4** | Trace resuming for long-pause cases (durable parked-frame table; restore on restart) | E2 + existing Tracing.fs |

Source: EVENT-STREAMS-AND-PARKING.md, MIGRATION P3-1 + new
sketches for E4.

**E4 is the biggest unknown** — the user flagged "awaiting might
take a very long time, dependent on a manual resolution."
Specifically: a parked frame awaiting human input may wait
hours or days; the substrate needs durable parking that survives
restart. Likely wants its own spike before serious work.

---

## Theme S — Sync wire (after I + T)

Dark HTTP handlers on top of existing `Builtins.Http.Server`.

| ID | Chunk | Depends on |
|---|---|---|
| **S1** | `GET /sync/snapshot` + `/snapshot/hash` | none (localhost initially) |
| **S2** | `GET /sync/events?since=...&branch=...` | none (localhost) |
| **S3** | `GET /sync/whoami` + Tailscale header → account_id | I1 |
| **S4** | `POST /sync/events` with idempotent apply | S2 + existing `PackageOpPlayback` |
| **S5** | Autosync cron (Dark-side background loop) | S2 + S4 |

Source: STABILITY-AND-SHARING.md share-2 → share-6, MIGRATION P3-3 → P3-7.

---

## Theme M — Multi-user (later; sketched for completeness)

| ID | Chunk | Depends on |
|---|---|---|
| **M1** | `ApprovalRequest` + `ApprovalDecided` PackageOp variants | O1 |
| **M2** | `dark approve` CLI command | M1 |
| **M3** | Deploy `matter.darklang.com` (Tailscale-served Dark instance) | S1-S4 + I1 |
| **M4** | Bootstrap-from-network: `dark install` fetches seed from matter | B5 + M3 |

Source: share-7, share-8, share-9, share-10, MIGRATION P3-8 → P3-10.

---

## Theme R — Remote execution (after T + S)

| ID | Chunk | Depends on |
|---|---|---|
| **R1** | `POST /exec` Dark HTTP handler + `dark on <peer> <cmd>` CLI | T1 + I1 + C3 (cap-gated `CapRemoteExec`) |
| **R2** | Offline-queue support (op with `target_peer`) | S4 + S5 |
| **R3** | Agent runtime as long-running thread (refactor `LLM.Agent.run`) | E2 + existing `llm/agent.dark` |
| **R4** | `dark on <peer> agent ...` to control remote agents | R1 + R3 + I1 (cross-instance identity) |

Source: REMOTE-ACCESS.md RA-3 → RA-7.

---

## The DAG

The most-load-bearing reading. Items in `[brackets]` are
*spike-recommended-first* (E4 specifically).

```
LAYER 0 (start day 1 — leaf nodes, no upstream deps)
═════════════════════════════════════════════════════════════════
  A1   A2   A3   A4    ← audits / CI test / inventories (1d each)
  T1                   ← Tailscale builtins
  O1   O2              ← Conflict types + tables
  C1   C2              ← Cap field + grant tables
  E1                   ← EventBus<T> F# primitive
  S1   S2              ← /sync/snapshot + /sync/events (localhost)

      ↓             ↓                ↓               ↓
══════════════════════════════════════════════════════════════════
LAYER 1
══════════════════════════════════════════════════════════════════
              ┌──── T1 ────┬───── T1 ───┐
              ▼            ▼            ▼
             T2           I1           T3
          ping/pong    account_   /devices+
          (1ST NET    identities  dark devices
           MILESTONE)  +link
              │            │
              │            ▼
              │           I2
              │         dark link
              │         --tailscale
              │
   ┌── A1 ─── B1 ─── B2 ─── B3 ───┐
   │   audit  build-  CI    first-│
   │          seed    builds run  │
   │          CLI     seed   inst │
   │                              │
   └──────────── B4 ──────────────┤  (relocate loadPM)
                                  │
                                  ▼
                                 B5  delete packages/*.dark
                                  │
                                  ▼
                                 B6  LibParser edit-time-only
                                ↑
                          (B5 needs tagged release first)

  C1 ─┬─ C4    Builtins.Pure annotation
      ├─ C5    Http.Client + CliHost annotation
      │
      └─ + O1 ─→ C3  cap-check at Apply
                  │
                  └──────┐
                         ▼
                        C6  install-time grant UX
                          (depends on C2)

  O1 + O2 ─→ O3       Rebase.getConflicts → OpVsOp
                       (FIRST DISPATCH PROOF)
                       │
                       ▼
              ─→ O4   first raiseRTE migration
                       │  (also wants E2 for Park outcome)
                       ▼
                      O5    migrate next 5-10 sites

  E1 ─→ E2  scheduler+parking
        │
        ├──→ E3   Promise<T> + !
        │
        └──→ E4   trace resuming    [SPIKE FIRST — biggest unknown,
                  long-pause case    serves human-await flow]

  I1 ─→ S3   /sync/whoami
  S2 ─→ S4   POST /sync/events
  S2 + S4 ─→ S5   autosync cron

══════════════════════════════════════════════════════════════════
LAYER 2 (mostly higher-order, downstream of multiple items)
══════════════════════════════════════════════════════════════════

  S1+S2+S4 + I1 + B5 ─→ M3   deploy matter.darklang.com
                              │
                              ▼
                             M4    bootstrap-from-network

  O1 ─→ M1   ApprovalRequest op variants
         ↓
         M2   dark approve CLI

  T1 + I1 + C3 ─→ R1   POST /exec + dark on <peer>
                       │
                       ▼
                      R2   offline-queue support
                            (needs S4 + S5)

  E2 + existing llm/agent.dark ─→ R3   agent runtime as thread
                                       │
                                       ▼
                                      R4   dark on <peer> agent
                                            (needs R1 + R3 + I1)
```

**Layer 0 leaves** (can start in parallel, day 1):
`A1 A2 A3 A4 T1 O1 O2 C1 C2 E1 S1 S2` = **12 starter nodes**.

**Highest-leverage chunks** (unblock the most downstream work):
- **T1** — unblocks T2 (first net milestone) + I1 + R1 + many sync chunks
- **O1** — unblocks O3, O4, M1, C3 (all the dispatch flows)
- **E1** — unblocks E2 → E3 + E4 + R3 (all parking/agent work)
- **B1 (build-seed CLI)** — unblocks the whole bootstrap track

**Milestones** (rough chronological landing points):

1. **First network milestone** — `T2` ping/pong working
2. **First bootstrap milestone** — `B5` `.dark` files gone
3. **First dispatch proof** — `O3` Rebase → Conflict.OpVsOp routing through new dispatch
4. **First self-sync milestone** — `S4` + `S5` autosync between two of your laptops working
5. **Cohabitation milestone** — `M3` matter.darklang.com hosted, `R3` agents as threads

---

## Suggested orderings

### Solo dev, all chunks (~10-12 weeks)

```
Wk 1   A1 A2 A3 A4 + T1 + O1 + O2          (leaf nodes, get multiple started)
Wk 2   T2 + B1 + E1 + I1                   (first net milestone in week 2)
Wk 3   B2 + B3 + C1 + E2                   (bootstrap CI/install + scheduler)
Wk 4   B4 + C2 + S1 + S2                   (sync read-side; cap tables)
Wk 5   B5 + O3                              (delete .dark; first dispatch proof)
Wk 6   B6 + S3 + S4                         (LibParser edit-only; sync write)
Wk 7   S5 + I2 + R1                         (autosync working; remote exec)
Wk 8   C3 + C4 + C5 + C6                    (cap-check + annotations + grant UX)
Wk 9   O4 + E3                              (first error migration; Dark await)
Wk 10  E4 spike + R3                        (long-pause + agent threads)
Wk 11  M1 + M2 + M3                         (approval flow + matter deploy)
Wk 12  M4 + R2 + R4 + O5                    (full network + remaining migrations)
```

### Solo dev, **self-sync only** (~4-6 weeks)

Skip everything not on the self-sync critical path:

```
Wk 1   T1 + A2 + B1                         (Tailscale + CI test + build-seed)
Wk 2   T2 + B2 + B3                         (ping/pong + CI artifact + install)
Wk 3   B4 + B5 + S1                         (relocate loadPM + delete .dark)
Wk 4   S2 + S4 + I1                         (sync read+write + identity)
Wk 5   S3 + S5 + I2                         (whoami + autosync + dark link)
Wk 6   buffer + dogfooding                   (use it on Stachu's 2 laptops)
```

= **6 weeks**. Matches `SELF-SYNC.md`'s estimate.

### Two devs in parallel (~6-7 weeks)

- **Dev A — Network track**: T1, T2, T3, I1, I2, S1-S5, R1, R3, R4
- **Dev B — Substrate track**: A1-A4, B1-B6, O1-O5, C1-C6, E1-E4, M1-M2

Convergence at week ~5 when Dev B's O1 + Dev A's S4 both land,
making the dispatch-routes-sync story coherent.

---

## What's NOT ready (and why)

| Item | Why not ready |
|---|---|
| **Hot-reload subscriber** (BodyChanged → dependents) | Needs E1 + E2 first; sketch is design-light beyond that |
| **PDD viewer app** | VIEW-SKETCHES is a brief, not a design; needs MVU evolution work |
| **COMPOSABLE-MVU 6-step evolution** | Each step is its own sub-design (Msg type design, View tree, effects channel, ...) — not ready until designed |
| **PDD materializer as Dark fn** | Phase 4; needs all of Phase 2 + 3 substrate first |
| **F# → Dark interpreter migration** | Years-out goal; no design |
| **Public funnel for matter.darklang.com** | Needs rate-limiting + abuse model |
| **Multi-peer p2p sync** | Needs M3 + extra topology work |
| **PDD spike code merge** | Wrong-direction per user; reverts/cherry-picks needed |

These all have designs *sketched*, but not *ready*. Each needs
another design pass before an engineer should pick one up.

---

## Recommended starting move

If you're picking one chunk to start with today:

**`T2` (ping/pong)** — depends only on `T1` (1-week build). Once
working, you have visceral proof that the Tailscale shortcut
works end-to-end. **Single most-confidence-building chunk.**

Alternatively: **`A2` (LibDB.Seed round-trip CI test)** — half-
day, no deps, immediately useful. Catches bootstrap regressions
forever.

If two people: one does T1+T2 while the other does A1-A4 + B1.
By end of week 2, you have ping/pong working AND a clean
bootstrap track ready to shed `.dark` files.
