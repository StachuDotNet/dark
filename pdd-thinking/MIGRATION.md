# Migration — Current State → Substrate State

> Loop T21 (2026-05-20). The ordered shippable-chunk transition
> from today's main to the substrate per ROADMAP. Includes
> rollback notes per chunk.

Starting state: `main` at commit `61cb32ac7` (Merge PR #5649,
"misc-bwdserver-aot-etc-squashed"). Per the substrate sketches,
main is much further along than the spike-era docs implied —
about half the substrate is real.

Ending state: ROADMAP's full chunk list (C1-C16) at substrate-
grade, with bootstrapping shipped (Phase 1+3) and stability+
sharing shipped (Phase 3).

This doc is the **ordered list of feature-shippable chunks** to
walk from one to the other. No big-bang rewrites; each chunk is
a PR or small PR series that ships independently and can be
reverted if it breaks.

## Migration principles

- **Always shippable.** After every chunk, `dark` runs. No
  half-merged interpreter states.
- **Tests at each step.** Per-chunk: existing tests pass + new
  tests cover new behavior.
- **Backward-compat where possible.** Default behavior matches
  today; new behavior opts in.
- **Reality-grounded.** Each chunk lists "what existed before"
  + "what changes." Per the reality-grounding rule.
- **Rollback notes.** Per chunk: "if this breaks production,
  revert by..."
- **No big-bang rewrites.** Even if a subsystem ends up rewritten,
  it's in 5+ smaller PRs each.

## Phase-aligned plan

The chunks group into the phases from ROADMAP §"Phase plan"
(filled by T24). At this point in the loop, phase numbers are
provisional:

- **Phase 0** — readiness (audit, plumbing)
- **Phase 1** — bootstrap (remove `.dark` files)
- **Phase 2** — identity + capabilities + conflicts dispatch
- **Phase 3** — sharing + remote access + viewer
- **Phase 4** — agent-runtime, public funnel, multi-peer p2p

Per BOOTSTRAP and STABILITY-AND-SHARING and IDENTITY, this
phasing is locked. T24 will finalize.

## Phase 0 — readiness

Quick local cleanups before the substrate work. Each is small;
all can ship within a week.

| Chunk | What | Reversibility |
|---|---|---|
| **P0-1** Audit `LoadPackagesFromDisk` callers | Confirm only `LocalExec.fs` + `TestModule.fs` call it. Document in BOOTSTRAP. | Trivial; doc-only |
| **P0-2** Verify `LibDB.Seed.export/grow` round-trips | Export, copy, spin a fresh instance from the copy, assert all package_* tables match. Add a CI test. | Test addition only |
| **P0-3** Inventory `raiseRTE` call sites | Tag each ~60 site as configurable / not-configurable per CONFLICTS errors-as-conflicts framing. Add a comment per site noting the future Conflict variant. | Comment-only |
| **P0-4** Inventory builtin assemblies' default caps | Add a doc-comment to each `Builtins.*` assembly stating its default cap-set per CAPABILITIES per-assembly table. Set up for the field add in P2. | Comment-only |

## Phase 1 — Bootstrap (remove `.dark` files)

Per BOOTSTRAP.md sequencing. ~2-3 weeks focused. No identity /
sharing / cap dependency.

| Chunk | What | Reversibility |
|---|---|---|
| **P1-1** Separate "build-seed" CLI mode | Add `dark build-seed --output seed.db` that runs `LoadPackagesFromDisk` + applies ops + calls `LibDB.Seed.export`. End-user runtime no longer needs LoadPackagesFromDisk. | Easy revert; new code path doesn't change existing |
| **P1-2** CI builds seed.db | Build artifact = `seed.db` + binary. Both ship together as release-asset (later: matter.darklang.com). | CI-only change |
| **P1-3** First-run install detection | If data dir empty, copy bundled seed; else open existing. | Add a code path; existing path untouched |
| **P1-4** Relocate `LoadPackagesFromDisk` to `LibBuildTools` | Move out of `LocalExec` to a separate `backend/src/LibBuildTools/`. Only linked in build-seed mode. | Module reorganization; reversible |
| **P1-5** Delete `packages/*.dark` | The big one. Move source-of-truth to seed file. Tag a release before deletion. Archive in a separate repo? | Lock decision Q-bs-6 first. Hardest to revert; would re-add the files from tagged release. |
| **P1-6** Refactor LibParser to be edit-time only | LibParser stays compiled into the binary (the editor uses it); just not invoked during bootstrap. | Surgical; bootstrap test verifies. |

**Outcome after Phase 1:** repo is much smaller; CI is faster;
install is snapshot-based; `dark` runs fully without parsing any
`.dark` source files. The user's first itch is scratched.

