---
title: dark publish <project> --out <path>
section: 3.5 Sharing
priority: P1 (headline)
harness_signal: §6 #18 "Time to friend-runnable artifact"
---

# `dark publish <project> --out <path>` — *the headline missing tool*

**Problem**: agents finish a project, the human can't ship it. There's no command. The runtime is already a single launcher + dlls + DB; the missing step is "package these into one redistributable."

**Proposed fix**: `dark publish Darklang.MyProject --out ./myapp` produces a directory (or zip) containing (a) the Cli launcher, (b) only the dlls actually referenced by the project's transitive package-tree closure, (c) a stripped `data.db` containing only the project's package items, (d) a thin `myapp` shell script that sets `DARK_RUNDIR` and exec's the launcher with the project's main fn. Friend runs `./myapp serve` or `./myapp run main`. No devcontainer.

**Stretch**: `--single-file` mode self-extracts to `/tmp` on first run (Go-binary style). Removes the "directory of stuff" UX wart at the cost of slightly slower cold-start.

**Harness signal**: enables a Phase 3+ metric "Time to friend-runnable artifact" (§6 #18) — measure `time dark publish && scp && ssh ./myapp run`.

---

**Why this is the headline §3.5 item**: the user's [plan.md](../plan.md) §1 promise — "Claude builds a thing in Dark; you immediately share it with a friend; the friend runs it on their laptop/phone with no setup" — depends entirely on this. Without `dark publish`, the friend-can-run goal is aspirational. With it, it's measurable.
