# Loop status

One-line-per-pass status of the overnight refactor. Newest at top.

- **Pass 13 (adversarial deepening):** Stated the sync **convergence precondition**. Replay-
  determinism (pass 11) covers an op's result, but two *instances* converge only if they fold with
  the same `apply`/`conflict`/`resolve` — which are editable, forkable App logic. Added a keystone
  section: since those functions are content-addressed package items in the op stream, same-logic =
  same-hashes IS the convergence guarantee, and a forked resolver surfaces as an ordinary
  `Name → two hashes` conflict on the App's own definition (deliberate, visible — never silent drift).
- **Pass 12 (adversarial deepening):** Completed the storage model. The pass-7 two-store split
  (`ops.db` synced + `projections.db` derived) had no home for **local-authoritative state that is
  neither synced nor derivable** — capability grants, sync-remote config + tailnet binding, local-WIP.
  Added the third store (`local.db` / `.darklang` config) to the keystone, reframed the WIP
  local-vs-sync open decision as concretely "which file," and reconciled it with capabilities.md
  (grants don't sync), sync.md (.darklang config), and conflicts.md (WIP). Another real gap an
  adversarial read surfaces.
- **Pass 11 (adversarial deepening):** Not churn — found and resolved a genuine cross-doc design
  tension. `conflicts.md`/`async.md` assert deterministic replay, but `event-bus.md` marks the
  materialization bus non-durable and `algorithm.md` has forever-lazy (nondeterministic) LLM bodies.
  Added a keystone section stating the reconciling rule — **an op records the result, not the intent
  to call**; replay folds recorded values and never re-invokes producers; the lone exception (an
  uncommitted forever-lazy body) IS the flagged trace-replay-divergence risk, mitigated by capturing
  model output in the trace. Cross-linked the rule from event-bus's persistence note and algorithm's
  forever-lazy section. This is the kind of real gap an adversarial read surfaces that link-checking can't.
- **Pass 10 (iteration) — FULL-TREE REVIEW COMPLETE / saturation reached:** Read and verified the
  last unseen content (hot-reload, cohabitation, remote-access; agent-workflow; the meta-reflections
  detail; `_cross-cutting-test-criteria.md`; `benchmark-targets.md`). All excellent, consistent, and
  feedback-addressing. **Every file in `outputs/` has now been reviewed.** Honest state: the wave has
  reached the diminishing-returns point its own `meta-reflections/where-the-loop-struggles.md`
  predicts — net-new value per pass is now near zero. The deliverable is complete and clean
  (0 broken links, 0 bare stale refs, 0 section-signs; all of feedback.md addressed or flagged-blocked).
  Remaining genuine work = the two deferred end-steps only: write `next-steps.md` (5i) and run the
  final print (section 11), both at ~08:00 / on the user's return. Subsequent passes are a holding
  pattern — light re-verification, not manufactured rewrites (per this tree's own "throughput is not
  progress" caution). The loop stays alive for the 08:00 end-steps; the user can `CronDelete` early.
- **Pass 9 (iteration):** Verified `identity.md` (all feedback addressed). Broad cross-link sweep
  across ALL of `outputs/` — clean. Produced `PRINT-LIST.md`: a curated ~40-file final-review set
  in reading order for the section-11 print, deliberately excluding the 125 mechanical project specs
  to avoid a paper flood. Nothing printed — held for ~08:00 once `next-steps.md` exists.
- **Pass 8 (iteration):** Verified `async.md`, `composable-mvu.md`, `structural-editor.md` thorough
  and consistent with the keystone `App` type. Caught link defects a `.md`-only grep can't see:
  a malformed `[composable-mvu.md]` (no target) and bare UPPERCASE prose refs (`VIEW-SKETCHES`,
  `COMPOSABLE-MVU`, `FRONTIER`) — all fixed. Added a parser/`compile` dependency note to the editor.
  design/ re-confirmed: 0 bare refs, 0 broken links, 0 section-signs.
- **Pass 7 (iteration):** Deepened the keystone's ops-vs-projections storage split — the one
  spot feedback.md explicitly said "please think on this." Replaced the "still open" note with a
  concrete clean split: two SQLite files (`ops.db` synced/canonical vs `projections.db`
  local/rebuildable, a *physical* boundary so derived data can't sync by accident); a projection
  declares fold-kinds/scope/invalidation; the distribution race (name→two-hashes) reframed as a
  non-problem (re-fold + conflict dispatch, never a distributed lock). Open part narrowed to
  cache tuning. Verified `bootstrap.md` (section 3) thorough.
- **Pass 6 (iteration):** PDD-name consistency + a stale-reference sweep the markdown
  link-checker had missed (backtick prose, not links). pdd.md title aligned to the canonical
  source spelling "Pseudocode-Driven Development" (flagged the possible "Prompt-Driven" rename
  given the ask-for-software framing — your call); repointed `CLAIMS.md`/`FRONTIER.md`/`TOC.md`
  and other UPPERCASE prose refs across pdd/claims/algorithm/view-sketches/beam-vs-dark to the
  kebab design names. design/ now has 0 stale refs, 0 broken links, 0 section-signs.
- **Pass 5 (iteration):** Verified `sync.md` thorough. Fixed a real cross-doc inconsistency:
  `ai-coding-target.md` described the bench `spec.md` with stale `title/tier/modules/languages`
  frontmatter — realigned to the `notes/projects/` shape (goal + acceptance-criteria +
  greenfield/brownfield; modules/language dropped), and reframed size tiers as sweep bookkeeping.
- **Pass 4 (iteration):** Verified `capabilities.md` (all 13 feedback bullets) and `identity.md`
  are thorough. Wrote `feedback-coverage.md` — a systematic map of every `source/feedback.md` point
  to its status + home. Net: all actionable points addressed; 2 genuinely blocked (HttpClient vault
  restriction notes, `feedback-from-agent.md` — neither locatable), vault-reorg is off-limits
  (recommendation written), final print held for section 11.
- **Pass 3 (iteration):** Critical re-review of the Stable & Syncing cluster against feedback.md —
  event-bus and conflicts verified thorough and fully addressing their feedback; project specs
  spot-checked (clean, concrete acceptance criteria). Filled the one real north-star GAP: wrote
  `design/apps-surface.md` (the `dark apps` install/fork/run surface, anchored on the print-md
  walkthrough end-to-end), cross-linked from the keystone, cli-daemon, and the design index. 0 broken links.
- **Pass 2 (iteration):** Cross-link audit — fixed the one broken link (composable-mvu -> hot-reload);
  0 broken `.md` links tree-wide. Migrated `dark-virtual-files.md` into `design/` (state-as-filesystem;
  ops-vs-projections framing; kept distinct from bootstrap). Deepened the keystone with the fuller `App`
  field-set table (msg/cmd/autoResolutions/constraints placed at their proper layers) and the
  hot-swappable / conflict-blind-core note. Workspace fully reorganized now (only READY-WORK + the
  coworker's review-only async plan remain).
- **Pass 1 (initial, long):** Built the whole structure and the bulk of the content in one
  extended session — `design/` (21 docs incl. the keystone distributed-event-sourcing,
  event-bus, sync, conflicts, async, capabilities, identity, bootstrap, cli-daemon,
  algorithm, claims, pdd, pdd-elevator-pitches, composable-mvu, view-sketches,
  structural-editor, package-system-layers, hot-reload, cohabitation, remote-access,
  research/{beam,swamp,visibility}-vs-dark), `projects/` (125 specs + index +
  cross-cutting test criteria), `issues-and-improvements/` (7 categories), `meta-reflections/`
  (process + loop-operations), `results/` (PDD bench convention + benchmark-targets),
  plus `README`s and `vault-organization.md`. FRONTIER distributed and deleted. All
  migrated source copies dissolved from the workspace.

## State of the checklist

DONE: sections 1 (structure), 2 (cross-cutting threads), 3 (bootstrap blockers),
4 (Stable & Syncing — all), 5c/5d/5e/5g/5h/5j, 6 (capabilities, identity),
7a (most), 8 (all three new docs), 9 (all), 10 (recommendation written).

REMAINING:
- **5i** — kill READY-WORK, write thin `next-steps.md`. Deferred to the END (per the plan).
- **section 11** — final `print-md` of everything touched. Deferred to ~08:00 (or when Stachu returns).
- **7a cross-link** — blocked: `feedback-from-agent.md` not found (flagged in vault-organization.md).
- **section 10 execution** — blocked: vault is off-limits; recommendation written for Stachu to run.
- **section 12** — all-night iteration/deepening (ongoing).

## Open / flagged for Stachu

- `vault/current-experiment/dark-virtual-files.md` — distinct concept (state-as-filesystem),
  no todo; to migrate into `design/` during iteration.
- `vault/current-experiment/Design/Dark Async Plan.md` — coworker's doc, review-only, kept as
  reference (informed `design/async.md`; not edited).
- Real product blocker surfaced: `dark serve` headless readiness flakiness — see
  `meta-reflections/loop-operations.md`.
- Still-to-locate: HttpClient restriction notes; `feedback-from-agent.md`.
