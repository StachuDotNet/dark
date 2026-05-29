# todo-cli

**Goal:** Manage a persistent to-do list via `add`, `list`, `done`, and `rm` subcommands with timestamps and stable ids.

**Kind:** greenfield

## Acceptance criteria
- [ ] `add <text>` creates a timestamped item with a stable id.
- [ ] `list` shows pending items only by default; `list --all` also shows completed items.
- [ ] `done <id>` marks an item complete; `rm <id>` removes an item.
- [ ] State persists across separate process invocations (across sessions).
- [ ] Item ids remain stable across operations.
- [ ] A sequence of add/list/done/rm operations across two sessions behaves consistently.
- [ ] `todo-cli --help` prints usage and exits 0; an unknown subcommand exits non-zero with usage.
