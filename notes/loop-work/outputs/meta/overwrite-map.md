# Promotion & vault map — where each output goes, what it retires

The single guide for promoting this wave. The deliverable lives in `notes/loop-work/outputs/`;
the settled home is the repo **`notes/`** tree. Promotion = **copy each `outputs/<bucket>/` file
to `notes/<bucket>/`, then retire the original it supersedes** (delete the repo `pdd-thinking/`
copy, or archive the vault copy). Filenames + structure changed in wave 2, so this is
supersede-and-retire, not same-name in-place. (Consolidates the old overwrite-map +
vault-organization docs, refreshed to the wave-2 bucket structure.)

**Legend.** **[repo]** = `pdd-thinking/<NAME>` at the repo root (untouched by the loop).
**[vault]** = a file under `~/vaults/Darklang Dev/…` (read-only to the loop). **[new]** =
net-new wave-2 synthesis, no original to retire.

## pre-s-and-s/ (foundations)

| Output | Origin | Action |
|---|---|---|
| `distributed-event-sourcing.md` (keystone) | **[new]** (App-type synthesis; absorbed `package-system-layers`) | place new |
| `event-bus.md` | **[repo]** `EVENT-STREAMS-AND-PARKING.md` | delete original |
| `async.md` | **[new]** (informed by the coworker's async plan — *do not touch* below) | place new |
| `capabilities.md` | **[repo]** `CAPABILITIES.md` | delete original |
| `composable-mvu.md` | **[repo]** `COMPOSABLE-MVU.md` | delete original |
| `cli-daemon.md` | **[vault]** `90.Stachu/…/improvements/cli-daemon.md` | archive vault copy |
| `apps-surface.md`, `example-app.md` | **[new]** | place new |
| `tailscale.md` | **[new]** (informed by vault `…/Networking/Tailscale.md`, not overwriting it) | place new |
| `pr-eventbus.md`, `pr-ops-projections.md`, `pr-async-stage-a.md` | **[new]** (PR specs) | place new |

## stable-and-syncing/

| Output | Origin | Action |
|---|---|---|
| `sync.md` | **[repo]** `STABILITY-AND-SHARING.md` | delete original |
| `conflicts-and-resolutions.md` | **[repo]** `CONFLICTS-AND-RESOLUTIONS.md` | delete original |
| `bootstrap.md` | **[repo]** `BOOTSTRAP.md` | delete original |
| `steps-towards-print-md-sync.md` (the spine) | **[new]** (replaces `READY-WORK.md`/next-steps) | place new |
| `pr-conflict-dispatch.md`, `pr-sync-read-write.md`, `pr-print-md-app.md` | **[new]** (PR specs) | place new |

## good-for-ai-agents/

| Output | Origin | Action |
|---|---|---|
| `README.md` + 7 category docs + `ai-coding-target.md` | **[vault]** `…/ai-devloop/improvements*` + `plan.md` (consolidated + tightened) | archive the vault sources |

## pdd/

| Output | Origin | Action |
|---|---|---|
| `pdd.md` | **[repo]** PDD `README.md` + `TOC.md` (deduped) | delete both |
| `algorithm.md`, `claims.md`, `cohabitation.md`, `view-sketches.md` | **[repo]** `ALGORITHM/CLAIMS/COHABITATION/VIEW-SKETCHES.md` | delete originals |
| `pdd-elevator-pitches.md` | **[repo]** `pdd-elevator-pitches.md` | delete original |
| `projects/*.md` (127) | **[vault]** `…/ai-devloop/projects/*` + survey | archive vault sources |

## later/

| Output | Origin | Action |
|---|---|---|
| `identity.md`, `hot-reload.md` | **[repo]** `IDENTITY.md`, `HOT-RELOAD.md` | delete originals |
| `remote-access-and-control.md` | **[repo]** `REMOTE-ACCESS.md` | delete original |
| `beam-/swamp-/visibility-vs-dark.md` | **[repo]** `research/*-vs-dark.md` | delete originals |
| `dark-virtual-files.md` | **[vault]** `Current Experiment/dark-virtual-files.md` | archive vault copy |
| `structural-editor.md` | **[new]** | place new |

## meta/ (loop artifacts — keep as wave records, not the long-term tree)

`sanity-check.md` (the review verdict — worth keeping), this map, and `feedback.md` (the frozen
master input). Not promoted into `notes/<bucket>/` unless you want the wave record.

## Originals fully absorbed (no successor — retire after promoting)

- **[repo]** `FRONTIER.md` — dissolved into event-bus / sync / algorithm / conflicts / discovery.
- **[repo]** `TOC.md` — obsolete index, replaced by the bucket READMEs + the spine.
- **[repo]** `READY-WORK.md` — replaced by the spine (fresh rewrite).
- **[repo]** `pdd-thinking/assets/` (DAG PNGs) — only READY-WORK used them; drop.

## Vault hygiene (recommendation — the loop never edits the vault)

**Rule:** design/project/results notes live in the repo `notes/` tree (tracked, PR-reviewable);
the vault keeps personal/cross-cutting/long-horizon thinking + coworker-owned docs (cited by
name, not copied). **`90.Stachu/` cleanup:** it holds overlapping ai-devloop snapshots
(`even-newer/` May 5, `newest/` May 3, `latest/`, `may8/`) — treat **`even-newer/` as canonical**
(already mined into this tree), archive the older snapshots under `90.Stachu/_archive/`, and if
`may8/` is newer, reconcile its genuinely-new bits first. **Still-to-locate** (referenced, not
found — confirm or point): the HttpClient-restriction notes (capabilities) and
`feedback-from-agent.md`.

## Do NOT touch

- **[vault]** `…/Design/Dark Async Plan.md` — the coworker's doc. Read-only; `async.md` was
  *informed by* it, never edits it.
- The vault generally is off-limits to the loop — every "archive/delete" above is a
  **recommendation for you to run**, not an action the loop took.

## Net effect

After promotion, the repo `pdd-thinking/` is **fully superseded** (every file promoted-and-renamed
into `notes/<bucket>/` or dissolved) and can be deleted (or git-tagged then deleted). The vault
loses its scattered ai-devloop snapshots to the consolidated repo tree, keeping only genuinely
vault-shaped material. The wave-2 net-new product (the keystone, the spine, the 6 PR specs,
tailscale, the apps surface) has no originals to retire — it's the new substance.
