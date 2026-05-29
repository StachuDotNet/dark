---
title: dark fn requires --update for existing names
section: 3.2 Authoring
priority: P1
harness_signal: commandExec failure count (transient rise) + pass@1 on iteration tasks
---

# `dark fn` requires `--update` for existing names; otherwise errors

**Problem**: agents silently overwrite existing functions they didn't read first. (`Agent Next Steps.md` item #10.) Hard to detect from the harness side; corrupts the gold-reference invariant if it leaks into eval runs.

**Proposed fix**: if the fully-qualified name already exists, `dark fn` errors with `name 'X' already exists; use \`view X\` to inspect, then \`fn --update X …\` to replace`. The view-then-update enforcement is in the error string itself, no agent-side prompt change needed.

**Harness signal**: `commandExec` failure count rises briefly (agents hit the new error), then drops as the convention solidifies; pass@1 rises on iteration tasks because clobbering bugs disappear.
