# Loop status

One-line-per-pass status of the overnight refactor. Newest at top.

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