## Phase 2 — Identity + capabilities + conflicts dispatch

The bridging chunk. Per IDENTITY, CAPABILITIES, CONFLICTS. Lands
~4-6 weeks. Split into 2a (humans) + 2b (agents) per IDENTITY's
phasing.

### Phase 2a — humans, conflict dispatch foundation

| Chunk | What | Reversibility |
|---|---|---|
| **P2a-1** Add `account_identities` table + Tailscale binding | Per IDENTITY. `dark link --tailscale` CLI cmd. | Add table; rollback drops |
| **P2a-2** Add `Conflict` + `Resolution` types + `ConflictDispatch` field on ExecutionState | Default dispatch returns `FailLoudly` for every variant → no behavior change. | Field add; default preserves behavior |
| **P2a-3** Add `conflicts_v0` + `conflict_resolutions_v0` tables | Audit infra. Empty until used. | Add tables |
| **P2a-4** Migrate `LibDB/Rebase.fs.getConflicts` to emit `Conflict.OpVsOp` | Existing `RebaseConflict` becomes the payload of the new Conflict variant. SCM op-vs-op flows through dispatch. | Wrap-in-place; existing API can stay until callers updated |
| **P2a-5** Add `Capability` enum + `capabilities` field to `BuiltInFn` | Empty set per builtin (backward-compat: no caps required = always permitted). Per-assembly helpers set defaults gradually. | Field add; default empty |
| **P2a-6** Add `capability_grants_v0` + `capability_log_v0` tables | Empty until used. | Add tables |
| **P2a-7** Wire cap-check into Apply for builtin calls | Per CAPABILITIES F# integration sketch. Strict-mode default = `FailLoudly`. Fast-path: empty `required` skips the check. | Inline check; reversible |
| **P2a-8** Annotate `Builtins.Pure` with `{CapPure}` | First per-assembly retrofit. Pure builtins still always run (no cap requirement actually checked yet). Sets the pattern. | Annotation only |
| **P2a-9** Annotate `Builtins.Http.Client` + `Builtins.CliHost` with their caps | Real cap requirements. **Granting `CapReadNet`/etc. now required** for these to run. **Behavior change** — fix tests + add grants. | Set the defaults grants table to include these caps so existing CI passes |
| **P2a-10** Install-time grant UX | First-run interactive prompt per CAPABILITIES install flow. | New UX; pre-existing installs keep their cap profile |

**Outcome after Phase 2a:** Identity + cap-check + conflict
dispatch all wired. Existing behavior preserved by defaults.
Foundation for sharing (Phase 3) and agents (Phase 2b).

### Phase 2b — agents + delegations + LLM gating

| Chunk | What | Reversibility |
|---|---|---|
| **P2b-1** Add `kind` column to `accounts_v0` + `delegations` table + `package_ops.delegation_id` | Schema additions only. | Add column + table |
| **P2b-2** `dark agent spawn / list / revoke` CLI commands | Dark-side per IDENTITY. | Pure addition |
| **P2b-3** Wire agent cap-check (triple intersection) at builtin Apply | Per IDENTITY delegation contract. | Inline check; reversible |
| **P2b-4** Add `CapInvokeLLM` + `CapSendSecret` as AI-opt-in gatekeepers | Denied by default. LLM builtins (in `Builtins.Matter`) declare these caps. | Adding caps to existing builtins flips them off until granted — by design, AI-opt-in |
| **P2b-5** Migrate `raiseRTE Ints.DivideByZeroError` (first errors-as-conflicts site) | Per CONFLICTS errors-as-conflicts. One site as a pattern; others follow. | Per-site; default `FailLoudly` preserves behavior |

**Outcome after Phase 2b:** agents are first-class but opt-in.
PDD-style work becomes possible (still gated by `CapInvokeLLM`).
Errors-as-conflicts pattern established.

## Phase 3 — Sharing + remote access + viewer

The user's headline milestone. Per STABILITY-AND-SHARING.

