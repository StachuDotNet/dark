# Dark working notes — reorganized

This is the cleaned, reorganized notes tree being built in the loop sandbox. Once
reviewed, it is promoted into the real repo `notes/` tree. The frozen "before"
lives in `../source/`.

## How to review this (read first)

**This whole `outputs/` tree IS the deliverable** — "results of this overnight wave."
Review it here, then copy/overwrite the files into their long-term locations once you're
happy. `../source/` holds the frozen, untouched safety copies of every original — nothing
is lost, so the docs here are edited fully freely.

`outputs/results/` is a separate, nested dir holding the **PDD-specific bench results**
(raw bench data + one report per sweep). The actual numbers are generated later; for now
it carries the convention only.

The north star this all serves: Stachu's `print-md` script lives in Dark, edits
sync to his other machines, Ocean can fork it, and it shows up under `dark apps`.

## Structure

- **`design/`** — durable design docs. The spine: distributed event sourcing,
  the thin `App` type, ops vs. projections, sync, conflicts, capabilities,
  identity, async.
- **`projects/`** — spec files: each a goal line + acceptance-criteria list,
  tagged greenfield/brownfield. No phases, no iteration logs.
- **`results/`** — raw bench data + one report per sweep. No per-iteration
  intermediaries.
- **`issues-and-improvements/`** — one file per category of issue-space, each
  with candidate-fix subsections.
- **`meta-reflections/`** — how the *process* is going; feeds back into design.

Top-level review aids: `next-steps.md` (what to build first), `open-decisions.md`
(every unsettled decision, triaged), `grounding-against-main.md` (which claims are
verified against the live codebase vs. proposed/assumed), `feedback-coverage.md`
(feedback.md → status), `vault-organization.md` (vault recommendation), `PRINT-LIST.md`
and `STATUS.md`.

## design/ — what's there

| File | Was | Theme |
|---|---|---|
| `distributed-event-sourcing.md` | new (keystone) | the unifying frame: ops, projections, the `App` type |
| `event-bus.md` | EVENT-STREAMS-AND-PARKING | the op/event substrate + parking |
| `conflicts.md` | CONFLICTS-AND-RESOLUTIONS | reconciliation, organized by evaluation-time |
| `sync.md` | STABILITY-AND-SHARING | wire protocol + sharing over Tailscale |
| `async.md` | new (from section 2 + the coworker's plan) | kill Task/Ply, explicit parking |
| `capabilities.md` | CAPABILITIES | per-category effect permissions |
| `identity.md` | IDENTITY | the `Identity` model + intent |
| `bootstrap.md` | BOOTSTRAP | the blockers to removing `.dark` files (punted) |
| `cli-daemon.md` | improvements/cli-daemon | the resident host for the live substrate |
| `ai-coding-target.md` | vault plan.md | Dark as the optimal AI coding target |

Still to migrate into `design/` (later passes): ALGORITHM, CLAIMS,
pdd-elevator-pitches, COMPOSABLE-MVU, VIEW-SKETCHES, package-system-layers,
research/*, and the new structural-editor doc.

## Conventions

- New files use lowercase-kebab names.
- "Ops vs. projections" is the lens applied throughout: state is a timestamped op
  stream; everything visible is a projection of it.
- Prose never uses the section-sign symbol — it writes "section N".
