# Dark working notes — reorganized (by theme)

This whole `outputs/` tree is the deliverable. Review it here, then copy/overwrite
into the long-term locations (see `meta/overwrite-map.md` for exactly which output
maps to which original). `../source/` holds frozen safety copies of every original —
nothing is lost.

The north star it all serves: Stachu's `print-md` script lives in Dark, edits sync
to his other machines, Ocean can fork it, and it shows up under `dark apps`.

## Read in this order

1. **`next-steps.md`** — what to build first (the Stable & Syncing path to the north star).
2. **`open-decisions.md`** — every unsettled decision, triaged by leverage.
3. **`meta/grounding-against-main.md`** — which design claims are verified against the
   live codebase vs. proposed/assumed.
4. Then dive into the theme dirs below, keystone first.

## Themes (top-level dirs)

### `stable-and-syncing/` — the priority cluster
The substrate spine. Keystone first:
- [distributed-event-sourcing.md](stable-and-syncing/distributed-event-sourcing.md) — **the keystone**: ops, projections, the thin `App` type
- [example-app.md](stable-and-syncing/example-app.md) — a worked non-trivial App (KV store)
- [apps-surface.md](stable-and-syncing/apps-surface.md) — `dark apps`: install/fork/run (the north-star surface)
- [event-bus.md](stable-and-syncing/event-bus.md), [conflicts.md](stable-and-syncing/conflicts.md), [sync.md](stable-and-syncing/sync.md), [async.md](stable-and-syncing/async.md)
- [cli-daemon.md](stable-and-syncing/cli-daemon.md), [hot-reload.md](stable-and-syncing/hot-reload.md), [cohabitation.md](stable-and-syncing/cohabitation.md), [remote-access.md](stable-and-syncing/remote-access.md)
- [capabilities.md](stable-and-syncing/capabilities.md), [identity.md](stable-and-syncing/identity.md), [package-system-layers.md](stable-and-syncing/package-system-layers.md), [composable-mvu.md](stable-and-syncing/composable-mvu.md)

### `removing-dark-files/`
- [bootstrap.md](removing-dark-files/bootstrap.md) — the blockers (punted until sync + stability land)

### `pdd/` — resting (clean & tightened, not advanced)
- [pdd.md](pdd/pdd.md) (overview), [algorithm.md](pdd/algorithm.md), [claims.md](pdd/claims.md), [pdd-elevator-pitches.md](pdd/pdd-elevator-pitches.md)
- [ai-coding-target.md](pdd/ai-coding-target.md) — Dark as the optimal AI coding target (the eval bench)
- [projects/](pdd/projects/) — 125 project specs + category index + cross-cutting test criteria
- [results/](pdd/results/) — the PDD bench-results convention (numbers generated later)

### `good-for-ai-agents/` — CLI/agent tooling improvements
- [README.md](good-for-ai-agents/README.md) + 7 category docs (agent-workflow, discovery-and-search,
  editing-and-refactor, diagnostics-and-errors, cli-ergonomics, publishing-and-sharing, traces-and-debugging)

### `editing-software/`
- [structural-editor.md](editing-software/structural-editor.md), [dark-virtual-files.md](editing-software/dark-virtual-files.md), [view-sketches.md](editing-software/view-sketches.md)

### `later-other/`
- research comparisons: [beam-vs-dark.md](later-other/beam-vs-dark.md), [swamp-vs-dark.md](later-other/swamp-vs-dark.md), [visibility-vs-dark.md](later-other/visibility-vs-dark.md)

### `meta/` — process + provenance (how the work was made and verified)
- [README.md](meta/README.md) + the meta-reflections (what-the-loop-is-good-at, where-it-struggles, process-risks, loop-operations)
- review aids: [grounding-against-main.md](meta/grounding-against-main.md), [feedback-coverage.md](meta/feedback-coverage.md),
  [vault-organization.md](meta/vault-organization.md), [overwrite-map.md](meta/overwrite-map.md), [STATUS.md](meta/STATUS.md), [PRINT-LIST.md](meta/PRINT-LIST.md), `feedback.md` (the master spec, frozen)

`../source/` is the frozen "before"; `vault/` holds the coworker's review-only async plan.

## Conventions

- Lowercase-kebab filenames; theme-based top-level dirs.
- "Ops vs. projections" is the lens throughout: state is a timestamped op stream; everything visible is a projection of it.
- Prose never uses the section-sign symbol — it writes "section N".