| Chunk | What | Reversibility |
|---|---|---|
| **P3-1** EventBus<T> primitive in F# | Per EVENT-STREAMS-AND-PARKING. Empty buses + scheduler. No subscribers yet. | Add module; nothing uses it |
| **P3-2** Subscribe `conflicts_v0` + `capability_log_v0` to their persistence-bus | Per EVENT-STREAMS persistence table. | Reversible; switches subscriber |
| **P3-3** GET /sync/snapshot + /sync/snapshot/hash (Dark HTTP handler) | Per STABILITY-AND-SHARING share-2. Localhost only. | New endpoint; doesn't change existing |
| **P3-4** GET /sync/events with branch + sequence filter | Per share-3. | New endpoint |
| **P3-5** GET /sync/whoami + Tailscale header parsing | Per share-4. | New endpoint |
| **P3-6** POST /sync/events with idempotent apply | Per share-5. Inbound events route through PackageOpPlayback + conflict dispatch. | New endpoint; reversible |
| **P3-7** Autosync cron (Dark-side background loop) | Per share-6. Per 2025-11-12: user toggles autosync. | New Dark cron; toggle |
| **P3-8** `ApprovalRequest` + `ApprovalDecided` PackageOp variants + `dark approve` CLI | Per share-7+8. | Adds variants + CLI |
| **P3-9** Deploy matter.darklang.com (Tailscale-served Dark instance) | Per share-9. Initial Stdlib snapshot. Tailnet-members only initially. | Tear-down = delete the GCP/box |
| **P3-10** Bootstrap-from-network: `dark install` fetches seed from matter.darklang.com | Per BOOTSTRAP bootstrap-8. | New flow; bundled-seed fallback |
| **P3-11** Onboard Ocean / Feriel as second-user. Verify Stachu's commits flow live. | The MVP-cohabitation goal-line (T23). | Test scenario |
| **P3-12** PDD viewer app (MVU app subscribing to EventBuses) | Per VIEW-SKETCHES, behind `CapInvokeLLM` to keep AI-opt-in honored | New Dark app |
| **P3-13** REMOTE-ACCESS doc as design ref + Tailscale-based peer reach docs | Per T22b. CLI command for "exec on peer" with explicit cap-grant flow | Documentation + UX work |

**Outcome after Phase 3:** stability+sharing real. Two users on
two machines sharing a substrate. matter.darklang.com hosts. The
user's primary milestone hit.

## Phase 4 — Agent runtime, public funnel, multi-peer p2p

The frontier. Per FRONTIER's items + per T22 agent runtime.

| Chunk | What | Reversibility |
|---|---|---|
| **P4-1** Agent runtime: spawn-as-thread / observe / cancel | Per T22 (forthcoming). Dark-side mostly. | Add framework |
| **P4-2** Hot-reload via BodyChanged event subscription + mid-execution policy | Per HOT-RELOAD. FinishThenUpdate default. | Wire subscriber |
| **P4-3** Sync WebSocket live-push channel | Per share-11. Falls back to polling. | New transport |
| **P4-4** matter.darklang.com public funnel | Per share-12. Rate-limited. Deliberate decision. | Toggle off |
| **P4-5** Multi-peer p2p sync (Tailscale-direct between peers) | Per STABILITY-AND-SHARING topology. | Additional sync target; existing central sync stays |
| **P4-6** Migrate the rest of `raiseRTE` sites to conflict dispatch | Per CONFLICTS errors-as-conflicts. ~55 more sites. | Per-site |
| **P4-7** PDD materializer as Darklang fn (Stdlib.PDD.materialize) | Per FRONTIER's "materializer in Dark." Bounded by `CapInvokeLLM`. | Big refactor; can phase-incrementally |

## Cross-cutting concerns to maintain across all phases

- **AI-opt-in constraint**: every new feature in any phase that
  touches LLMs requires `CapInvokeLLM`. Denied by default.
- **Local-first**: every feature has a non-network path.
- **Reversibility**: tagged release before any irreversible
  change.
- **Tests**: per-chunk acceptance tests added; existing test
  suite always green.
- **Schema kill-and-fill**: new tables added by editing
  `schema.sql`; hash bumps; replay runs.
- **`main` reality grounding**: each chunk references a specific
  existing main artifact it modifies + each PR documents the
  diff scale.

## Open decisions

- **(Q-mig-1) Phase 0 readiness ordering with Phase 1 bootstrap.**
  Can P0-1 to P0-4 run in parallel to P1? Yes; they're audits +
  doc-comments. Don't block Phase 1 on them.
- **(Q-mig-2) Phase 2 timing pressure.** 2a + 2b is ~4-6 weeks.
  Real?  Or split further?
- **(Q-mig-3) Phase 3 deployment of matter.darklang.com.**
  Self-hosted on Stachu's tailnet (cheap) vs GCP (more
  professional). Probably tailnet first.
- **(Q-mig-4) Phase 4 ordering.** Within Phase 4, what's most
  load-bearing?  Probably hot-reload (P4-2) since it unlocks
  live-development across all the substrate.
- **(Q-mig-5) Where does PDD-spike code go?** Per WRAP-UP it's
  archived. Phase 4 (P4-7) is when the materializer-in-Dark
  shape lands; the spike branch stays a historical artifact.
- **(Q-mig-6) Beyond Phase 4** — when does the substrate feel
  "complete"? Probably never; just stable. ROADMAP §"What comes
  after this loop" addresses (T29).
