# Dark improvements pillar

> What to change in Dark to compress the AI dev loop. See [`plan.md`](plan.md) for harness/metrics; [`projects.md`](projects.md) for the bench. Every improvement here gets evaluated through the §7 Phase 3 A/B protocol — branch + sweep + accept-or-revert.

Organized by where the agent loop spends time. For each item: **problem**, **proposed fix**, **harness signal** (which §6 metric should move). Validation against user-known frictions in `~/vaults/Darklang Dev/02.Project Management/Current Experiment/{where we're a bit short,TODOs to Improve AI's Development}.md` is noted inline.

---

## 3.1 Discovery (agent finding what already exists)

Discovery is the *first* friction every agent loop hits. If the agent can't find what's already in the package tree, it either re-implements badly or wastes a turn searching. Five concrete items, ordered by how early in the loop they bite.

1. **`darklang/CLAUDE.md` auto-loaded template.**
   - *Problem*: agents start cold. Every session begins with the agent calling `dark tree`, `dark status`, `docs for-ai`, etc. — 3–5 orientation calls before any task work. (`Agent Next Steps.md` items #3, #11.)
   - *Proposed fix*: Dark ships a `CLAUDE.md` (and `AGENTS.md` symlink) template at any Dark project root, picked up automatically by Claude Code. Includes (a) a one-line workflow reminder (`fn → run → commit`), (b) a freshly-rendered `dark tree` snapshot, (c) the current branch from `dark status`, (d) a pointer to `docs for-ai` for deep dives. Refresh hook: `dark commit` rewrites the snapshot section.
   - *Harness signal*: median *first-tool-call type* shifts from "orient" (`tree`/`status`/`docs`) to "task" (`view`/`fn`/`run`). Tokens-to-first-fn drops materially on the trivial tier.

2. **`dark search` ranking + structured output.**
   - *Problem*: `dark search json` (verified iter 13) returns alphabetical groupings with emojis and ANSI colors — token-noisy and unranked. `--json` is absent (other listy subcommands have it; `traces list --json`, `traces find --json`, etc.). Agents scroll through a wall of `Darklang.JsonRPC.*` matches before getting to `Stdlib.Json`.
   - *Proposed fix*: (a) add `dark search --json` with the same surface as `traces find --json`. (b) Rank results by composite score = `prefix-match-bonus + exact-name-bonus + (log of usage count from package-tree refs) − path-depth`. Stdlib lifts above niche modules without manual curation.
   - *Harness signal*: tokens-to-first-relevant-fn drops; trace adoption rate (§6.0 #4) untouched (this is upstream of trace use).

3. **"Did you mean" on miss.**
   - *Problem*: verified iter 13 — `dark search xqzbf` outputs `No results found for: xqzbf` and stops. `Stdlib.NoSuchModule.foo` errors with `not found`, no fuzzy-match candidate. Two of the loudest "agent gives up and re-implements" triggers.
   - *Proposed fix*: (a) on empty `search` results, suffix the response with "Did you mean: `<top 3 fuzzy matches by trigram or Levenshtein>`?" (b) the same for "X not found" runtime errors. Heuristic must be cheap (trigram index over the package tree, refreshed on commit).
   - *Harness signal*: rework ratio (§6 supporting metric) drops; first-parse-success attempts (§6 diagnostic) drops on Dark.

4. **`dark suggest <natural-language>` — affordance for "what handles X?"**
   - *Problem*: agents know what they want to do ("parse JSON", "hash a string") but not which module does it. They typically end up calling `docs stdlib` then drilling into 4–5 modules. `Agent Next Steps.md` calls this out as item #4 (token budget).
   - *Proposed fix Phase 1*: a thin wrapper around full-text search across `view <name>` doc strings, scored by overlap. **Unblocked today** — verified iter 30 that `Darklang.LLM` is rich (11 example modules, multi-provider) but full-text doesn't need any of that — `Stdlib.String.contains` over package-tree docstrings is enough.
   - *Proposed fix Phase 3+ stretch*: replace with an embedding-based search using `textEmbedding3Small` or equivalent. **Blocked today** — verified iter 30 that `Darklang.WIP.AI.OpenAI.Embeddings` has the types (`CreateEmbeddingResponse`, `textEmbedding3Large/Small/Ada002`) but the **Functions list is empty**. WIP namespace; no callable embedding fn exists. Stretch waits until that ships.
   - *Harness signal*: tokens-to-first-relevant-fn drops further; the metric `dark search` invocations per run drops (replaced by one `dark suggest` call).
   - *Adjacent finding (iter 30)*: `Darklang.LLM.Examples.CodeAgent` exists as prior art for an in-Dark coding agent. **Worth a future survey iteration** — its system prompt / tool-use patterns may inform our §4.7 harness prompt template, and our migration-to-Dark milestone (when the harness eventually ports to Dark per §4.0).

5. **Bare `dark` invocation defaults to agent-friendly orientation.**
   - *Problem*: verified iter 13 — `dark` (no args) prints an ASCII-art banner + command groupings with ANSI codes. Lovely for humans; noisy when the agent is iterating. The tokens for that banner are paid every time.
   - *Proposed fix*: detect non-TTY callers (no terminal capabilities) and emit a terse plaintext orientation: 1 line per command group, no banner, no ANSI. Behind the scenes the same logic that already powers `--no-color` envvars in many CLIs. No agent prompt change needed; the savings are automatic.
   - *Harness signal*: median tokens for the agent's first turn drops on every project (small per-run gain × N runs = real money over a sweep).

6. **Cross-cutting `--json` audit and roll-out.**
   - *Problem*: `--json` exists on 5 commands, missing on 9 of the most-used ones. Verified iter 18 (rerun any of these to confirm):

     | Command | `--json`? | Verified by |
     |---|---|---|
     | `traces list` | ✅ clean JSON per-record | `./scripts/run-cli traces list --json 5` |
     | `traces find` | ✅ | `help traces` (iter 6) |
     | `traces stats` | ✅ clean JSON | `./scripts/run-cli traces stats --json` |
     | `traces hotspots` | ✅ | `help traces` (iter 6) |
     | `traces follow` | ✅ NDJSON | `help traces` (iter 6) |
     | `traces view` | ❌ | iter 6 — silently ignores `--json` |
     | `tree` | ❌ | iter 18 — emits ANSI tree |
     | `search` | ❌ | iter 13 — formatted output |
     | `status` | ❌ | iter 18 — ANSI |
     | `log` | ❌ | iter 18 |
     | `view` | ❌ | iter 18 — flag rejected, usage printed |
     | `branch` | ❌ | iter 18 |
     | `deps` | ❌ | iter 18 |
     | `docs <topic>` | ❌ | iter 18 — silently ignores |
     | `builtins` | ❌❌ | iter 18 — `--json` interpreted as a *search filter* (worse than ignored: the UX confuses an agent into thinking it filtered) |

     5 with, 9 without. The 9 without are the ones agents reach for most often (orient → search → view → log → status). The `builtins` bug is the worst category: `--json` doesn't error or get ignored, it gets coerced into a filter argument.
   - *Proposed fix*: roll out `--json` across all 9 missing commands plus fix the `builtins` bug. Output schema for each: structured shape that mirrors the human view (e.g. `tree --json` emits `{owner, modules: [{path, fns: [...], types: [...]}, ...]}`). Behind a small shared formatter so adding `--json` to a new command becomes a one-liner. No language/runtime change.
   - *Meta-principle for §3 broadly*: **any command an agent may consume should ship `--json`**. New commands should default to JSON-supportable output design. Audit-as-of-2026-05-02 above is the baseline; track it per sweep to ensure we don't regress.
   - *Harness signal*: median tokens per Dark run drops (parseable output is denser than ANSI-formatted human view); first-parse-success attempts (§6 #12) — a downstream effect since agents that parse `view` cleanly produce better follow-up code; constraint-escape attempts (§6 #13) — agents won't shell out to `grep`/`awk` to massage `view` output if `view --json` exists.

Out of §3.1 scope: the §3.2 sticky-branch fix (covered there); the §3.3 trace surface (covered there). Out of §3 scope entirely: a dedicated package registry / search-as-a-service (Phase 4+).

---

## 3.2 Authoring (agent writing new code)

This is where agents spend the most output tokens. Five concrete items, ordered roughly by expected token-impact (largest first). Each lists **problem**, **proposed fix**, **harness signal** so we know the fix worked.

1. **`dark edit` — diff-based function editing.**
   - *Problem*: `dark fn Foo.bar = …` requires resending the full body even for a one-line change. A 50-line function with one bug costs ~50 lines of output every iteration. (`~/vaults/.../Agent Next Steps.md` item #19. Validated by user-known friction: vault TODO "multi-line editing" + "update existing package entities" + "rename package items".)
   - *Proposed fix*: a new CLI command `dark edit <name> --replace <old> --with <new>` with exact-string-replace semantics (Claude-Code-Edit-style: error if `<old>` not unique, succeed silently otherwise). Falls back to whole-body rewrite if `--replace` is omitted. No language-level change required — it's a CLI surface only.
   - *Harness signal*: median tokens per pass (§6 #5), edit-to-first-green (§6 #8) drop on the M/L tier. Compare a sweep with/without the wrapper exposing `edit`.

2. **Auto-emit diagnostics after every write.**
   - *Problem*: After `dark fn` succeeds at parsing, the agent doesn't see "and now these 3 callers no longer typecheck" until it explicitly asks. Wastes a turn (and tokens) per write. (`Agent Next Steps.md` items #22b, #22d.)
   - *Proposed fix*: after every successful `fn`/`type`/`val`, append a diagnostics block to stdout listing (a) any new parse/type errors anywhere in the package, (b) callers of the changed name (from `deps`) and whether they still typecheck, (c) a "1-line affected scope" summary. Behind a flag for human callers (`--quiet`) so it doesn't spam the REPL.
   - *External validation* (iter 25): [Anthropic's Claude Code best-practices doc](https://code.claude.com/docs/en/best-practices) explicitly recommends "install a code intelligence plugin to give Claude precise symbol navigation and automatic error detection after edits." This proposal is the Dark-CLI analogue.
   - *Harness signal*: drop in turn count per project (telemetry-derived); rework ratio (§6 #9) drops because agents catch self-inflicted breakage one turn earlier.

3. **`dark fn` requires `--update` for existing names; otherwise errors.**
   - *Problem*: agents silently overwrite existing functions they didn't read first. (`Agent Next Steps.md` item #10.) Hard to detect from the harness side; corrupts the gold-reference invariant if it leaks into eval runs.
   - *Proposed fix*: if the fully-qualified name already exists, `dark fn` errors with `name 'X' already exists; use \`view X\` to inspect, then \`fn --update X …\` to replace`. The view-then-update enforcement is in the error string itself, no agent-side prompt change needed.
   - *Harness signal*: `commandExec` failure count rises briefly (agents hit the new error), then drops as the convention solidifies; pass@1 rises on iteration tasks because clobbering bugs disappear.

4. **Parse errors carry suggested fixes + `docs syntax` pointer.**
   - *Problem*: parse errors today are message-only. Agent sees `Parse error at line 3 col 12: unexpected ','`. Doesn't know that lists use `;` not `,` (a known Dark gotcha — `docs syntax` line: "Lists (semicolon separator)"). (`Agent Next Steps.md` "Error Recovery" section. **Validated** by vault TODO: *"List arg with commas dies ungracefully. `[1L, 2L]` (commas) instead of `[1L; 2L]` (semicolons) produces a 100-line stack trace with no hint about the separator."* — exactly the case this proposal fixes.)
   - *Proposed fix*: parse errors include (a) a 3-line excerpt with the offending position underlined, (b) the most-likely-fix heuristic ("did you mean `;`?"), (c) a literal pointer `→ See: docs syntax`. The heuristic table is small (`,`-in-list → `;`, `@`-in-string → `++`, `let` inside `let` → no nested fns, …) — start with 6–8 entries and grow.
   - *Harness signal*: median attempts before a successful `fn` for the trivial tier drops; first-parse-success attempts (§6 #12) drops materially.

5. **Sticky branch context (or default-to-current).**
   - *Problem*: branch state doesn't persist across CLI invocations, so every command needs `--branch <name>`. Agents forget and silently land on `main`. (`docs scm`, memory: branch context. Validated: vault TODO "No non-interactive `commit`".)
   - *Proposed fix*: write the current branch into `rundir/.dark-branch` (gitignored). `dark <cmd>` reads this file unless `--branch` overrides. `dark branch switch <name>` updates the file. Status of "untracked branch state" is visible via `dark status`. Trade-off: introduces hidden state — but that hidden state already exists in users' heads.
   - *Harness signal*: count of `--branch` flags per run drops to ~1 (the initial switch); accidental-main-commits in eval runs go to zero.

Out of scope for §3.2 (covered elsewhere): traces as test inputs (§3.3), rename safety (§3.4), the `dark publish` command (§3.5).

---

## 3.3 Verification (agent checking what it wrote works)

Dark's trace surface is far more developed than commonly assumed (see [plan.md](plan.md) §2; verified iter 6). The improvement work in §3.3 is about **making the agent reach for these primitives by default** rather than building new ones.

1. **Auto-attach the latest trace to every failing `run`.** When `dark run X args` exits non-zero (or returns a `Result.Error`), the wrapper / agent prompt should follow up with `traces tail` automatically and feed that into the agent's next turn — saves a discovery hop. Surfaces *for the agent* something Dark already has *for the human*.
2. **`run --replay <trace-id>` shorthand.** Today: agent calls `traces replay <id> --diff` (long, two-step lookup). Proposed: every failing `run` returns its trace ID prominently, and `dark run --replay <prefix>` is sugar for "re-run the same inputs against current code, diff against recorded output." Compresses the bug-fix loop to two commands.
3. **Promote `gen-test <trace-id>` in agent docs.** It exists; the agent has no reason to know it does. A line in `docs for-ai` saying "after any successful `run` of a non-trivial fn, consider `traces gen-test <id>` to capture it as a regression test" turns "did I write a test?" into a one-token answer.
4. **`hotspots` as a built-in code-review pass.** When the agent says "done", the wrapper runs `traces hotspots` over the recent traces and surfaces any fn taking >10 ms. Becomes free perf-review at no extra agent cost.
5. **JSON output for `traces view`.** Today `--json` exists for `list`, `find`, `stats`, `hotspots`, `follow`. Confirmed *missing* from `view` itself per `help traces`. Adding it lets the agent parse trace bodies structurally instead of regex-ing the human-formatted view.

Harness signal for §3.3 work: **fix-iteration delta** (§6 #3) — Dark's expected biggest win — should rise materially after items 1+2 ship.

Adjacent gap (validated by vault `where we're a bit short.md`): "fix tracing / make it more useful" + a known runtime regression: *"httpServerServe runs handlers in a fresh execution context without lambda instruction caches, so handlers that use lambdas (List.map, routeRequest, etc.) will fail at runtime."* Resolving the regression is a Dark-runtime change (not a §3 CLI-surface item) but the bench should detect it via HTTP-handler project failures in Phase 2.

---

## 3.4 Iteration (agent changing what it wrote)

The agent's *own* edit-rebuild-recheck loop. Distinct from §3.2 (writing new code) and §3.6 (human review). Validated by `where we're a bit short.md` — *"updating and renaming package items"* is on the user's known-gaps list. Five items, ordered by how often the agent will hit each.

1. **`dark rename <old> <new>` with auto-update of callers.**
   - *Problem*: verified iter 21 — `./scripts/run-cli help` has `branch rename` but no top-level `rename` for package items. Today an agent that picks the wrong name has to (a) `view` the bad name, (b) `fn --update` it, (c) grep for callers, (d) edit each. Token-expensive and error-prone. Validated by user-known friction in vault TODO.
   - *Proposed fix*: `dark rename Old.Name New.Name` updates the package-tree item *and* every reference. Errors if the new name conflicts. Behind `--dry-run` for previewing the call-site list. Closest existing analog: `traces export`/`import` machinery already moves package items between trees; `rename` is the within-tree variant.
   - *Harness signal*: median tokens per pass on M/L tier drops on rename-heavy runs; rework ratio (§6 #9) drops because the "I picked the wrong name and now I'm stuck" failure mode goes away.

2. **`dark uncommit` / `dark revert <commit>` — undo a *committed* change.**
   - *Problem*: verified iter 21 — `dark discard` only handles uncommitted changes (per `docs scm`). Once an agent commits a bad change, there's no equivalent of `git reset HEAD^` or `git revert`. The agent has to manually re-author the prior version.
   - *Proposed fix*: (a) `dark uncommit` — pop the most-recent commit on the current branch back into uncommitted state, preserving work. (b) `dark revert <commit>` — create a new commit that undoes a prior one. The two together cover "I just committed a typo" and "this old commit was wrong."
   - *Harness signal*: edit-to-first-green (§6 #8) drops on iteration tasks; rework ratio (§6 #9) drops; agent-abandonment (Phase 1 metric) decreases when agents previously got stuck because they couldn't unwind a commit.

3. **Session-scoped "what have I changed?" view.**
   - *Problem*: agents iterating across many `fn` calls lose track of their own working set, especially when the package tree is large. Today they can `dark log` for commits or `dark status` for uncommitted, but there's no "show me everything I've touched in this session, regardless of commit boundary."
   - *Proposed fix*: `dark since <ref>` (where `<ref>` defaults to the start of the current session, recorded in `rundir/.dark-session-start`) lists every package item created or modified since `<ref>`, with a `view` snippet for each. Cross-pollinates with §3.6 #1 `dark review`, but session-scoped (agent-facing) vs review-scoped (human-facing).
   - *Harness signal*: drop in turn count per project (telemetry-derived); the agent stops re-discovering its own work.

4. **Surface `merge --dry-run` and `rebase --status` in the agent prompt template.**
   - *Strength to surface, not a gap*: verified iter 21 that both already exist. `merge --dry-run` checks "is this merge clean?" without doing it; `rebase --status` checks for conflicts without rebasing. **Phase 3's A/B wave workflow** ([plan.md](../ai-devloop/plan.md) §7) hits these naturally — the bench's `--dark-revision` flag wants to know "will my candidate branch cleanly pull onto main?" before running.
   - *Proposed fix*: a §4.7-template prompt addition that mentions both commands when the agent is about to `merge` or pre-flight a branch move. **Costs nothing** — pure prompt-template change, no Dark code.
   - *Harness signal*: agent abandonment in cross-branch operations drops; cleaner Phase 3 protocol execution.

5. **Cross-references (covered elsewhere; don't duplicate)**:
   - Sticky branch context: §3.2 #5.
   - Deprecation visibility for review: §3.6 #4.
   - Editing existing fn bodies: §3.2 #1 (`dark edit`).
   - "Read before update" guard: §3.2 #3.

Out of §3.4 scope: distributed merge / pull / push (deferred per [plan.md](../ai-devloop/plan.md) §1 "out of scope: distribution / sync / multi-user collab").

---

## 3.5 Sharing / running the result

**The promise the user wants** (per [plan.md](plan.md) §1): Claude builds a thing in Dark; you immediately share it with a friend; the friend runs it on their laptop/phone/different computer with no setup. **What exists today** (verified iter 10): `./scripts/run-cli export-seed` extracts a minimal `seed.db` — not a runnable app. The §2 "single distributable" bullet was demoted to aspirational because of this gap. The fix is the most user-visible improvement on this whole list.

1. **`dark publish <project> --out <path>`** — *the headline missing tool.*
   - *Problem*: agents finish a project, the human can't ship it. There's no command. The runtime is already a single launcher + dlls + DB; the missing step is "package these into one redistributable."
   - *Proposed fix*: `dark publish Darklang.MyProject --out ./myapp` produces a directory (or zip) containing (a) the Cli launcher, (b) only the dlls actually referenced by the project's transitive package-tree closure, (c) a stripped `data.db` containing only the project's package items, (d) a thin `myapp` shell script that sets `DARK_RUNDIR` and exec's the launcher with the project's main fn. Friend runs `./myapp serve` or `./myapp run main`. No devcontainer.
   - *Stretch*: `--single-file` mode self-extracts to `/tmp` on first run (Go-binary style). Removes the "directory of stuff" UX wart at the cost of slightly slower cold-start.
   - *Harness signal*: enables a Phase 3+ metric "Time to friend-runnable artifact" — measure `time dark publish && scp && ssh ./myapp run`.

2. **`dark publish --target wasm`** — browser distribution.
   - *Problem*: "share with a friend on their phone" easiest path is a URL.
   - *Proposed fix*: WASM target compiles the project + a slim runtime to a single `.wasm` + HTML harness. Out-of-scope for Phase 1–3; a Phase 4+ stretch goal.

3. **Reproducible builds.**
   - *Problem*: agent-built projects need to be re-buildable from package-tree state alone. If `dark publish` depends on filesystem state outside the package tree, sharing breaks.
   - *Proposed fix*: `dark publish` reads exclusively from the named project's transitive closure in the package tree. CI: re-run `publish` on a fresh clone, byte-compare the artifact.

4. **Run-without-publish** — the friend story doesn't always require an artifact.
   - *Problem*: simplest possible share is "the friend has Dark installed and runs the same code." Dark's package-tree-first model already supports this — `dark export <project>` could emit a single SQL dump that `dark import` re-hydrates.
   - *Proposed fix*: `dark export <project> > project.darkpack` + `dark import < project.darkpack`. Smaller than a published binary; useful for collaboration; close cousin of `traces export`/`import` which already exists. Reuse that machinery.

Out of §3.5 scope: discovery / package registry / multi-machine sync (user explicitly deferred distribution).

Harness signal for §3.5 work: **§6 #18 "Time to friend-runnable artifact"** *(defined iter 33 / activates when wave 5 ships)* — measures the actual user-visible delay from "agent says done" to "friend can run it." Promotes the friend-can-run goal from [plan.md](plan.md) §1 (ambitious) to §6 (trackable).

---

## 3.6 Human review of agent-built Dark code

**The pain** (validated by `where we're a bit short.md` — *"review: no good way to review code"*): an agent ships a wave of changes; a human needs to verify them in 5–15 minutes before merging. The agent likely deprecated a few things, renamed others, restructured a module. Existing `dark log` / `dark show` give the *what*, not the *implications*. Five concrete items, ordered by reviewer time-on-task they save.

1. **`dark review` — augment the existing TUI with structured/headless output.**
   - *Discovery (final-validation pass)*: `dark review` *already exists* — verified at `packages/darklang/cli/core.dark:227` ("Review branch changesets"). It's an **interactive TUI** with `--all` flag (show all branches), navigation keys (Up/Down/Right/Enter for detail/diff/commits, Left/Esc for back). The original §3.6 #1 framing called it "the headline missing tool" — that was wrong; the tool exists. The augmentation below is what's actually needed.
   - *Problem* (refined): the TUI works for an interactive human reviewer but is unusable from a script, CI, or agent prompt. There's no `--json` output, no `--since <ref>` filter to bound a review window to "since I last looked," no way to feed the review surface back into a different tool (e.g. an agent that wants to summarise what another agent did).
   - *Proposed fix*: extend `dark review` with three new flags: (a) `--json` produces the same surface as the TUI but as a structured tree (commits / changed items / deprecation list / fan-in counts) — rides on the §3.1 #6 `--json` rollout pattern; (b) `--since <ref>` bounds the review window (defaults to parent branch; respects the §3.6 #5 `review-mark` if it ships); (c) `--include-traces` attaches the most recent traces against changed fns (so the reviewer sees real exec, not dead code). Keep the TUI mode as the default; flags are additive. **Lowest cost than I originally claimed** because the underlying review machinery exists.
   - *Harness signal*: this is human-time, not agent-time, so off-§6. Track separately as "median reviewer time-to-decision" once we instrument it. Phase 4+ metric.

2. **Structured `dark show <hash> --json`** *(rides on the §3.1 #6 `--json` rollout)*.
   - *Problem*: `dark show` emits formatted human output today; iter 18 audit confirmed `--json` missing. A reviewer's tooling (gh-style web UI, IDE plugin) can't consume the diff.
   - *Proposed fix*: emit `{commit_hash, author, ts, msg, ops: [{kind: "fn-add"|"fn-edit"|"fn-deprecate"|...|"rename", path, before?, after?, ...}]}`. Schema versioned (`_v: 1`).
   - *Harness signal*: not direct; enables the `dark review` summary above to compose cleanly + enables CI hooks.

3. **Auto-generated change summary at end of agent run.**
   - *Problem*: agents finish a session with zero metadata about *why*. Reviewer reads a wall of new code without context. Even good agents don't write good commit messages by default.
   - *Proposed fix*: on `__HARNESS_DONE__` (or a manual `dark agent-summary` command), produce `<branch>/SUMMARY.md` containing (a) goal as the agent understood it (from the spec/task prompt), (b) approach taken in 3–5 bullets, (c) explicit list of deprecated items + the agent's reasoning for each, (d) anything the agent gave up on. Agent generates this *as the last turn*, not the wrapper — so it's part of the run's token budget. Reviewer reads SUMMARY.md first, then dives into `dark review` for the diff.
   - *Harness signal*: indirect — should reduce the "median reviewer time-to-decision" metric materially.

4. **Audit trail for `deprecate` / `discard` moves.**
   - *Problem*: memory `project_deprecation_visibility` says deprecated items are hidden from `ls`/`tree`/`search` unless still referenced. *Great* for the agent's working set; *dangerous* for review — the reviewer might miss that an agent quietly deprecated `Critical.Auth.checkUser` and replaced it with a less-restrictive version. Today no canonical "show me what got hidden" view.
   - *Proposed fix*: `dark deprecated [--since <ref>] [--all]` lists all deprecation events with kind / reason / replacement. `dark review` (item 1) surfaces this prominently as a separate section (deprecations get a louder visual treatment than additions). The reviewer must see them, not opt-in.
   - *Harness signal*: doesn't move §6 metrics directly. Catches a class of *silent regressions* — fold into a Phase 4+ "review-caught regressions" metric.

5. **`dark review-mark` — explicit "I reviewed up to here" pointer.**
   - *Problem*: with the current SCM model, there's no equivalent of GitHub's "this PR was reviewed by X." A reviewer who took 20 min through the existing `dark review` TUI can't record that decision; the next reviewer starts from scratch.
   - *Proposed fix*: `dark review-mark <ref>` writes a tiny SCM artifact ("reviewer: <git-config-name>, ts: …, ref: <commit-hash>") into the package tree. The existing `dark review` TUI (per §3.6 #1) defaults to *bounding its window* by the most-recent `review-mark`. Lightweight; no PR concept needed.
   - *Harness signal*: enables the "median reviewer time-to-decision" metric to actually be measured (we know when review starts/ends).

**Cross-references**:
- Items 1, 2, 4 are CLI-surface only. Phase 3-shippable.
- Item 3 (agent-generated SUMMARY.md) is an *agent-prompt* change (§4.7), no Dark code change. Even cheaper to A/B.
- Item 5 needs a small SCM addition (the `review-mark` artifact). Defer to Phase 4 unless cheap.

Out of §3.6 scope: anything resembling a multi-user review queue / PR comments / approval workflow. Single-user local review is the bar (per [plan.md](plan.md) §1: "out of scope: distribution / sync / multi-user collab").

---

## Cross-cutting themes (worth surfacing as meta-principles)

- **Any command an agent might consume should ship `--json`.** Empirically audited iter 18 — see §3.1 item #6 for the full table. 9 commands missing `--json`; 1 (`builtins`) has the worst-of-both UX of coercing `--json` into a search filter. Roll-out is a single Phase 3 wave.
- **CLI-surface fixes are the right first wave.** §3.2 #1–#4, §3.1 #1, #5, #6 are all CLI-only (no language/runtime change). Fast to ship, easy to A/B in the harness.
- **Validate every proposal against `where we're a bit short.md`** before adding it. Several items here are confirmed user-known pains; that gives them priority over speculative additions.

---

## Strengths verified during probes (worth surfacing in the prompt template)

These are existing CLI capabilities the agent doesn't reach for by default. *Cheap* to surface — small additions to `evals/harness/prompts/system.dark.md`. Each was verified by an actual probe.

- **`merge --dry-run` and `rebase --status`** *(verified iter 21)* — pre-flight branch merges without committing. Already cited in §3.4 #4.
- **`traces` family** *(verified iter 6)* — 16+ subcommands including `replay --diff`, `gen-test`, `hotspots`, `inspect`. §3.3 items concretize the agent's use of them.
- **`deprecate --kind {obsolete, harmful, superseded-by}` with `--dry-run` and `undo`** *(verified iter 34)* — full deprecation lifecycle exists. Three kinds, with `superseded-by --replacement` and `harmful --allow-harmful` for opt-in semantics. `view <name>` on a deprecated item shows `⚠ DEPRECATED (kind)` prominently. **Agents should adopt the deprecate discipline**: when refactoring, deprecate the old version with `--kind superseded-by --replacement <new>` instead of just overwriting (which is the §3.2 #3 anti-pattern). Surfacing this in the prompt template costs nothing and yields cleaner code-review trails (§3.6 #4 audit-trail item is downstream).

These are zero-Dark-code-change opportunities. Bundle into the wave 1 prompt-only Phase 3 wave (§7).

---

## Documentation bugs surfaced during probes (track for batch-fix)

Cheap to fix; cumulatively they cost agent tokens. Source: vault `TODOs to Improve AI's Development.md` *plus* probes during this loop.

- **`docs for-ai` `fn` example uses single-quoted arg** *(verified iter 23)*. The doc shows `fn "Darklang.Testing.fib (n: Int64): Int64 = ..."` (one arg) — fails with `Inline function definition required`. Correct form is `fn '<location>' '(<params>): <ret> = <body>'` (two args).
- **`run @<fn>` `@`-prefix missing from `docs for-ai`** *(verified iter 23 + vault TODO)*. The `@` distinguishes "stored fn" from "script file" and is in `help run` but not `docs for-ai`. Result: agents try `run Darklang.X.y` and get a "tries to read a script file" error.
- **`discard --yes` output uses future tense** *(verified iter 23)*. Successful invocation prints `Will discard: 1 function(s)` then exits 0 — looks like a no-op. Should print `Discarded 1 function(s)` past-tense.
- **`commit` requires `DARK_ACCOUNT` env, undocumented** *(vault TODO; not yet probed)*. Fails with "No account set" if env is unset; with arbitrary value, fails with "account 'X' not found." No CLI command to create an account from cold.
- **List-arg with commas → 100-line stack trace** *(vault TODO)*. `[1L, 2L]` (commas) instead of `[1L; 2L]` produces an opaque stack with no syntax-pointer hint. Already covered as the headline case in §3.2 #4 (parse-error suggestions).
- **Inconsistent units in SCM output** *(vault TODO)*. `status` says "6 function(s)"; `discard` on the same state says "Discarded 9 ops". Same state, two different counts shown — agents can't reconcile.
- **`signatures` referenced top-level but lives under `docs`** *(verified iter 18; refined post-loop)*. `dark signatures Stdlib.List` errors `Unknown command`; the actual form is `dark docs signatures Stdlib.List` (subcommand of `docs`). Source comment in `packages/darklang/cli/commands/signatures.dark` marks the command as **CLEANUP-staged** for folding into `tree --signatures` / `view --signatures`, so the right doc fix is to *update for-ai docs to match the eventual surface* (probably `docs signatures` for now, `tree --signatures` after the cleanup).
- **`stdlib overview` referenced but lives under `docs`** *(verified iter 40; refined post-loop)*. `dark stdlib` errors; actual form is `dark docs stdlib`. Source comment in `commands/stdlibOverview.dark` is even stronger: *"Stub: probably delete and fold the entry point into `tree --signatures-summary` or similar."* So the for-ai docs reference is wrong **and** the underlying command is on the path to deletion. Doc fix should point at `docs stdlib` for now and remove all references when the cleanup folds.
- **`find` references are ambiguous between `find-values` (top-level) and `traces find` (subcommand)** *(verified post-loop)*. There's no bare `find` command. The for-ai docs' references probably mean `find-values` (a real top-level command for finding package values by type). Real doc bug.
- **`list --fn` is a `traces list` subform, not top-level** *(verified iter 40)*. `dark list --fn` errors; `dark traces list --fn <name>` works. For-ai docs should clarify the namespace.

These should ride together as a "batch doc-bug + small-UX" Phase 3 wave, separate from the larger structural waves. Cheap to land; produces nothing on the §6 dashboard directly but compounds with every other wave (an agent reading correct docs writes correct code first-pass).

---

## Known runtime gaps (out of §3 scope, but worth tracking)

These are Dark-runtime issues the user has flagged in `where we're a bit short.md`. They're not CLI-surface fixes so they don't fit cleanly in §3.1–§3.6, but the bench should expose them and they should land before Phase 4:

- **No async / concurrency primitives.** Limits a real class of workloads (parallel HTTP, background daemons).
- **No native test support.** Agents roll their own assertion code per project.
- **`httpServerServe` lambda instruction-cache regression** *(probed iter 15 — partially refuted)*. Vault `where we're a bit short.md` warned that handlers using `List.map`, `routeRequest`, etc. fail at runtime. Probe of `Darklang.DemoData.HttpServerTest.router` (which calls `Stdlib.HttpServer.routeRequest`, which uses `Stdlib.List.filter (fun h -> …)`) returned 200 successfully on a first request. The blanket "will fail at runtime" framing doesn't hold on this branch. A separate "Address already in use" race in the hot-reload mechanism does manifest in server logs. Multi-request cache-reuse path remains untested — keep on the watchlist but don't gate Phase 2 HTTP projects on it.
- **Script-port gaps**: arg passing into stored scripts; streaming output (vs wait-until-done); background spawn; signal/SIGINT cleanup hooks; threads/concurrent tasks.
