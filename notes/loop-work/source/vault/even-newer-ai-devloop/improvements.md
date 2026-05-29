# Dark improvements pillar

> What to change in Dark to compress the AI dev loop. See [`plan.md`](plan.md) for harness/metrics; [`projects.md`](projects.md) for the bench. Every improvement here gets evaluated through the §7 Phase 3 A/B protocol — branch + sweep + accept-or-revert.

Organized by where the agent loop spends time. For each item: **problem**, **proposed fix**, **harness signal** (which §6 metric should move). Validation against user-known frictions in `~/vaults/Darklang Dev/02.Project Management/Current Experiment/{where we're a bit short,TODOs to Improve AI's Development}.md` is noted inline.

---

## 3.1 Discovery (agent finding what already exists)

Discovery is the *first* friction every agent loop hits. If the agent can't find what's already in the package tree, it either re-implements badly or wastes a turn searching. Six concrete items, ordered by how early in the loop they bite. Each lives in its own file under [`improvements/`](improvements/) — the body below is a thin index of (problem-summary → harness-signal → file).

| # | Recommendation | One-line problem | Harness signal | Detail |
|---|---|---|---|---|
| 1 | CLAUDE.md auto-loaded template | Agents waste 3–5 turns orienting at session start | Tokens-to-first-fn (T-tier headline) | [improvements/claude-md-template.md](improvements/claude-md-template.md) |
| 2 | `dark search` ranking + `--json` | Wall of unranked ANSI; `Stdlib.Json` buried under `Darklang.JsonRPC.*` matches | Tokens-to-first-relevant-fn | [improvements/search-ranking.md](improvements/search-ranking.md) |
| 3 | "Did you mean" on miss | Empty-result and not-found errors both stop dead, no fuzzy candidate | Rework ratio + first-parse-success | [improvements/did-you-mean.md](improvements/did-you-mean.md) |
| 4 | `dark suggest <natural-language>` | Agents know intent ("parse JSON") but not the module | Tokens-to-first-relevant-fn | [improvements/dark-suggest.md](improvements/dark-suggest.md) |
| 5 | Bare `dark` non-TTY orientation | ASCII banner + ANSI is paid on every cold session | Median tokens for first turn | [improvements/tty-detection.md](improvements/tty-detection.md) |
| 6 | `--json` audit + roll-out | 9 of the most-used commands missing `--json`; `builtins` worst-case (coerces flag to filter) | Median tokens + first-parse-success + constraint-escape | [improvements/json-rollout.md](improvements/json-rollout.md) |
| 7 | `docs for-ai-bundle` — single dense doc | Agent loads 3–5 `docs <topic>` files separately before first fn | Doc-loading tokens + turns-before-first-fn | [improvements/for-ai-bundle.md](improvements/for-ai-bundle.md) |

Out of §3.1 scope: the §3.2 sticky-branch fix (covered there); the §3.3 trace surface (covered there). Out of §3 scope entirely: a dedicated package registry / search-as-a-service (Phase 4+).

---

## 3.2 Authoring (agent writing new code)

This is where agents spend the most output tokens. Five concrete items + 1 architectural analysis, ordered roughly by expected token-impact (largest first). Each lives in its own file under [`improvements/`](improvements/); the body below is a thin index.

| # | Recommendation | One-line problem | Harness signal | Detail |
|---|---|---|---|---|
| 1 | `dark edit` (AST-aware, redesigned) | Resending full fn body for a 1-line change costs ~50 lines/iter on M/L tier | Median tokens per pass + edit-to-first-green | [improvements/dark-edit.md](improvements/dark-edit.md) |
| 2 | Auto-emit diagnostics after every write | Caller-typecheck-broken signal arrives one turn late | Turn count + rework ratio | [improvements/auto-emit-diagnostics.md](improvements/auto-emit-diagnostics.md) |
| 3 | `dark fn` requires `--update` for existing names | Silent overwrites of unread functions corrupt gold-reference invariant | commandExec failures (transient) + pass@1 on iteration | [improvements/fn-update-required.md](improvements/fn-update-required.md) |
| 4 | Parse errors carry suggested fixes + `docs syntax` pointer | Agent sees raw `unexpected ','` with no hint that lists use `;` | Median attempts before successful fn + first-parse-success | [improvements/parse-error-suggestions.md](improvements/parse-error-suggestions.md) |
| 5 | Sticky branch context (default-to-current) | Every command needs `--branch`; agents forget and land on `main` | --branch flags per run + accidental-main-commits | [improvements/sticky-branch.md](improvements/sticky-branch.md) |
| 6 | CLI daemon vs per-call (analysis) | ~0.7-1s cold-start per call; 7-11 s per 10-turn agent loop | Cold-start time per CLI call (§6 supporting) | [improvements/cli-daemon.md](improvements/cli-daemon.md) |

