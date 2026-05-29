# Overwrite map — which output replaces which original

What to copy where when you promote this wave. The deliverable lives in
`notes/loop-work/outputs/`; the settled home is the repo **`notes/`** tree. So the
promotion is: **copy each `outputs/<theme>/` file to `notes/<theme>/`, then retire the
original** it supersedes (delete the repo `pdd-thinking/` copy, or archive the vault
copy). Filenames changed (UPPERCASE → kebab, several renamed), so this is
supersede-and-retire, not same-name in-place overwrite.

Origin legend: **[repo]** = `pdd-thinking/<NAME>` at the repo root (untouched by the
loop). **[vault]** = a file in the Obsidian vault (`~/vaults/Darklang Dev/…`).
**[new]** = net-new synthesis, no original to retire.

## stable-and-syncing/

| Output (`outputs/stable-and-syncing/…`) | Origin | Action on the original |
|---|---|---|
| `distributed-event-sourcing.md` | **[new]** (synthesizes the App-type notes + feedback) | none — place new |
| `event-bus.md` | **[repo]** `EVENT-STREAMS-AND-PARKING.md` | delete original |
| `conflicts.md` | **[repo]** `CONFLICTS-AND-RESOLUTIONS.md` | delete original |
| `sync.md` | **[repo]** `STABILITY-AND-SHARING.md` | delete original (its stable-def went to `pdd/algorithm.md`) |
| `capabilities.md` | **[repo]** `CAPABILITIES.md` | delete original |
| `identity.md` | **[repo]** `IDENTITY.md` | delete original |
| `hot-reload.md` | **[repo]** `HOT-RELOAD.md` | delete original |
| `cohabitation.md` | **[repo]** `COHABITATION.md` | delete original |
| `remote-access.md` | **[repo]** `REMOTE-ACCESS.md` | delete original |
| `composable-mvu.md` | **[repo]** `COMPOSABLE-MVU.md` | delete original |
| `cli-daemon.md` | **[vault]** `90.Stachu/even-newer/ai-devloop/improvements/cli-daemon.md` | archive vault copy |
| `package-system-layers.md` | **[vault]** `02.Project Management/Current Experiment/package-system-layers.md` | archive vault copy |
| `apps-surface.md` | **[new]** | none — place new |
| `example-app.md` | **[new]** | none — place new |
| `async.md` | **[new]** (informed by the coworker's async plan — see "do not touch" below) | none — place new |

## removing-dark-files/

| Output | Origin | Action |
|---|---|---|
| `bootstrap.md` | **[repo]** `BOOTSTRAP.md` | delete original |

## pdd/

| Output | Origin | Action |
|---|---|---|
| `pdd.md` | **[repo]** `README.md` (the PDD one) + deduped vs `TOC.md` | delete both originals |
| `algorithm.md` | **[repo]** `ALGORITHM.md` | delete original |
| `claims.md` | **[repo]** `CLAIMS.md` | delete original |
| `pdd-elevator-pitches.md` | **[repo]** `pdd-elevator-pitches.md` (was `20-elevator-pitches.md` in vault) | delete original |
| `ai-coding-target.md` | **[vault]** `90.Stachu/even-newer/ai-devloop/plan.md` (mined `newest/…/plan.md`) | archive both vault `plan.md` snapshots |
| `projects/*.md` (125 specs) | **[vault]** `even-newer/ai-devloop/projects/*` + `projects.md` + `Current Experiment/project-survey.md` | archive those vault sources |
| `projects/README.md`, `projects/_cross-cutting-test-criteria.md` | **[new]** (derived) | none |
| `results/*.md` | **[new]** (convention; numbers come later) | none — `specs/SUMMARY.md` in vault can be archived |

## good-for-ai-agents/

| Output | Origin | Action |
|---|---|---|
| `README.md` + 7 category docs | **[vault]** `even-newer/ai-devloop/improvements.md` + `improvements/*.md` (consolidated) | archive the vault `improvements/` set |

(Note: `dark suggest` was intentionally dropped per your feedback; the `dark docs for-ai`
composed-doc idea is in `agent-workflow.md`.)

## editing-software/

| Output | Origin | Action |
|---|---|---|
| `view-sketches.md` | **[repo]** `VIEW-SKETCHES.md` (extended) | delete original |
| `dark-virtual-files.md` | **[vault]** `Current Experiment/dark-virtual-files.md` | archive vault copy |
| `structural-editor.md` | **[new]** (cross-refs the vault editing note — not overwritten) | none — place new |

## later-other/

| Output | Origin | Action |
|---|---|---|
| `beam-vs-dark.md` | **[repo]** `research/beam-vs-dark.md` | delete original |
| `swamp-vs-dark.md` | **[repo]** `research/swamp-vs-dark.md` | delete original |
| `visibility-vs-dark.md` | **[repo]** `research/visibility-vs-dark.md` | delete original |

## meta/

| Output | Origin | Action |
|---|---|---|
| meta-reflections (`what-the-loop-is-good-at`, `where-the-loop-struggles`, `process-risks`, `loop-operations`, `README`) | **[vault]** `even-newer/ai-devloop/` reflection-template + plan-analysis + phasing + research-log + samples/historical + orchestration/queue docs; `newest/…/research-log.md` | archive those vault sources |
| `grounding-against-main.md`, `feedback-coverage.md`, `vault-organization.md`, `overwrite-map.md`, `STATUS.md`, `PRINT-LIST.md` | **[new]** (loop artifacts) | keep as wave records; not for the long-term notes tree unless you want them |
| `feedback.md` | your master spec input | leave as the frozen record |

## Originals to retire with NO direct successor (already absorbed)

These were dissolved into other docs — delete the repo/vault originals after promoting:

- **[repo]** `FRONTIER.md` — distributed into event-bus, sync, algorithm, conflicts, discovery-and-search, process-risks, benchmark-targets.
- **[repo]** `TOC.md` — obsolete index; replaced by the new `README.md`s.
- **[repo]** `READY-WORK.md` — replaced by `next-steps.md` (a fresh rewrite; themes A/B killed).
- **[repo]** `pdd-thinking/assets/` (the DAG PNGs) — only READY-WORK used them; drop.
- **[vault]** `90.Stachu/newest/ai-devloop/*` — the older (May-3) snapshot; archive whole.
- **[vault]** dropped orchestration docs (`tonights-queue`, `launch-checklist`, `feedback-plan`,
  `for-feriel`, the devloop `README`, `samples/dashboard-mock.html`) — ephemeral run-state; archive/drop.

## Do NOT touch

- **[vault]** `02.Project Management/Current Experiment/Design/Dark Async Plan.md` — the coworker's
  doc. Review-only; `async.md` was *informed by* it, never edits it. (Kept under `outputs/vault/` here
  only as a reading reference.)
- The Obsidian vault generally is off-limits to the loop — the moves above are *recommendations* for
  you to run; see `vault-organization.md` for the broader vault cleanup (esp. the messy `90.Stachu/`).

## Net effect after promotion

- The entire repo `pdd-thinking/` directory is **fully superseded** — every file is either
  promoted (renamed) into `notes/<theme>/` or dissolved. After promotion, `pdd-thinking/` can be
  deleted (or git-tagged then deleted).
- The vault loses its scattered ai-devloop snapshots to the consolidated repo `notes/` tree; keep in
  the vault only what's genuinely vault-shaped (personal/cross-cutting), per `vault-organization.md`.
- Two blocked items remain regardless: the HttpClient restriction notes and `feedback-from-agent.md`
  were never located (see `grounding-against-main.md` / `feedback-coverage.md`).
