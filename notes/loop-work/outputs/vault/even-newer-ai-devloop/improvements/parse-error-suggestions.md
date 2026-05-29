---
title: Parse errors carry suggested fixes + docs syntax pointer
section: 3.2 Authoring
priority: P1
harness_signal: median attempts before successful fn (T-tier) + first-parse-success (§6 #12)
---

# Parse errors carry suggested fixes + `docs syntax` pointer

**Problem**: parse errors today are message-only. Agent sees `Parse error at line 3 col 12: unexpected ','`. Doesn't know that lists use `;` not `,` (a known Dark gotcha — `docs syntax` line: "Lists (semicolon separator)"). (`Agent Next Steps.md` "Error Recovery" section. **Validated** by vault TODO: *"List arg with commas dies ungracefully. `[1L, 2L]` (commas) instead of `[1L; 2L]` (semicolons) produces a 100-line stack trace with no hint about the separator."* — exactly the case this proposal fixes.)

**Proposed fix**: parse errors include (a) a 3-line excerpt with the offending position underlined, (b) the most-likely-fix heuristic ("did you mean `;`?"), (c) a literal pointer `→ See: docs syntax`. The heuristic table is small (`,`-in-list → `;`, `@`-in-string → `++`, `let` inside `let` → no nested fns, …) — start with 6–8 entries and grow.

**Harness signal**: median attempts before a successful `fn` for the trivial tier drops; first-parse-success attempts (§6 #12) drops materially.