Out of scope for §3.2 (covered elsewhere): traces as test inputs (§3.3), rename safety (§3.4), the `dark publish` command (§3.5).

---

## 3.3 Verification (agent checking what it wrote works)

Dark's trace surface is far more developed than commonly assumed (see [plan.md](plan.md) §2; verified iter 6). The improvement work in §3.3 is about **making the agent reach for these primitives by default** rather than building new ones. Each item is a standalone file under [`improvements/`](improvements/).

| # | Recommendation | One-line problem | Harness signal | Detail |
|---|---|---|---|---|
| 1 | Auto-attach trace on failing `run` | Agent has to discover-and-fetch trace separately after every failure | Fix-iteration delta (§6 #3) | [improvements/auto-attach-trace.md](improvements/auto-attach-trace.md) |
| 2 | `run --replay <trace-id>` shorthand | Bug-fix loop is two manual commands today | Fix-iteration delta (§6 #3) | [improvements/run-replay-shorthand.md](improvements/run-replay-shorthand.md) |
| 3 | Promote `gen-test <trace-id>` in agent docs | Tests not captured even when affordance exists | Trace adoption rate (§6 #4) | [improvements/gen-test-promotion.md](improvements/gen-test-promotion.md) |
| 4 | `hotspots` as built-in review pass | "Ships but slow" results pass rubric silently | Perf-review-at-no-extra-cost | [improvements/hotspots-review.md](improvements/hotspots-review.md) |
| 5 | `--json` for `traces view` | Agents regex the human-formatted trace view | Parsing reliability (downstream of [json-rollout](improvements/json-rollout.md)) | [improvements/traces-view-json.md](improvements/traces-view-json.md) |

Harness signal for §3.3 work overall: **fix-iteration delta** (§6 #3) — Dark's expected biggest win — should rise materially after items 1+2 ship.

Adjacent gap (validated by vault `where we're a bit short.md`): "fix tracing / make it more useful" + a known runtime regression: *"httpServerServe runs handlers in a fresh execution context without lambda instruction caches, so handlers that use lambdas (List.map, routeRequest, etc.) will fail at runtime."* Resolving the regression is a Dark-runtime change (not a §3 CLI-surface item) but the bench should detect it via HTTP-handler project failures in Phase 2.

---

## 3.4 Iteration (agent changing what it wrote)

The agent's *own* edit-rebuild-recheck loop. Distinct from §3.2 (writing new code) and §3.6 (human review). Validated by `where we're a bit short.md` — *"updating and renaming package items"* is on the user's known-gaps list. Four concrete items + cross-references; each item is a standalone file.

| # | Recommendation | One-line problem | Harness signal | Detail |
|---|---|---|---|---|
| 1 | `dark rename <old> <new>` with auto-update of callers | Renames cost 4-step manual workflow today | Median tokens per pass + rework ratio | [improvements/dark-rename.md](improvements/dark-rename.md) |
| 2 | `dark uncommit` / `dark revert <commit>` | `dark discard` doesn't unwind committed changes | Edit-to-first-green + rework ratio + abandonment | [improvements/dark-uncommit-revert.md](improvements/dark-uncommit-revert.md) |
| 3 | `dark since <ref>` — session-scoped change view | Agents lose track of their own working set | Turn count per project | [improvements/session-changed-view.md](improvements/session-changed-view.md) |
| 4 | Surface `merge --dry-run` / `rebase --status` in prompt | Existing strengths agents don't reach for | Cross-branch agent abandonment | [improvements/merge-rebase-prompt-surfacing.md](improvements/merge-rebase-prompt-surfacing.md) |

**Cross-references (covered elsewhere; don't duplicate)**:
- Sticky branch context: §3.2 [sticky-branch.md](improvements/sticky-branch.md).
- Deprecation visibility for review: §3.6 #4.
- Editing existing fn bodies: §3.2 [dark-edit.md](improvements/dark-edit.md).
- "Read before update" guard: §3.2 [fn-update-required.md](improvements/fn-update-required.md).

Out of §3.4 scope: distributed merge / pull / push (deferred per [plan.md](plan.md) §1 "out of scope: distribution / sync / multi-user collab").

---

## 3.5 Sharing / running the result

**The promise the user wants** (per [plan.md](plan.md) §1): Claude builds a thing in Dark; you immediately share it with a friend; the friend runs it on their laptop/phone/different computer with no setup. **What exists today** (verified iter 10): `./scripts/run-cli export-seed` extracts a minimal `seed.db` — not a runnable app. The §2 "single distributable" bullet was demoted to aspirational because of this gap. The fix is the most user-visible improvement on this whole list. Each item below is a standalone file.

| # | Recommendation | One-line problem | Harness signal | Detail |
|---|---|---|---|---|
| 1 | `dark publish <project> --out <path>` | Headline missing tool — friend can't run agent's output | §6 #18 "Time to friend-runnable artifact" | [improvements/dark-publish.md](improvements/dark-publish.md) |
| 2 | `dark publish --target wasm` | Phone-friend = URL share | Deferred (Phase 4+) | [improvements/dark-publish-wasm.md](improvements/dark-publish-wasm.md) |
| 3 | Reproducible builds | Hidden FS state breaks "byte-equal between author + friend" | Byte-equal artifact CI gate | [improvements/reproducible-builds.md](improvements/reproducible-builds.md) |
| 4 | `dark export` / `dark import` | "Friend has Dark" should be one-line each side | Collaboration friction | [improvements/dark-export-import.md](improvements/dark-export-import.md) |

Out of §3.5 scope: discovery / package registry / multi-machine sync (user explicitly deferred distribution).

Harness signal for §3.5 work: **§6 #18 "Time to friend-runnable artifact"** *(defined iter 33 / activates when wave 5 ships)* — measures the actual user-visible delay from "agent says done" to "friend can run it." Promotes the friend-can-run goal from [plan.md](plan.md) §1 (ambitious) to §6 (trackable).

---

## 3.6 Human review of agent-built Dark code

**The pain** (validated by `where we're a bit short.md` — *"review: no good way to review code"*): an agent ships a wave of changes; a human needs to verify them in 5–15 minutes before merging. The agent likely deprecated a few things, renamed others, restructured a module. Existing `dark log` / `dark show` give the *what*, not the *implications*. Five concrete items, ordered by reviewer time-on-task they save. Each is a standalone file under [`improvements/`](improvements/).

| # | Recommendation | One-line problem | Harness signal | Detail |
|---|---|---|---|---|
| 1 | `dark review` — augment existing TUI | TUI exists but no `--json` / `--since` / `--include-traces` for headless callers | Median reviewer time-to-decision (Phase 4+) | [improvements/dark-review.md](improvements/dark-review.md) |
| 2 | `dark show <hash> --json` | Diff isn't machine-readable | Indirect (enables CI hooks + dark-review composition) | [improvements/dark-show-json.md](improvements/dark-show-json.md) |
| 3 | Auto-generated SUMMARY.md at end of run | Reviewer reads code with no goal/approach context | Median reviewer time-to-decision | [improvements/agent-summary.md](improvements/agent-summary.md) |
| 4 | Audit trail for `deprecate` / `discard` moves | Hidden deprecations slip past review (security) | Review-caught regressions (Phase 4+) | [improvements/deprecated-audit.md](improvements/deprecated-audit.md) |
| 5 | `dark review-mark <ref>` | No "reviewed-up-to-here" pointer for next reviewer | Enables time-to-decision metric measurement | [improvements/review-mark.md](improvements/review-mark.md) |

**Phase mapping**:
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
