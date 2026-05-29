---
title: docs for-ai-bundle — single dense AI-centric doc command
section: 3.1 Discovery (cross-cutting with §3.2 doc-churn)
priority: P1 (round-2 user-flagged)
harness_signal: turns spent on doc-loading; first-fn-call latency
---

# `docs for-ai-bundle` — collapse AI doc-loading into one call

**Problem** *(user round-2 feedback verbatim)*: *"for-ai makes you load a few more docs, and we're spending multiple turns just wasting tokens/time. a denser ai-centric doc with fewer tokens and everything we might need would be better. and/or, having the for-ai doc just include _references_ to things that could be loaded into context. and some way to load all of those at once, maybe by including a reference list."*

Today `docs for-ai` is a top-level overview that points the agent at *more* `docs <topic>` calls — `docs syntax`, `docs scm`, `docs traces`, etc. An agent doing real work routinely loads 3–5 of those per session before producing a single `fn`. Each is its own CLI invocation paying the iter-23-measured ~0.7-1s cold-start; each is its own context-window ingest; each ate tokens before the agent could *do* anything.

**Proposed fix**: a single `docs for-ai-bundle` command that emits *everything* an agent needs in one shot:

- one-line workflow reminder (`fn → run → commit`)
- syntax cheat-sheet (the `docs syntax` essentials, ~30 lines)
- common-commands reference (the AI-relevant subset of top-level commands per the [`improvements/claude-md-template.md`](claude-md-template.md) snapshot)
- current package-tree summary (one-line per top-level module)
- current branch state (from `dark status`)
- a closing reference list of `docs <topic>` calls the agent *could* load if it needs more depth on a specific topic

**Output budget**: target a qualitatively-cheap dense doc — one `dark` invocation, one cold-start, one context-window load. The for-ai-bundle replaces 3–5 turn-by-turn lookups with a single front-loaded read.

**Implementation**: pure CLI surface, no language change. The `for-ai-bundle` command is a thin orchestrator that calls the existing `docs for-ai` / `docs syntax` / `tree` / `status` internals and concatenates with section headers. Behind a small shared formatter so the bundle stays cheap to maintain.

**Harness signal**: median tokens spent on doc-loading drops; turns-before-first-fn drops on every project (small per-run gain × N runs = real savings over a sweep). Bench should track `docs.*` CLI invocation count per run — agents using `for-ai-bundle` should drop to 1 (or 1 + N specific-topic deep dives only when needed).

---

**Cross-references**:
- Pairs naturally with [`claude-md-template.md`](claude-md-template.md) (§3.1 #1) — the auto-loaded `CLAUDE.md` template covers the "no orient calls at all" case; `for-ai-bundle` covers "agent needs to refresh / didn't get the autoload."
- Solves the **doc-bug catalog** in `improvements.md` "Documentation bugs surfaced during probes" (the `signatures` / `stdlib overview` / `find` / `list --fn` ambiguity issues): a bundle command fixes the dispatch problem at the consolidation point rather than per-doc-page.
