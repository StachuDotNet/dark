# Vault organization & cross-system hygiene (recommendation)

This is a **recommendation for Stachu to execute** — the Obsidian vault is off-limits
to the loop, so nothing here is applied automatically. It addresses section 10 of the
plan: what lives in the repo vs. the vault, and how to reconcile the messy recently-added
vault material.

## Repo vs. vault: the rule

**Default: design + project + results notes live in the repo `notes/` tree** (tracked,
committed, reviewable in PRs). That is where this whole reorganized `outputs/` tree is
headed. The vault is for **personal/cross-cutting thinking that isn't repo-scoped**:
longer-horizon strategy, cross-project research, meeting/coworker notes, and anything you
want in your personal knowledge graph rather than in source control.

Concretely:

| Content | Home |
|---|---|
| Design docs (the `design/` set) | repo `notes/design/` |
| Project specs + bench results | repo `notes/projects/`, `notes/results/` |
| Issues/improvements, meta-reflections | repo `notes/` |
| Implementation reference (e.g. Tailscale capabilities, editing research) | vault (cross-ref from repo by name) |
| Coworker docs (e.g. the async plan) | vault (owned by them; repo cites by name) |
| Personal strategy / long-horizon | vault |

## Reconciling the `90.Stachu/` snapshots

The `90.Stachu/` folder accumulated overlapping snapshots of the same AI-devloop
thinking: `even-newer/` (May 5), `newest/` (May 3), plus `latest/` and `may8/`. These
are the same material at different times, which is exactly the "repeated stuff / outdated
phrasings" the effort is trying to kill.

Recommended steps:

1. **Treat `even-newer/` (May 5) as the canonical snapshot** of the devloop plan/projects/
   improvements. Its design content has already been extracted and tightened into this
   repo tree (`design/ai-coding-target.md`, `projects/`, `issues-and-improvements/`,
   `meta-reflections/`).
2. **Archive the superseded snapshots** (`newest/` May 3, and `latest/`/`may8/` if older
   than May 5) under a `90.Stachu/_archive/` folder — do not delete; just get them out of
   the active working set so they stop competing with the canonical version.
3. **If `may8/` is NEWER than May 5**, reconcile it against the canonical first — mine any
   genuinely new thinking into the repo notes, then archive it too.
4. **Once the repo notes are reviewed and promoted**, the `90.Stachu/` devloop docs become
   redundant with the repo — keep only what is genuinely vault-shaped (personal strategy)
   and archive the rest.

## Still-to-locate vault material

Two referenced inputs were not found and need a manual pointer (or confirmation they don't
exist under that name):

- **HttpClient restriction notes** — referenced by [design/capabilities.md](design/capabilities.md);
  the specific restriction spec is said to live in the vault but its exact location wasn't found.
- **`feedback-from-agent.md`** — referenced by the agent-workflow theme; not located under
  that name. May not exist yet, or lives under a different name.

## Net target

Fewer + smaller `.md` files; less repetition; no outdated phrasings — on this computer and
in the vault. The repo tree here is the model; the vault should converge toward the same
discipline (one canonical home per idea, superseded snapshots archived).
