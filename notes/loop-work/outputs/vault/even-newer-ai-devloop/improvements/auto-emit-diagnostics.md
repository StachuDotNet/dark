---
title: Auto-emit diagnostics after every write
section: 3.2 Authoring
priority: P1
harness_signal: turn count per project + rework ratio (§6 #9)
---

# Auto-emit diagnostics after every write

**Problem**: After `dark fn` succeeds at parsing, the agent doesn't see "and now these 3 callers no longer typecheck" until it explicitly asks. Wastes a turn (and tokens) per write. (`Agent Next Steps.md` items #22b, #22d.)

**Proposed fix**: after every successful `fn`/`type`/`val`, append a diagnostics block to stdout listing (a) any new parse/type errors anywhere in the package, (b) callers of the changed name (from `deps`) and whether they still typecheck, (c) a "1-line affected scope" summary. Behind a flag for human callers (`--quiet`) so it doesn't spam the REPL.

**External validation** (iter 25): [Anthropic's Claude Code best-practices doc](https://code.claude.com/docs/en/best-practices) explicitly recommends "install a code intelligence plugin to give Claude precise symbol navigation and automatic error detection after edits." This proposal is the Dark-CLI analogue.

**Harness signal**: drop in turn count per project (telemetry-derived); rework ratio (§6 #9) drops because agents catch self-inflicted breakage one turn earlier.
