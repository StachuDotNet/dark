---
title: Sticky branch context (default-to-current)
section: 3.2 Authoring
priority: P1
harness_signal: count of --branch flags per run + accidental-main-commits
---

# Sticky branch context (or default-to-current)

**Problem**: branch state doesn't persist across CLI invocations, so every command needs `--branch <name>`. Agents forget and silently land on `main`. (`docs scm`, memory: branch context. Validated: vault TODO "No non-interactive `commit`".)

**Proposed fix**: write the current branch into `rundir/.dark-branch` (gitignored). `dark <cmd>` reads this file unless `--branch` overrides. `dark branch switch <name>` updates the file. Status of "untracked branch state" is visible via `dark status`. Trade-off: introduces hidden state — but that hidden state already exists in users' heads.

**Harness signal**: count of `--branch` flags per run drops to ~1 (the initial switch); accidental-main-commits in eval runs go to zero.

---

**Cross-references**:
- §3.4 #5 lists this as a §3.2 cross-ref to avoid duplication.
- The `rundir/.dark-branch` file format should compose with the existing `dark status` output; consider adding `branch:` to the status block once this lands.
