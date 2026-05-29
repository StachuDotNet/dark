# Issues and improvements

What to change in Dark to compress the AI dev loop, organized by where the loop spends time. Each file collects a category of issue and the candidate fixes within it, written as issue → candidate fix.

The unifying lens across categories: most of these are missing or human-only **projections** of state the CLI already holds as **ops** — the package tree, the changeset, recorded traces, the deprecation list, the current branch. The work is rarely new machinery; it's exposing what exists in an agent- or reviewer-consumable shape.

## Categories

- [discovery-and-search.md](discovery-and-search.md) — finding what already exists in the package tree: auto-loaded `CLAUDE.md` template, ranked structured search, "did you mean" on a search miss.
- [editing-and-refactor.md](editing-and-refactor.md) — the agent changing its own code: AST-aware `dark edit`, `dark rename` with caller auto-update, `dark uncommit` / `revert`, session-scoped `dark since`, surfacing `merge --dry-run` / `rebase --status`.
- [publishing-and-sharing.md](publishing-and-sharing.md) — the friend-can-run promise: `dark publish`, reproducible builds, `dark export` / `import`, and a deferred WASM target.
- [diagnostics-and-errors.md](diagnostics-and-errors.md) — actionable failure messages: parse-error suggestions, auto-emitted diagnostics after each write, "did you mean" on a runtime not-found.
- [cli-ergonomics.md](cli-ergonomics.md) — cross-cutting CLI friction: non-TTY orientation, the `--json` audit and roll-out, sticky branch context.
- [traces-and-debugging.md](traces-and-debugging.md) — surfacing the trace primitives that exist (auto-attach trace on failure, `hotspots` review pass, `traces view --json`) and building the missing ones (`replay --diff`, `run --replay` shorthand, `gen-test`).
- [agent-workflow.md](agent-workflow.md) — orienting, recording, and handing work to a reviewer: the composed/expanding `dark docs for-ai` doc, end-of-run change summary, headless flags on `dark review`, `dark show --json`, deprecation audit trail, `dark review-mark`.

## Notes

- **`dark suggest` was dropped** — the natural-language discovery affordance is gone; its intent-to-module goal is partly served by better search ranking and the composed `for-ai` doc.
- **`cli-daemon` is not here** — it graduated into its own design doc (design/cli-daemon.md); not duplicated in this set.
- **`dark docs for-ai`** is captured in agent-workflow.md as a composed document that loops in other docs (core doc lists doc hashes + names; a follow-up `dark docs hash1 hash2 hash3` concatenates them), eventually shaped by the current project, task, and user. It may graduate into its own design doc later.
