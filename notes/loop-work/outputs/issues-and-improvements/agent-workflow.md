# Agent workflow

Issues in how an agent orients at session start, records what it did, and hands work to a human reviewer. The unifying lens: most of these are **projections** the CLI already could compute (the package tree, the changeset, the deprecation list) but doesn't expose in an agent- or reviewer-consumable shape. The ops exist; the projections are missing or human-only.

## `dark docs for-ai` as a composed, expanding document

**Issue**: `docs for-ai` today is a top-level overview that points the agent at *more* `docs <topic>` calls — `docs syntax`, `docs scm`, `docs traces`. An agent doing real work routinely loads three to five of those per session before producing a single `fn`. Each is its own CLI invocation, its own cold-start, its own context ingest, and each spends tokens before the agent can do anything. The user's framing: "for-ai makes you load a few more docs, and we're spending multiple turns just wasting tokens/time. A denser AI-centric doc with everything we might need would be better — and/or have the for-ai doc include *references* to things that could be loaded, with some way to load all of them at once."

**Candidate fix — make `for-ai` a composed document, not a bigger static blob.** The goal is not to ship one giant CLAUDE.md. It is to make `dark docs for-ai` itself much more helpful: a document with dynamic and expanding content that loops in other docs, eventually informed by the specific project, task, and user.

A possible mechanism:

- The core `for-ai` doc emits the workflow essentials plus a **reference list of doc HASHES + names** — the topics the agent might want next, each addressable by a stable hash.
- A follow-up call `dark docs hash1 hash2 hash3` **concatenates those docs** into one beautifully formatted document in a single shot — one invocation, one cold-start, one context load — replacing three-to-five turn-by-turn lookups.
- Over time the composition becomes context-aware: the set of hashes the core doc recommends can be shaped by the current package tree, the task at hand, and per-user preferences.

**Content the composed doc should be able to pull in**: the one-line workflow reminder (`fn → run → commit`), the `docs syntax` essentials, the AI-relevant subset of top-level commands, a one-line-per-module package-tree summary, and current branch state — each as a hash-addressable fragment the agent loops in on demand.

**Implementation**: pure CLI surface, no language change — a thin orchestrator over the existing `docs` / `tree` / `status` internals plus a hash-addressed doc store, behind a small shared formatter. Solving the dispatch at this consolidation point also fixes the doc-bug catalog (the `signatures` / `stdlib overview` / `find` / `list --fn` ambiguities) at one place rather than per page.

**Note**: this entry may graduate into its own design doc once the hash-addressing and context-shaping mechanism is fleshed out.

## Auto-generated change summary at end of run

**Issue**: agents finish a session with no metadata about *why*. The reviewer reads a wall of new code with no goal or approach context, and even good agents don't write good commit messages by default.

**Candidate fix**: as the agent's last turn (or via a manual `dark agent-summary` command), produce a `SUMMARY.md` on the branch containing the goal as the agent understood it, the approach in a few bullets, an explicit list of deprecated items with reasoning for each, and anything the agent gave up on. The reviewer reads this first, then dives into the diff. Zero Dark-code change — pure agent-prompt behavior.

This is *post-hoc end-of-run* summarization (structured, full paragraphs), distinct from a *live in-flight* "what is the agent doing right now?" fragment refreshed from terminal tail — both are useful and complementary. Reusable patterns for the bench's own report generator: a fast/cheap model for the summary slot, ANSI-stripping for transcript processing, and a graceful pattern-match fallback so the report always has *something* in the summary slot.

## `dark review` — headless / structured flags on the existing TUI

**Issue**: `dark review` already exists as an interactive TUI for browsing branch changesets (with an `--all` flag and navigation keys). The TUI works for a human but is unusable from a script, CI, or agent prompt — no structured output, no way to bound a review window, no way to feed the surface into another tool.

**Candidate fix**: extend `dark review` with additive flags, keeping the TUI as default:

- `--json` produces the same surface as a structured tree (commits, changed items, deprecation list, fan-in counts).
- `--since <ref>` bounds the review window (defaults to parent branch; respects `review-mark` below if it ships).
- `--include-traces` attaches the most recent traces against changed fns, so the reviewer sees real execution rather than dead code.

Cost is lower than once assumed because the underlying review machinery already exists.

## `dark show <hash> --json`

**Issue**: `dark show` emits formatted human output; `--json` is missing. A reviewer's tooling (web UI, IDE plugin) can't consume the diff.

**Candidate fix**: emit a versioned structured shape — `{commit_hash, author, ts, msg, ops: [{kind, path, before?, after?, ...}]}` — covering add / edit / deprecate / rename op kinds. Enables `dark review --json` to compose cleanly and unlocks CI hooks. This is the review-surface instance of the broader `--json` rollout (see cli-ergonomics.md).

## Audit trail for `deprecate` / `discard`

**Issue**: deprecated items are hidden from `ls` / `tree` / `search` unless still referenced — great for the agent's working set, dangerous for review. A reviewer might miss that an agent quietly deprecated a critical function and replaced it with a less-restrictive version. There is no canonical "show me what got hidden" projection.

**Candidate fix**: `dark deprecated [--since <ref>] [--all]` lists every deprecation event with kind, reason, and replacement. `dark review` surfaces this as a separate, louder section — deprecations are shown to the reviewer, not opt-in. Pairs with the existing `deprecate --kind {obsolete, harmful, superseded-by}` discipline: agents should reach for `--kind superseded-by --replacement <new>` instead of overwriting, and this audit trail is what makes that discipline reviewable.

## `dark review-mark <ref>`

**Issue**: the SCM model has no equivalent of "this was reviewed by X." A reviewer who spent time in the `dark review` TUI can't record the decision; the next reviewer starts from scratch.

**Candidate fix**: `dark review-mark <ref>` writes a tiny SCM artifact (reviewer name, timestamp, ref). `dark review` then defaults to bounding its window by the most-recent mark. Lightweight, no PR concept needed. Needs a small SCM addition, so defer unless cheap; composes with `dark review --since`.
