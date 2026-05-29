# Plan: Dark as the optimal AI coding target

> **Status: living document.** Iterated on by the 5-minute loop. See [`README.md`](README.md) for the map; [`improvements.md`](improvements.md) for §3 (the Dark improvement backlog); [`projects.md`](projects.md) for the bench project catalog; [`research-log.md`](research-log.md) for the iteration log.

---

## 1. Goal

A user opens a fresh Claude Code session in any directory and says:

> "Using only Darklang, build me \<a thing\>."

…and the agent does a great job. Better than building the same thing in TypeScript or Python. "Better" measured by:

- **Tokens** to first working version, and to "done"
- **Wall time** to first working version, and to "done"
- **Output artifact size** on disk (single distributable executable + data, ideally)
- **Runtime perf** of the resulting program on representative workloads
- **Pass rate** against a per-project rubric (functional spec + smoke tests)
- **Edit cost** of a small follow-up change ("add a /healthz endpoint", "add dark mode")

Optimizing for the agent loop also tightens the loop for humans (especially during code review).

Out of scope for now: distribution / sync / multi-user collab. Local single-machine is enough.

---

## 2. Why Dark plausibly wins (the thesis to prove)

Things Dark already has that other stacks don't:

- **Persistent package tree, amortised across projects** *(stress-tested 2026-05-02)*: no `node_modules`, no `requirements.txt`, no Docker layers. The "thing under construction" is a SQLite-backed package tree, not a directory of files. Verified footprint on this branch: Debug build is ~72 MB across 121 files (dominated by FSharpPlus.dll 7.7 MB, LibTreeSitter.dll 4.7 MB, FSharp.Core.dll 2.3 MB); the actual `Cli` launcher is 77 KB. Persistent tree (`rundir/data.db` + `seed.db`) is ~54 MB. **Total Dark install: ~125 MB.** That's *not* dramatically smaller than a Python venv with FastAPI (~50–150 MB) or even a single Node.js project's `node_modules` (~200 MB for a moderate project), but the framing matters: **install once, build many.** Adding a 100th project to a Dark install costs ~zero on disk; adding a 100th Node project costs another `node_modules`. Agents are great at structured CRUD over the SQLite-backed tree; less great at coordinating filesystem layouts and per-project dep installs.
- **Live programming workflow** *(stress-tested 2026-05-02 / iter 23)*: `fn` to define → `run` to test → `commit` to save. **Confirmed**: no build step between `fn` and `run`. Verified by creating `Darklang.Testing.double (x: Int64): Int64 = Stdlib.Int64.multiply x 2L` and immediately running `run @Darklang.Testing.double 5L` → `10` (with the trace showing internal exec at ~2 ms). **Caveat the harness must price in**: each `dark` CLI invocation has a ~0.7–1.1 s cold-start (fresh .NET process per call). A 10-turn agent loop pays ~7 s of pure CLI overhead, which dominates wall time at small budgets. Memory `feedback_no_json_or_dotnet_prewarm` deliberately defers a CLI-daemon optimisation; harness Phase 1 should measure cold-start time as a supporting metric to make this cost visible. Three companion doc bugs surfaced during the probe (matched against vault `TODOs to Improve AI's Development.md`): (i) `docs for-ai`'s `fn "Darklang.X.y (…): T = …"` example uses a single quoted arg — it actually wants two args (`fn '<location>' '(<params>): <ret> = <body>'`); (ii) `run @<fn>` `@`-prefix is in `help run` but missing from `docs for-ai`; (iii) `discard --yes` prints `Will discard: 1 function(s)` (future tense) even when it succeeds — should be `Discarded`.
- **AI-shaped CLI surface already** *(audited end-to-end against `packages/darklang/cli/core.dark` — final validation pass post-loop)*: the canonical top-level command list is `quit, help, config, install*, version, nav, ls, back, clear, run, eval, scripts, view, tree, search, deps, val/let, fn, type, hash, status, log, commit, discard, show, branch, rebase, merge, builtins, find-values, agent (ai), serve (http-server), docs, undo, deprecate, delete, db, outliner, export-seed, traces (trace), review, views`. AI-relevant subset: `docs for-ai`/`for-ai-internal`, `tree`, `search <term>`, `view <name>` (with `--with-trace <traceId>` overlay), `traces …` (rich subsurface — 16+ subcommands; see §2 trace bullet), `find-values <type-path>` (locate package values by type), `agent` (in-Dark AI workflows; see iter-31 CodeAgent survey), `review` (interactive TUI for branch changesets — *not* the missing tool §3.6 #1 originally implied; see [improvements.md](improvements.md) §3.6 for the augmentation proposal). Subcommands of `docs`: `docs signatures <module>`, `docs stdlib`, and the full topic list (`syntax`, `types`, `errors`, `cli`, `scm`, `deprecation`, etc.). Most languages don't ship anything like this. **What looks like a top-level command but isn't**: bare `signatures` (only `docs signatures`), `stdlib overview` (only `docs stdlib`), `find` (the actual top-level command is `find-values`; bare `find` is the `traces find` subcommand), `list --fn` (only `traces list --fn`). Source comments in `commands/signatures.dark` and `commands/stdlibOverview.dark` mark both as cleanup-staged for folding into `tree --signatures` / `view --signatures` — so the bare forms may consolidate into existing commands rather than be added separately. Doc-bug tracked in [improvements.md](improvements.md) "Documentation bugs surfaced during probes."
- **Built-in tracing — far richer than the scaffold initially claimed.** Verified 2026-05-02 by running `./scripts/run-cli help traces`: every `run`/`eval`/HTTP request stores a trace, and the CLI surface around them is already substantial. Trace IDs are ULIDs with unambiguous-prefix lookup (`traces view fe62` → suggests prefixes when ambiguous; `traces view fe6216` succeeds). Subcommands available *today*: `list` (with `--fn`/`--route`/`--json` filters), `view`, `tail`, `follow` (NDJSON streaming for live debugging), `stats`, `find <pattern>` (substring across recorded values), `hotspots` (per-fn timing aggregated across the last N traces), `export`/`import` (portable JSON for sharing), `replay <id> --diff` (re-execute and *diff* against the recorded output — i.e. regression-testing built in), `gen-test <id>` (generates a testfiles skeleton from an HTTP trace), `inspect <id>` (annotated source with trace overlay), `values <id>` (per-AST-node values for debugging). View flags include `--depth`, `--no-builtins`, `--no-stdlib`, `--filter <pattern>`, `--slow-ms <n>`. Sample `traces view` output is small and structured (handler / time / input / function-calls) — well-suited to feeding back into the agent loop without flooding the context. The competing `console.log`-and-rerun loop in TS/Py simply has nothing comparable. **This is one of Dark's biggest under-leveraged advantages — see [improvements.md](improvements.md) §3.3 for the agent-loop changes that surface it.**
- **Deprecation as a first-class concept** *(stress-tested 2026-05-02 / iter 34)* — *richer than the scaffold initially claimed.* Verified end-to-end: created `Darklang.IterTest.disposable`, ran `deprecate fn Darklang.IterTest.disposable --kind obsolete --yes`, then confirmed (a) `search disposable` returns "No results found", (b) `tree Darklang.IterTest` shows empty, (c) `ls Darklang.IterTest` shows empty, (d) `view Darklang.IterTest.disposable` *still works* and prints a prominent `⚠ DEPRECATED (obsolete)` header before the source. Three deprecation *kinds* exist (`obsolete`, `harmful`, `superseded-by`) — `harmful` halts the runtime unless `--allow-harmful` opt-in, `superseded-by` requires `--replacement`. `delete` is sugar for `--kind obsolete --force`; `undo` reverses a WIP deprecate; `--dry-run` previews. The memory rule (deprecated items hidden from default views) is exactly true; the *kind* metadata is the bonus that the original framing missed. **For agents**: this is a working-set discipline tool — agents that follow the deprecation discipline implicitly produce code that looks freshly-current. Surface in the agent prompt template (§4.7) and in §3.6 review tooling so reviewers see what's been hidden.
- ~~**Single distributable**: a finished project ships as one file.~~ **Aspirational, not real today** *(stress-tested 2026-05-02)*. `./scripts/run-cli help` shows only `export-seed` (which produces a minimal `seed.db`, not a runnable app). There's no `dark build my-project --out app` command that emits a self-contained binary. The runtime *is* one file (the `Cli` launcher loads dlls from a fixed directory), so the path to single-file distribution is short — but the actual command doesn't exist. See [improvements.md](improvements.md) §3.5 for the proposal.
- **Telemetry already piping to `rundir/logs/telemetry.jsonl`**: CLI and test events land in one greppable JSONL stream. Verified on this branch (`remove-bwdserver-in-favor-of-dark-impl`, 2026-05-02): file is ~5 MB / ~56 k lines, line shape is `{"event": str, "ms": int, "us": int, "wall": ISO8601, "ctx": {}}`. Confirmed event names: `cli.createPM`, `cli.execute`, `cli.growIfNeeded`, `cli.pmInit`, `cli.total`, `commandExec`, `httpserver.{listening,request,serve,serve.named,shutdown}`, `seed.{applyOps,applyOps.count,evaluateValues,generateRefs,growIfNeeded,walCheckpoint}`, `tabComplete`, `test.suite.{start,end}`. Two caveats the harness must work around: (a) `ctx` is empty in every sample line — there's currently no per-event run/project correlator, so the harness has to inject one (see §4.2 plumbing); (b) no `build.compile.*` events on this branch (they're scoped to a separate AI-dev-loop infra PR), so the harness derives "build success/fail" from process exit code instead of a telemetry event.

Things Dark does *not* yet have that competing stacks do (improvement backlog — synthesised from `~/vaults/Darklang Dev/05.Implementation/AI/Agent Next Steps.md` + repo memory + `~/vaults/Darklang Dev/02.Project Management/Current Experiment/where we're a bit short.md`):

- **No diff-based editing primitive.** `dark fn` rewrites the entire function body — no equivalent of Claude Code's Edit tool's `old_string`/`new_string`. Single-line tweaks to a 50-line function cost 50 lines of output. Affects metric #2 (median tokens) and #5 (edit-to-first-green) most. (Validated by user-known friction: "multi-line editing" + "update existing package entities" in vault TODO.)
- **No automatic post-write diagnostics.** After `dark fn` succeeds at parsing, the agent has to *explicitly* check whether anything downstream broke (`dark deps`, `dark status`, `dark run`). No "you just changed X, here are the 4 callers that no longer typecheck" feedback loop.
- **No "read-before-edit" guard.** Agents can `dark fn Foo.bar = …` without ever calling `dark view Foo.bar` first; updates routinely overwrite work the agent didn't realise existed. Mainstream IDEs / Claude Code's Edit tool enforce this implicitly.
- **No trace-as-test affordance.** Dark *captures* traces but the agent has no terse way to say "this trace is the failing case, replay it against my new version." (See [improvements.md](improvements.md) §3.3 backlog.)
- **No agent-shaped error vocabulary.** Parse errors are message-only — no "did you mean", no auto-suggested syntax fix, no `docs syntax` pointer in the error itself. (Validated by user-known friction: vault TODO calls out `[1L, 2L]` (commas) producing a "100-line stack trace with no hint about the separator.")
- **No project-level instruction file the agent picks up.** Mainstream agents read `CLAUDE.md` / `AGENTS.md` from the project root automatically; Dark has no equivalent (the `darklang.com` repo doesn't ship one and the for-ai docs are a separate command the agent has to know to call).
- **Two-pass build for some type changes** (memory: `feedback_dark_type_change_two_builds`). First build has stale `package-ref-hashes.txt`; agent has to know to touch + rebuild. Surface area for spurious "build broke" detours.
- **Branch context doesn't persist between CLI invocations.** Agents have to thread `--branch <name>` on every call (per `docs scm`); easy to forget and silently land work on `main`. (Validated: vault TODO "No non-interactive `commit`".)
- **No async / concurrency primitives.** No `async`, `await`, threads, channels (per the [project-survey](#references) §2 inventory). A parallel downloader is not directly expressible. Limits the bench's ability to test a real class of workloads.
- **No native test support.** (Vault: "where we're a bit short.md" — "a native way to support tests in dark.") Agents end up rolling their own assertion code per project.
- **Familiarity gap in agent training data.** Agents have seen orders of magnitude more TS/Py than Dark. Convex's pitch ([convex.dev/ai](https://www.convex.dev/ai)) is explicit: "TypeScript … can be generated by AI with exceptional accuracy" — they're banking on familiarity. Dark's first-pass code quality will be worse than TS/Py for the same prompt purely from data scarcity. Tight feedback loops (traces, diagnostics) mitigate but don't eliminate this — the Dark harness expects a *fix-iteration delta* (§6 #3) advantage, not necessarily a *pass@1* advantage in early sweeps.
- **No reactive queries.** Convex's killer feature is built-in query reactivity. Dark doesn't have this today. Out of scope for the eval bench (none of the §4.1 starter projects need it), but real for any "build me a chat app" task.
- **No MCP server / no bundled "agent skills."** Both Convex (their MCP integration) and Instant DB (their "official skills" callout — [instantdb on HN](https://news.ycombinator.com/item?id=47707632)) ship pre-built agent integrations. Dark has neither. A bundled set of slash-command templates (`/dark-trace-debug`, `/dark-deprecate-sweep`, `/dark-init`) would compress common agent workflows; flagged as a candidate Phase 4+ improvement (Phase 3 boundary, see §7).

### 2.1 Competitive landscape — what Dark is up against (surveyed 2026-05-02)

The "AI-coded apps" space is no longer empty; two platforms are explicitly positioning themselves the same way:

- **[Convex](https://www.convex.dev/) / [convex.dev/ai](https://www.convex.dev/ai)** — "the database designed to be generated." Pure TypeScript across schemas, queries, auth, APIs; reactive queries are built into the engine; ships an "AI Agent" component with built-in memory + vector search + long-running workflows ([convex.dev/components/agent](https://www.convex.dev/components/agent)). Open source backend; cloud + self-hosted. Their explicit competitive moat is *familiarity* — TypeScript training data is everywhere.
- **[Instant DB](https://www.instantdb.com/) / [architecture essay](https://www.instantdb.com/essays/architecture)** — "the best backend for AI-coded apps." Multi-tenant Postgres + a Clojure sync engine; relational DB + auth + storage + presence + streams in one prompt. Their pattern: tell users to prepend `Read getadb.com first` to any AI prompt and the agent picks up a database. Ship "official skills" for agent UX. Open source.

**Implications for Dark**:

1. **Familiarity is a structural disadvantage** — see new §2 bullet. Our improvement pillar must lean disproportionately on tight-feedback features (traces, diagnostics) that compound with the *agent's* abilities rather than competing with TS volume in training data.
2. **Both competitors run in the cloud by default.** Dark's local-first single-binary positioning is genuinely differentiated — but only if the eval harness actually exercises it (the `url-shortener-cli` Phase 1 project does; we should add 2–3 more local-first-only projects in Phase 2).
3. **MCP / bundled skills is becoming table stakes — but for ecosystem reach, not for the bench.** Adding a Dark MCP server is a Phase 4+ deliverable (resolved iter 11, see §7 Phase 3 boundary). The bench measures Dark-as-primary-platform; MCP measures Dark-as-tool-from-other-platforms. Complementary, but different experiments — don't conflate.
4. **Reactivity matters for some app classes** (collab tools, live dashboards). Out of scope for the bench but real for vision §1's "build me a chat app" framing — see §5 open question on whether to keep that framing for the long term or narrow it.

---

## 4. Eval harness pillar — measure that we're getting better

### 4.0 Harness layout & language (decided 2026-05-02)

**Decision: hybrid Python wrapper + language-native rubrics.** The wrapper that orchestrates Claude Code invocations, captures token usage, and writes metrics is a small Python package. Each project's rubric is written in the same language as the artifact under test (Dark project → Dark rubric; TS project → TS rubric; Py project → Py rubric). The rubric never imports the artifact — it shells out and inspects stdout / HTTP responses.

**Why not build the harness in Dark itself?** Three reasons:

1. **Don't depend on what you're measuring.** If Dark has a regression that breaks the eval framework, we lose visibility precisely when we most need it.
2. **Bootstrapping cost.** The harness needs HTTP polling, parallel process management, JSON parsing of Claude Code transcripts, subprocess timeouts, and parquet/jsonl writing — all idiomatic in Python today, all on the [improvements.md](improvements.md) backlog.
3. **Avoid a `dark eval` subcommand creating circular pressure** (we'd be tempted to ship features in Dark just so the harness runs, conflating "feature for users" with "feature for the bench").

**Why not build the harness in TS?** Same arguments work, plus Python has lighter-weight subprocess and parquet stories. TS would also be fine; pick Python for taste.

**Future migration path.** Once the harness has been stable for ≥3 sweeps and Dark has shipped the [improvements.md](improvements.md) §3.2 improvements, port the wrapper to Dark as a dogfooding milestone. Don't do this earlier — it's not the same kind of test as building greenfield projects, and conflates bench-as-Dark-project with bench-as-measurement-tool.

**File layout** (under `evals/` at repo root):

```
evals/
  bin/                    # exit-code-tracking dark wrapper, claude-code launcher
  harness/                # the Python wrapper (this is what `python -m harness` runs)
    main.py               # CLI: `sweep`, `report`, `verify-rubrics`, `single`
    runner.py             # spawns claude-code, captures `--output-format json`
    metrics.py            # parses transcript + telemetry.jsonl, writes metrics.json
    report.py             # produces evals/report.md per sweep
  projects/
    <project-name>/
      spec.md             # the only thing the agent sees (frontmatter + behaviours)
      rubric.{dark,ts,py} # one per language; reads artifact path/URL from argv; exits 0/1
      driver.sh           # representative-input perf driver
      gold/
        dark/  ts/  py/   # gold reference, human-reviewed
  runs/<sweep_id>/<run_id>/
    transcript.json       # claude-code session output
    telemetry.jsonl       # per-run rundir copy
    artifact/             # the agent's output
    metrics.json
    cli-invocations.jsonl # exit-code-tracking dark wrapper output
  results.jsonl           # one line per run, append-only across all sweeps
  report.md               # most recent sweep summary
  notes/                  # ephemeral loop notes
```

**Spec file format** (`evals/projects/<name>/spec.md`):

```markdown
---
title: url-shortener-cli
tier: S
modules: [Stdlib.DB, Stdlib.Crypto, Stdlib.Cli]
languages: [dark, ts, py]
---

# Description
One paragraph the agent reads. No implementation hints.

# Behaviours
- `add <url>` returns a 6-char slug …
- `get <slug>` prints the URL or exits 1 …
- persists across invocations …
- slug collisions handled …
- invalid URLs rejected …

# Smoke commands
- add https://example.com
- get <slug-from-prior>
```

The agent sees only `spec.md`. The rubric (which encodes the behaviour checks operationally) is hidden in the same directory. **Agents that read the rubric file directly are detected and the run is invalidated** — wrapper enforces via filesystem-permission-stripped working directory (`spec.md` symlinked into the agent's cwd; rubric is not).

### 4.1 Bench projects

See [`projects.md`](projects.md) for the full project catalog (Phase 1 starter set + the wider candidate pool drawn from the AI-generated Darklang CLI Project Survey).

Top-level shape: ~100 projects, 4 tiers (T = trivial, S = small, M = medium, L = large), balanced across categories. Reference-implementation policy: an agent (Claude Code, no time budget cap) writes the `dark/`, `ts/`, and `py/` reference impls; a human spends ≤30 min reviewing each before it's accepted as the gold copy. The rubric runner is language-agnostic — shells out to the artifact and inspects stdout / HTTP, never imports the implementation.

### 4.2 Per-run metrics (collected automatically)

For each `(project, language, agent_config, attempt_n)` we record a `run_id`. All metrics below correlate by `run_id`.

**Plumbing prerequisites (verified against `rundir/logs/telemetry.jsonl` on 2026-05-02):**

- The current `ctx` field on every telemetry event is empty `{}`. **Resolved iter 5 / §4.0**: the harness uses **per-run dir** — each run sets `DARK_RUNDIR=evals/runs/<sweep_id>/<run_id>/` and gets its own fresh `telemetry.jsonl`. No code change to Dark required. If we later need concurrent runs against a shared rundir, upgrade to context-stamping via a `DARK_TELEMETRY_CTX` env var (one small Dark code change, deferred).
- Wall-time end-to-end is `cli.total.wall` of the last line minus the first event's `wall` — clean now that runs are isolated per-rundir.

| Metric | How collected (concretely) |
|---|---|
| Input / output / cached tokens | Claude Agent SDK / Claude Code session JSON output. For Claude Code: `claude --output-format json` and parse `usage.{input_tokens,output_tokens,cache_read_input_tokens,cache_creation_input_tokens}` |
| Tool-call count, by tool | Same Claude Code JSON: count entries in `messages[].content[]` where `type == "tool_use"`, group by `name` |
| Wall time, end-to-end | Wrapper records `start_ts`/`end_ts` around the agent invocation |
| Wall time spent in Dark CLI vs agent thinking | Sum of `cli.total.ms` from telemetry.jsonl ÷ wall time |
| Number of `dark` invocations during the run | Count `cli.total` events |
| Number of failed `dark` invocations | Count `commandExec` lines where exit ≠ 0 (event currently has no exit field — wrapper captures exit code per invocation in `cli-invocations.jsonl`) |
| Final artifact size on disk | `du -sb evals/runs/<run_id>/artifact/` |
| Build success / fail | Process exit code of `dark` build subcommand (no `build.compile.*` telemetry on this branch — see §2 caveat) |
| Test pass rate | Rubric runner exit code + parsed pass/fail count |
| Runtime perf | Driver script `evals/projects/<project>/driver.sh` that calls the artifact N times, records median + p95 wall ms |
| Lines of code | Dark: `dark list --fn '<project>.*' \| wc -l` for fn count + sum of fn body line counts via `view`. TS/Py: `tokei` or `cloc` |
| Functions created / used / deprecated | `dark status` + parse; deprecation count from `dark list --deprecated` |
| Edits to first green | Count of `commit` events between run start and first all-rubric-pass |
| Rework rate | (commits after first green) / (commits up to first green) |
| Agent abandonment | Wrapper detects: agent stop reason ∈ `{stop_sequence, end_turn}` *and* rubric not yet green |
| HTTP request handling (if applicable) | `httpserver.request` events from telemetry.jsonl, count + p95 `ms` |

### 4.3 Cross-language baselines

Same 100 projects in TypeScript, Python, Dark. Same agent (Claude Code) given the same task prompt with only the language-specific tooling exposed (e.g. only `dark`, only `npm/node`, only `python/uv`). Compare metrics. Track the gap over time.

We adopt MultiPL-E's design explicitly: **one canonical spec per project, language-agnostic tests** (rubric runner shells out to the artifact, never imports it). This keeps cross-language comparisons honest. Source: [MultiPL-E paper / repo](https://github.com/nuprl/MultiPL-E) — translates HumanEval + MBPP into 18 languages with the same canonical tests. Their finding that Java consistently outperforms Rust on the same spec is a useful prior: language-shape friction is real and measurable, even when the prompt is identical. Our harness exists to make that friction visible for Dark and then drive it down.

#### 4.3.2 Constraint-mode policy (decided iter 36)

The bench can run in one of two constraint modes. Each mode is a *separate measurement series* — its rows never aggregate with the other mode's rows. The §6 north-star is reported per-mode.

**Strict mode (Phase 1–3, headline)**: agent gets only the language-specific tooling. For Dark: `dark` (via `dark-wrapped`). For TS: `node`, `npm`. For Py: `python`, `uv`. **No bash, no shell builtins, no `curl`/`jq`/`grep`/`tar`.** Wrapper enforces by sandboxing the agent's working directory + a `PATH` whitelist. Escape attempts (any invocation of `bash`, `sh`, `zsh`, `/bin/*`, `awk`, `sed`, `cut`, `xargs`, …) are counted as §6 #14 diagnostic but the call is *rejected* (agent gets a "command not allowed in strict mode" stderr).

**Realistic mode (Phase 4+, secondary)**: agent gets the full agentic-coding-realistic toolset — language-specific tools *plus* common Unix utilities. Runs in a different sweep namespace (`results.jsonl` row tagged `mode: "realistic"`). Reported as a *parallel* set of headline metrics in `evals/report.md`.

**Why both — and why strict is headline**:

- **Strict mode measures what we're actually trying to learn**: when an agent uses Dark *as a primary platform*, how does it perform? If the agent shells out to `jq` for JSON munging, we're measuring `jq`, not Dark's `Stdlib.Json`. The whole point of [improvements.md](improvements.md) §3 is to make Dark's stdlib worth reaching for; bash-fallback would let us mask that with bandaid composability.
- **Realistic mode measures what users actually do**: most people running Claude Code give it bash. If our headline numbers tell a Dark-wins story but realistic-mode tells a "agents shell out 80% of the time anyway" story, the headline is misleading. Realistic mode is the honesty check.
- **Per-wave A/B** (per §7 Phase 3) **only runs in strict mode**. We're isolating Dark improvements; bash-fallback noise would swamp the signal.
- **Cross-mode comparison is a Phase 4+ analysis** — "how much does the strict-vs-realistic delta shrink as Dark improves?" If the §3 backlog lands successfully, the delta should narrow because Dark itself becomes good enough that agents *don't* reach for bash. That's the win-condition.

**What changes in the harness**:

| Setting | Strict mode | Realistic mode |
|---|---|---|
| `PATH` whitelist | Language tools only | Language tools + `/usr/bin/*`, `/bin/*` |
| Agent prompt template | Mentions only language tools | Mentions both: "you may use bash + standard utilities, or the language-native tools — whichever fits" |
| `mode` field in results.jsonl | `"strict"` | `"realistic"` |
| Constraint-escape attempts (§6 #14) | Counted + rejected (call fails) | Not applicable |
| Phase 3 A/B waves | All run in strict | Don't run waves in realistic — too much noise |

**Operational cost**: strict mode requires the wrapper to sandbox `PATH`. Realistic mode is the cheaper default — it's also more brittle for cross-language comparison (different bash availability across platforms). Strict mode locks the comparison surface; realistic mode reflects user reality. Run strict for *every* sweep; run realistic *quarterly* in Phase 4+ as a sanity check.

#### 4.3.1 Framework-pinning policy (decided iter 16)

A natural worry: TS/Py agents can pull in an arbitrary library (`hono`, `fastapi`, `flask`, …). If we let them, sweep-to-sweep variance balloons (different deps, different bug surfaces, different versions). If we *pin* a framework, we artificially handicap the languages that gain real-world advantage from their ecosystems — which is precisely the comparison Dark wants to win on.

**Decision: hybrid pinning.**

1. **Pin the runtime version.** `node:22-alpine` and `python:3.13-slim` per sweep (already noted in §8 risk "Reference implementations rot"). Locked at the sweep_id boundary.
2. **Pin a dependency-snapshot timestamp, not a framework list.** Agents can `npm install <whatever>` and `uv add <whatever>` freely; the wrapper proxies through a snapshot of the registry pinned to the sweep date (e.g. via a local mirror or `--index-url` override). This means a 2026-05 sweep sees the 2026-05 versions of every package, not whatever's latest at re-run time. Re-baseline quarterly when the snapshot moves.
3. **No framework allowlist.** Don't dictate Hono vs Express, FastAPI vs Flask, Click vs argparse. The agent picks what it would pick in real life. We *measure* what got picked (parse `package.json` / `pyproject.toml` after the run, log to `metrics.json`) — useful diagnostic, not gating.
4. **Dark side**: pinned by `dark_sha` already (§4.5 sweep_id). The package tree at that SHA is the snapshot — agent gets whatever stdlib + community packages exist there, no external-library equivalent.
5. **The `tier=L` exception**: large projects may need a curated dep list because the agent will otherwise spend ~all its tokens reading docs for a 50-MB framework. For projects flagged `framework_hint: <name>` in `spec.md` frontmatter, surface that hint to the agent in the task prompt. Sparingly — most projects shouldn't need it.

**Why not simpler ("just pin one framework per language")**: the project survey in `projects.md` spans pure-CLI tools (no framework needed), HTTP services (multiple plausible framework choices), TUI apps (still different ecosystem), data-munging, etc. A single per-language framework pin would mismatch most projects.

**Why not full freedom (no snapshot)**: pip and npm version drift would mean sweeps in February vs August disagree purely from upstream churn. We'd be measuring noise. The snapshot pin makes that drift explicit and quarterly.

**Why no frameworks for Dark**: Dark doesn't have an external package ecosystem the agent can pull from. The `dark_sha` *is* the snapshot. If/when Dark grows a community package registry (Phase 4+ territory), revisit this.

The harness records, per run: runtime version, registry snapshot, top-level deps installed (TS/Py only), and Dark SHA. That's the language-eco footprint — diagnostic-only metric, never the headline.

### 4.4 Parallelism / sandboxing

- Each run isolated in its own per-run dir (see §4.0 / §4.2): `evals/runs/<sweep_id>/<run_id>/` holds the full transcript, telemetry.jsonl, artifact, metrics.json.
- Stable per-project SQLite seed: copy the package tree, don't mutate the source.
- N parallel Claude Code instances (configurable `-j` flag; default `nproc/2`). **Phase 1 ships with `-j 1` — concurrency lands in Phase 2** (per §7).

#### 4.4.1 HTTP-project startup reliability *(finding from iter 26)*

Probed iter 15 (first attempt) + iter 26: starting `dark serve <router> --port <port>` in non-interactive background mode is **flaky from a non-TTY shell**. Two attempts to start the canonical demo router for a multi-request probe failed silently — server process consumed but wrote no output to stdout/stderr, didn't bind the port. A *third* attempt at iter 15 with a different invocation pattern succeeded. The mode-switching is non-deterministic from the loop's vantage point.

**Implication for the harness**: when the bench runs Phase 2 HTTP projects (`http-healthz`, `webhook-echo`, `paste-bin`), naive `subprocess.Popen([..., "serve", ...])` will inherit this flakiness. Required harness-side measures:

1. **Robust readiness check** — poll the listening port (`socket.connect_ex`) every 200 ms with a generous timeout (e.g. 30 s) before declaring the server alive. *Don't* poll the server log; the log doesn't always get written before the port is bound.
2. **Retry-on-startup-failure** — if readiness check times out, kill the server, wait, and retry up to 3 times. Treat as `agent_abandoned: false, harness_flake: true` in metrics so flakes don't pollute the §6 dashboard.
3. **TTY wrapping (last resort)** — if retries persist, wrap the serve invocation under `script(1)` or a pty allocator (`unbuffer` from expect). The iter-15-success pattern suggests TTY-detection differences in `dark serve`'s startup path.
4. **Hot-reload disable** — the `[HttpServer] Listening on port X (hot-reload: ...)` line in iter 15 logs revealed a separate "Address already in use" race from hot-reload trying to start a second listener. Disable hot-reload in bench mode (`DARK_NO_HOT_RELOAD=1` env or equivalent flag — verify availability in a future iteration).

This is a Phase 2 deliverable, not Phase 1 (Phase 1 deliberately skips HTTP). Track it as risk #8 in §8.

### 4.5 Storage + reporting

- Every run: a row appended to `evals/results.jsonl`.
- `evals/report.md` regenerated per sweep: per-project results, language deltas, regressions vs prior sweep, biggest wins.
- Sweep IDs are `(dark_sha, agent_version, prompt_template_hash)` triples so we can plot improvement over time and tie regressions to a specific input change.

#### 4.5.1 Retention policy (decided iter 27)

**Disk math**: per run ≈ 1 MB (transcript + telemetry + artifact + metrics + cli-invocations). At 100 projects × 3 languages × pass@2 (~2 attempts) × weekly sweep cadence in Phase 4, that's ~600 runs/week ≈ 600 MB/week ≈ ~30 GB/year. Compressible portion is large (transcripts and telemetry both deflate ~10×).

**Policy** — three retention tiers, automated by a wrapper subcommand:

1. **`evals/results.jsonl` — keep forever.** The metrics-history row is what makes the bench valuable across years. Each row is a few hundred bytes; a decade of weekly sweeps is on the order of MBs. Never delete. Append-only.
2. **Per-run dirs in `evals/runs/<sweep_id>/<run_id>/` — keep uncompressed for the most-recent 4 sweeps**, then compress to `evals/runs/<sweep_id>.tar.gz` (single tarball per sweep) and `rm -rf evals/runs/<sweep_id>/`. Compression typically 5–10×.
3. **Tarballs — keep ≥12 months by default.** After 12 months, eligible for deletion under disk pressure. Don't auto-delete; flag in `evals/report.md`'s footer. Decision to delete is per-quarter manual review.

**Wrapper command**: `python -m harness retention` — runs the compress + flag-old logic, idempotent. Should run weekly via cron in Phase 4 (or after every sweep — cheap). Phase 1 doesn't need this; tarballing kicks in at Phase 2 once we have ≥4 sweeps.

**What gets archived in the tarball**:
- `transcript.json` (largest contributor — Claude Code session output)
- `telemetry.jsonl` (compresses extremely well — repetitive event names)
- `artifact/` (varies — TS/Py `node_modules`/venvs are *not* in here; gold-reference subprojects always start clean per §4.0)
- `metrics.json`, `cli-invocations.jsonl` (small)
- `prompt.txt` (the resolved Jinja-substituted prompt; trivial size)

**What never gets archived**:
- `evals/projects/` source — version-controlled in git, not in `runs/`.
- `evals/baselines.json` — small, kept indefinitely.
- `evals/improvements/<branch>.md` retros — small, kept indefinitely.

**Pull-back-an-old-run**: `python -m harness extract <sweep_id> <run_id>` un-tars on demand. Rare; debugging only.

**Why not parquet for results.jsonl** (re-deciding): tempted to compact `results.jsonl` to columnar parquet for analytics speed. Decision: stick with jsonl. Reasons: (a) append-only is trivial for jsonl, painful for parquet; (b) `jq` + `awk` work out-of-the-box, parquet needs an extra dep; (c) at <100 MB/year, no compute reason to switch. Revisit if results.jsonl crosses 1 GB.

### 4.6 The improvement loop

Cycle: sweep → analyse (which projects failed, which used the most tokens, which had the biggest delta vs TS/Py) → hypothesise an [improvements.md](improvements.md) change → branch + ship → sweep candidate → A/B-compare → accept-or-revert. **The full protocol is specified in §7 Phase 3.**

The improvement pillar ([improvements.md](improvements.md)) is downstream of the harness — bug reports come *out* of the harness.

#### 4.6.1 Attempt model: borrow Aider's two-shot-with-feedback

Aider's polyglot benchmark gives the model **two attempts**: first attempt blind; if it fails, the model gets the unit-test output and tries again. This separates "first-shot ability" from "fix-iteration ability" — both matter, and Dark's tight-feedback story (`fn` → `run` → trace) ought to win disproportionately on the *second* attempt. Source: [aider polyglot README](https://github.com/Aider-AI/aider/blob/main/benchmark/README.md) and [leaderboard](https://aider.chat/docs/leaderboards/) (GPT-5 high: 88.0% pass, 91.6% edit-format compliance, $29.08/run on 225 problems ≈ $0.13/problem).

Our harness will report **pass@1**, **pass@2-with-feedback**, and **fix-iteration delta** = pass@2 − pass@1. We expect Dark's headline win to live in `fix-iteration delta`.

### 4.7 Agent task prompt template

What the wrapper actually sends to Claude Code per run. Reproducibility-critical: `prompt_template_hash` is part of every `sweep_id` (§4.5), so this template's contents are load-bearing — change it and you've started a new measurement series.

**Three messages per attempt**: a system-prompt augmentation (CLAUDE.md), an initial task user message, and (only on pass@2) a feedback user message. The agent's `claude --output-format json` writes its full transcript to `evals/runs/<sweep_id>/<run_id>/transcript.json`.

#### Template files (versioned in-repo)

```
evals/harness/prompts/
  system.{dark,ts,py}.md      # language-specific CLAUDE.md content
  task.md                     # initial task message (Jinja-templated over spec.md)
  retry.md                    # pass@2 feedback message (Jinja over rubric output)
```

The wrapper hashes the concatenation of `system.<lang>.md` + `task.md` + `retry.md` to produce `prompt_template_hash` for the sweep_id triple.

#### `system.dark.md` (the agent's CLAUDE.md for Dark runs)

```
You are building software in Darklang only. Use the Darklang CLI, not bash.

## Workflow
1. fn <name>      — create a function
2. run <fn>       — test it
3. commit "<msg>" — save it (idempotent within a session)

## First-look commands
- ./scripts/run-cli docs for-ai
- ./scripts/run-cli tree           (current package tree)
- ./scripts/run-cli stdlib overview

## Rules
- Full path on `fn`: e.g. fn "Darklang.MyProject.main (n: Int64): Unit = …"
- Lists use semicolons: [1L; 2L; 3L]
- String concat is ++, interpolation is $"…{x}…"
- Match patterns drop the type: `| Some x ->`, not `| Option.Some x ->`
- No nested `let` defining a function. Top-level only.
- Pipes need parens around complex LHS: `(complex expr) |> fn`

## When you're done
Print exactly the line `__HARNESS_DONE__` then stop. The wrapper polls for it
to mark the run complete.
```

`system.ts.md` and `system.py.md` are the analogues — point at `node`/`npm` (or `python`/`uv`), no bash, equivalent done-marker.

#### `task.md` (initial task message; Jinja over spec.md)

```
Build the project specified below in {{language}}. The artifact must satisfy
every behaviour bullet in the spec.

You have a budget of ${{budget_dollars}} (north-star metric §6.0).
Tools available: {{language_tools}}. No bash, no network unless noted.

When the rubric runner accepts your artifact, you'll know — but you may also
print `__HARNESS_DONE__` to declare done early.

---
{{spec_md_body}}
---

Begin.
```

Substitution variables (filled by `harness.runner`):
- `{{language}}` — `Dark` / `TypeScript` / `Python`
- `{{budget_dollars}}` — the §6 north-star cap (currently `0.50`)
- `{{language_tools}}` — `dark` / `node, npm` / `python, uv`
- `{{spec_md_body}}` — the project's `spec.md` minus its frontmatter

The agent never sees the rubric file (§4.0 enforcement: rubric path is permission-stripped from the cwd).

#### `retry.md` (pass@2 message; Jinja over the failing rubric output)

```
Your first attempt didn't pass. Here's what the rubric reported:

{{rubric_failure_output}}

You have ${{budget_remaining_dollars}} left. Apply a fix and try again.
{% if language == "Dark" %}
Tip: `traces tail` shows the most recent execution (input + per-fn calls).
`traces replay <id> --diff` re-runs against current code and diffs against
the recorded output.
{% endif %}

Begin.
```

The Dark-specific tip is the [improvements.md](improvements.md) §3.3 promotion of trace primitives — surfacing them in the retry prompt is the cheapest way to boost the **fix-iteration delta** (§6 #3) without shipping any Dark code change. **This is itself a candidate Phase 3 improvement wave**: A/B the prompt with vs without the trace tip and see how big the delta moves.

#### What the template deliberately excludes

- **No example solutions.** Showing a "here's how to structure it" hint would compress the cross-language signal we're trying to measure.
- **No mention of specific rubric tests.** Spec describes *behaviours*; rubric encodes *checks*. Agents that game the rubric should fail to satisfy behaviours, and §8 risk #1 (rubric mutation testing) catches the inverse.
- **No "be terse" / "be verbose" instruction.** We measure tokens; we don't optimise for them via the prompt. If Dark wins on tokens, we want it to be because of the *language*, not because we told the agent to write less.
- **No CoT scaffolding** ("first plan, then code"). Claude Code already has its own scaffolding; layering ours would distort the cross-language comparison.

#### What the template deliberately includes — tool-affordance hints (not CoT)

Distinct from CoT scaffolding above: pointing the agent at *available tools* is fair game, since the tools are an objective property of each language environment, not a thinking style. Surveyed iter 25 against [Anthropic's Claude Code best practices](https://code.claude.com/docs/en/best-practices), which finds "Claude mostly did well at verifying features end-to-end once explicitly prompted to use [testing tools]." Validates our existing trace-tip in `retry.md`.

For language-specific tool affordances, the system prompt mentions the tool, the task prompt does not prescribe *when* to use it. Examples that are okay:
- Dark: "`traces tail`, `traces replay <id> --diff`" (already in `retry.md`)
- TS: mention `node --watch` is available for iteration
- Py: mention `pytest` is available if the agent writes a test

Rule of thumb: *an instruction is okay if it's true regardless of which approach the agent takes*. "Use traces if a `run` fails" is okay (objective fact about the toolchain). "First write a test, then implement" is not okay (prescribes thinking).

#### Template versioning rules

1. Any text change → `prompt_template_hash` changes → new `sweep_id` → results are not directly comparable to prior sweeps. Acknowledge in the report.
2. If a Phase 3 improvement wave changes only the prompt (e.g. adds the trace tip), that's a *prompt-template-only* wave: keep `dark_sha` constant, change `prompt_template_hash`, run the bench.
3. The wrapper writes the resolved final prompt (after Jinja substitution) into `evals/runs/<sweep_id>/<run_id>/prompt.txt` for after-the-fact inspection.

#### Reproducibility settings (decided iter 31)

Cross-referencing `Darklang.LLM.Examples.CodeAgent` (Dark's own in-tree coding agent — prior art surveyed iter 31). CodeAgent pins `withTemperature 0.2`, `withMaxTokens 2000L`, `withMaxTurns 10L`. Our harness needs analogous knobs to keep `prompt_template_hash` actually meaningful — without them, sweep-to-sweep variance from random sampling would swamp the signal we're trying to measure.

**Pinned harness defaults**:

| Knob | Value | Reason |
|---|---|---|
| temperature | **0.0** | Stricter than CodeAgent's 0.2. Eval bench wants determinism: same `(dark_sha, prompt_template_hash, model_id, project, attempt_n)` should produce the same artifact, modulo provider non-determinism. Higher temperatures multiply variance. |
| max output tokens per turn | **16000** | Per vault `Agent Next Steps.md` item #4: CodeAgent's 2000 is "too low" for complex tasks; "16000+" was the proposed bump. We adopt 16K. |
| max turns per attempt | **50** | CodeAgent uses 10 (small-task assistant); Aider uses ~50. Our tasks span trivial → large; 50 covers the L tier without runaway. The §6 north-star ($0.50 budget) is the *real* cost cap; max-turns is a runaway-detector. |
| top_p, top_k | not set | Defer to provider defaults. With temperature=0, top_p/top_k don't materially change determinism. |
| seed (where supported) | derived from `run_id` | Anthropic SDK doesn't expose seed today; OpenAI does. When supported, seed = `hash(run_id)` to make individual runs reproducible. |

These knobs are part of the `prompt_template_hash` input — change a knob, change the hash, start a new measurement series.

**Why not match CodeAgent's settings exactly**: CodeAgent is an *interactive* coding assistant for ad-hoc tasks; our harness runs *non-interactive* batch evaluations with a budget cap and a rubric runner. Different optimization targets:
- Interactive wants quick turnarounds (low max_tokens), some creative variance (temperature=0.2), short loops (max_turns=10).
- Eval wants reproducibility (temperature=0), task completion (large max_tokens), and runaway protection (max_turns=50).

**`withFileTools`-style bundling as future migration target**: CodeAgent's `withFileTools` exposes file-ops as a *single grouped tool* in one Agent.create call. When the harness eventually ports to Dark (per §4.0 future migration path), the equivalent would be a `withDarkCliTools` bundle that exposes `dark fn`, `dark run`, `dark traces`, etc. as one grouped tool. Out of scope for Phase 1–4; flag for the migration milestone.

#### Open question

What's the right `__HARNESS_DONE__` polling cadence? Too tight: wraps Claude Code's own response stream. Too loose: agent declares done, wrapper waits 30 s before noticing. Initial guess: poll the transcript file every 2 s for the marker; in parallel, run the rubric every 5 s in case the agent forgets the marker but actually passed. Calibrate in Phase 1.

---

## 5. Open questions

- ~~How much do we need to constrain the agent ("you may only use Dark") vs let it freely fall back to bash?~~ **Resolved iter 36**: two-mode policy. **Strict mode** (Phase 1–3) — only `dark` / `node`+`npm` / `python`+`uv` exposed; bash blocked; escape attempts counted as §6 #14 diagnostic. **Realistic mode** (Phase 4+) — common Unix tools available (`curl`, `jq`, `grep`, `tar`, `awk`); separate sweep_id namespace so its results never aggregate with strict-mode rows. See §4.3.2 below.
- ~~Do we run with prompt caching on or off for the eval baseline?~~ **Resolved iter 7 / §7 Phase 1**: caching OFF for the headline baseline (honest first-pass numbers); caching-ON ships as a separate sweep mode in Phase 2. Both reported, segmented.
- What's the equivalent of "SWE-bench" for green-field projects — does anything pre-existing fit? *(Partially answered iter 3 — surveyed [SWE-bench Verified](https://www.swebench.com/verified.html), [Aider polyglot](https://github.com/Aider-AI/aider/blob/main/benchmark/README.md), [MultiPL-E](https://github.com/nuprl/MultiPL-E). None target green-field; we're building one. Could optionally adopt MultiPL-E's HumanEval/MBPP translations as a free trivial-tier baseline — flagged for Phase 2.)*
- ~~For cross-language fairness: do TS/Py agents get to pick frameworks (Hono, FastAPI), or do we pin?~~ **Resolved iter 16 / §4.3.1**: hybrid — pin runtime version + dependency-snapshot timestamp; no framework allowlist; agent picks libs freely within the snapshot; we *measure* what got picked as a diagnostic.
- How do we keep the rubric from gaming itself — i.e. agents writing code that passes our rubric but is bad? *(partially mitigated: §8 risk #1, mutation-test every rubric)*

**Resolved (with iter pointers):**

- ~~Minimum viable harness in 1–2 days?~~ Resolved iter 7: §7 Phase 1 spec.
- ~~Tokens-per-project vs pass-at-fixed-budget as north-star?~~ Resolved iter 9: pass-rate-at-$0.50/project. See §6.0.
- ~~Agent transcripts location?~~ Resolved iter 5: `evals/runs/<sweep_id>/<run_id>/transcript.json`.
- ~~`dark eval` CLI subcommand vs sibling Python harness?~~ Resolved iter 5: hybrid Python + language-native rubrics. See §4.0.

---

## 6. Specific metrics shortlist (the dashboard we want)

### 6.0 North-star + metric tiers

**North-star: pass rate at fixed cost budget** (e.g. "% of projects passed when capped at $0.50 per project, agent killed past the cap").

Why pass-rate-at-fixed-budget over the previously-implicit median-tokens-per-pass:

- **It rewards cheap wins AND penalises expensive failures simultaneously.** Median tokens hides the long tail of agents that spiral and never converge — those runs dominate cost in practice but not the median.
- **It maps directly to the user's actual question.** "If I give Claude $X to build this in Dark, will it work?" is what someone reading the dashboard wants to know.
- **It's robust to model swaps.** When models get cheaper, the budget shifts; the metric stays interpretable. Median-tokens drifts unmoored.
- **It's a single number that combines pass + cost** without resorting to a contrived weighting.

The cap (`$0.50/project` initially, tunable) deliberately forces a behaviour: agents that loop forever fail loudly. Re-tune in Phase 3 if the cap is too tight or too loose; the absolute number isn't the point, the *consistency* of the cap across sweeps is.

##### Cost-attribution formula (decided iter 39)

Open since iter 9. The cap is "billed cost," not raw tokens. Cached tokens are cheaper; output tokens are more expensive. Compute as:

```
cost_per_turn = (input_tokens         × input_price
              + output_tokens         × output_price
              + cache_creation_tokens × cache_creation_price
              + cache_read_tokens     × cache_read_price) / 1_000_000
```

Prices come from a per-model config file:

```json
// evals/pricing.json
{
  "claude-opus-4-7":      { "input": 15.0, "output": 75.0, "cache_creation": 18.75, "cache_read": 1.50 },
  "claude-sonnet-4-6":    { "input":  3.0, "output": 15.0, "cache_creation":  3.75, "cache_read": 0.30 },
  "claude-haiku-4-5":     { "input":  0.8, "output":  4.0, "cache_creation":  1.0,  "cache_read": 0.08 },
  "gpt-5":                { "input":  2.5, "output": 10.0, "cache_creation":  2.5,  "cache_read": 0.25 }
}
```

(Per-million-tokens, USD. Numbers approximate as of 2026-05; refresh quarterly.)

**Edge cases**:

- **Older API versions / models without `cache_read_input_tokens` in usage**: assume `cache_read = 0` (no cache used). Don't infer a default ratio — silent assumption masks bugs.
- **Prompt-caching-OFF baseline mode** (Phase 1, per §7 Phase 1): `cache_creation` and `cache_read` should be 0 in the response. If they're not, log a wrapper warning and proceed with raw-cost.
- **Provider-extended thinking tokens** (Anthropic-specific): bill at `output_price`. Wrapper sums them with normal output tokens before the multiplication.
- **Failed turns** (server returned 500 / timeout / rate-limit): tokens spent up to the failure still count against the budget. Keeps the bench honest about real-world cost; Phase 2 retry-on-flake (§4.4.1) still pays.

**Pricing-file is part of `prompt_template_hash`**: change a price → new `sweep_id` → not directly comparable. Same discipline as iter 31's reproducibility settings. **Important**: this means refreshing prices quarterly (when API prices change) starts a new measurement series. Acknowledge in `evals/report.md`'s footer; flag the price change clearly.

**Why not just raw tokens**: easier to compute, but then the bench is mute about the actual question users ask: "how much did this cost?" Raw tokens hide the input/output and cached/uncached price differential, both of which can be 5–25× ratios. Tracking dollars makes the §8 "Cost overrun" risk numerically grounded.

**Why not provider-billed-cost-of-record**: provider invoices arrive monthly with batched line-items; can't tie back to individual runs. Compute-it-ourselves loses ~few percent precision (provider may round differently, charge for overhead) but gains per-run attribution. Acceptable trade.

Metrics are organised in three tiers — **Headline** (must-track, on every dashboard), **Supporting** (track per-sweep, surface in deep-dives), and **Diagnostic** (track if cheap, used during regression analysis).

#### Headline (4 metrics — these are the dashboard front page)

1. **Pass rate at fixed cost budget** (Dark vs TS vs Py, per tier). *The north-star.*
2. **Pass rate per tier @ unbounded budget** (Dark vs TS vs Py). *Tells us whether failures are cost-bounded or capability-bounded.*
3. **Fix-iteration delta** = pass@2-with-feedback − pass@1 (Dark vs TS vs Py). *Promoted from "supporting" — this is where Dark's tight-feedback story lives. Per §2.1, we expect this to be Dark's biggest visible win in early sweeps, before the familiarity gap closes.*
4. **Trace adoption rate** (Dark only) — fraction of Dark runs where the agent invoked any `traces …` subcommand at any point. *Behavioural metric, not outcome. Tests whether the [improvements.md](improvements.md) §3.3 work is moving the loop, not just whether outcomes shifted.*

#### Supporting (track per-sweep)

5. **Median tokens per pass** (Dark vs TS vs Py). Cost-curve diagnostic.
6. **Dollars-per-pass** (median) — tokens × current API price. Aider-comparable. Reported alongside #5.
7. **Median wall time per pass**. Useful when comparing thinking-on vs thinking-off models.
8. **Edit-to-first-green** (commits before all-rubric-pass). Lower is better.
9. **Rework ratio** = (commits after first green) / (commits to first green). Lower is better.
10. **Artifact-size ratio** = Dark / smaller-of(TS, Py).
11. **Followup-edit cost** — tokens to apply a fixed small change to a finished project.
12. **Median CLI cold-start ms** (Dark only) — derived from `cli.total.ms` in `telemetry.jsonl`, averaged across all `dark` invocations in a run. *Added iter 24 / verified iter 23: each `dark` call has ~0.7–1.1 s of .NET process boot overhead; a 10-turn agent loop pays ~7 s. Currently the largest non-token wall-time component of a Dark run. Memory `feedback_no_json_or_dotnet_prewarm` deliberately defers the CLI-daemon optimisation; this metric makes the cost visible so the trade-off is re-litigatable from data.*

#### Diagnostic (track if cheap, used in regression deep-dives)

13. **First-parse-success attempts** (Dark only) — median number of `dark fn` calls before the first one parses.
14. **Constraint-escape attempts** — count of agent attempts to invoke `bash`/`sh`/etc. when the wrapper has only language-specific tooling exposed.
15. **Edit-format compliance** (Aider-style) — fraction of agent turns producing syntactically-valid output for the target language.
16. **Doc-bug encounters per run** *(added iter 24)* — count of times the agent hits one of the catalogued doc bugs in [improvements.md](improvements.md) "Documentation bugs surfaced during probes." Defined operationally: regex-match the 7 known bug signatures against `cli-invocations.jsonl` (e.g. `Inline function definition required`, `tries to read a script file`, `Will discard:` followed by exit 0). Drops to zero when the Phase 3 doc-bug batch wave ships. Behavioral metric for that specific wave's effectiveness.
17. **Median time-to-first-fn ms** (Dark only) *(added iter 24)* — wall ms from agent task start to first successful `dark fn` exit-0. Captures the §3.1 Discovery friction directly: high values mean the agent spent its early turns orienting (`tree`/`status`/`docs for-ai`) rather than authoring. Drops materially when wave 1 (CLAUDE.md template) ships. The metric that makes wave 1's effect visible distinct from wave 3 (`dark edit`).
18. **Time to friend-runnable artifact** (added iter 33; Dark only) — wall seconds from rubric-pass to a *separate machine* successfully running the artifact. Operationally: harness sub-step `dark publish <project> --out /tmp/artifact-<run_id>` + `scp` to a prepared remote env + `ssh` to invoke the project's main entry point and check exit 0. Records the wall sum of those three steps. **Blocked until §3.5 #1 (`dark publish`) ships** — pre-defined here so when wave 5 (`dark publish` MVP) lands, the metric activates without redesign. Promoted from §3.5's hand-wave to a concrete §6 entry: it converts the [plan.md](plan.md) §1 "share with a friend" promise into a number we can plot.

#### Sweep-level metrics (added iter 33; computed from the rows of a single sweep, not per-run)

19. **Total sweep cost ($)** — sum of #6 (dollars-per-pass) over every run in the sweep, plus the runs that didn't pass (which still cost tokens up to the budget cap). Reported in `evals/report.md` per sweep. Lets the user tell at a glance how much a sweep cost — the §8 "cost overrun" risk needs this; today the §6 individual-run dollars-per-pass doesn't aggregate cleanly to the budget answer.
20. **Sweep flake rate** — (runs with `harness_flake: true`) / (total runs). Tracks the §8 risk #8 ("Non-interactive `dark serve` startup flakiness") at the sweep level. If this trends > 5% the harness needs hardening before the bench is trustable.
21. **Cross-language coverage gap** — for each project tier, did all 3 languages produce a row? If TS/Py runs carried forward from a prior baseline (per §7 Phase 3 wave protocol), the report should mark which rows are *fresh* vs *carried forward*. Not really a metric in the score sense — a *meta-metric* about the sweep's completeness.

#### Display rules

- Phase 1 ships *all* Headline metrics or it isn't done. Supporting metrics ship best-effort.
- The dashboard plots the four Headline metrics on the same axis (sweep_id) so regressions in any one show up in the same eye-frame.
- Diagnostic metrics get a "diagnostics" tab; not on the front page.
- **Per-wave isolation metrics** (iter 24): each Phase 3 wave has a *primary* diagnostic that should move materially if the wave shipped well. Wave 1 (prompt-only) → #17 time-to-first-fn ↓ + #4 trace adoption ↑. Wave 3 (`dark edit` + auto-diagnostics) → #5 median tokens ↓ + #9 rework ratio ↓. Wave 4 (error-UX) → #13 first-parse-success ↓. The doc-bug batch wave → #16 doc-bug encounters → 0. If the primary diagnostic doesn't move, the wave shipped wrong (or the bench is too noisy). Captured per-wave in [`evals/improvements/<branch>.md`](../evals/improvements/) retros.

The loop should propose deletions/additions to this list as it learns.

---

## 7. Phasing

### Phase 0 (this doc) — specify what we're building.

### Phase 1 — Skeleton harness, end-to-end (target: 1–2 days)

Goal: collect *one row* of real data on Dark vs TS vs Py for 3 projects, with all the §6 headline metrics populated. **Does not need to be parallel, fast, or pretty.** It needs to produce trustworthy numbers we can iterate on.

**Project subset (chosen to expose maximum harness surface with minimum project work)**:
- `hello-cli` (T) — smoke-tests the whole pipeline end-to-end.
- `csv-to-json` (S) — parsing + JSON, no infra. Catches stdlib-discovery friction.
- `url-shortener-cli` (S) — Dark's persistence-without-setup differentiator. The only Phase 1 project that puts Dark in a winning posture.

Deliberately *skipping* HTTP for Phase 1 — health-polling adds complexity that should land in Phase 2.

**Punch list (files that exist when Phase 1 ships)**:

```
evals/
  projects/hello-cli/        spec.md, rubric.{dark,ts,py}, gold/{dark,ts,py}/
  projects/csv-to-json/      spec.md, rubric.{dark,ts,py}, gold/{dark,ts,py}/
  projects/url-shortener-cli/spec.md, rubric.{dark,ts,py}, gold/{dark,ts,py}/
  harness/
    __init__.py
    main.py                  # `python -m harness sweep|report|verify-rubrics`
    runner.py                # spawns claude-code with `--output-format json`
    metrics.py               # parses transcript + telemetry.jsonl into metrics.json
    report.py                # generates evals/report.md
  bin/
    dark-wrapped             # tee exit code + cmd into cli-invocations.jsonl
  results.jsonl              # one row per run, append-only
  report.md                  # most recent sweep summary
```

**Commands that work**:

```
# Run one project, one language, one attempt
python -m harness single --project hello-cli --language dark

# Full Phase 1 sweep: 3 projects × 3 languages × 1 attempt = 9 runs
python -m harness sweep --projects hello-cli,csv-to-json,url-shortener-cli \
                        --languages dark,ts,py --attempts 1

# Verify a rubric: mutation-test it against its gold reference
python -m harness verify-rubrics --project url-shortener-cli

# Re-generate the report from results.jsonl
python -m harness report <sweep_id>
```

**Phase 1 metric coverage** (matched to §6.0 tiers):

| Tier | Metric | Source for Phase 1 |
|---|---|---|
| Headline | Pass rate at $0.50 budget | rubric runner exit codes; cap enforced by wrapper |
| Headline | Pass rate unbounded | rubric runner exit codes |
| Headline | Fix-iteration delta | **deferred to Phase 2** (Phase 1 is pass@1 only) |
| Headline | Trace adoption rate (Dark only) | count of `traces …` invocations in `cli-invocations.jsonl` |
| Supporting | Median tokens per pass | transcript JSON `usage.{input,output,cache_*}_tokens` |
| Supporting | Dollars-per-pass | tokens × hard-coded current API price |
| Supporting | Median wall time per pass | wrapper `start_ts`/`end_ts` |
| Supporting | Edit-to-first-green | count of `commit` events in telemetry.jsonl (Dark) / count of file-write tool calls in transcript (TS/Py) |
| Supporting | Rework ratio | derived from above |
| Supporting | Artifact-size ratio | `du -sb` per language artifact dir |
| Supporting | Followup-edit cost | **deferred to Phase 2** (needs a follow-up prompt) |
| Diagnostic | First-parse-success attempts (Dark) | count of `dark fn` calls before the first that exits 0 |
| Diagnostic | Constraint-escape attempts | count of agent attempts at `bash`/`sh`/etc. (wrapper rejects + counts) |
| Diagnostic | Edit-format compliance | count of agent turns producing syntactically valid output per language |

**Decision points settled inline (so Phase 1 doesn't get blocked on bikeshedding)**:

- **How to invoke the agent**: shell out to `claude` CLI with `--output-format json` (not the SDK). Simplest possible. Switch later if we need streaming.
- **Concurrency**: none. Sequential runs. Concurrency is Phase 2.
- **Telemetry isolation**: per-run rundir (§4.2 option A). Each run sets `DARK_RUNDIR=evals/runs/<sweep_id>/<run_id>/`.
- **Prompt caching**: **off** for Phase 1 baseline. We want honest first-pass numbers. Caching-on becomes a separate sweep mode in Phase 2.
- **Mutation-testing rubrics**: not gating in Phase 1 (`verify-rubrics` exists but isn't blocking). Gate in Phase 2.
- **Constraint mode**: agent gets *only* the language-specific tooling exposed (no fallback to bash). For Dark: `dark-wrapped` is the only exposed binary; for TS: only `node`/`npm`; for Py: only `python`/`uv`. We'll measure how often agents try to escape and treat the count as a §6 metric in Phase 2.

**Time budget**:

| Half-day | Work |
|---|---|
| Day 1 AM | Write 3 `spec.md` + 9 gold references (3 projects × 3 langs). Two are trivial; budget ~30 min each. |
| Day 1 PM | Write 9 rubrics (3 specs × 3 langs). Budget ~20 min each. |
| Day 2 AM | Build the Python harness skeleton (`main.py` + `runner.py` + `metrics.py`). |
| Day 2 PM | First end-to-end sweep + report. Iterate on flakiness. Land Phase 1. |

**Definition of done**: `python -m harness sweep …` runs to completion, produces `evals/results.jsonl` with 9 rows, and `python -m harness report <sweep_id>` writes a markdown report including all 9 of the populated §6 metrics. The numbers don't have to look good — they have to be real.

### Phase 2 — Scale to 30 projects (target: 1 week)

- Parallelism (configurable -j flag; default `nproc / 2`).
- Pass@2-with-feedback attempt model (§4.6.1).
- Mutation-testing rubrics becomes a hard gate before a project enters the bench.
- HTTP projects join the bench (`http-healthz`, `webhook-echo`).
- Report generator hardens: comparison vs prior sweep, regression callouts.
- Followup-edit cost (§6 #11) populated.

### Phase 3 — First improvement wave (the A/B protocol)

The point of Phase 3 is to convert "we shipped a Dark improvement" into "we have data showing the improvement helped." Without a protocol, this slides into vibes. With one, every improvement either survives the bench or gets reverted.

**The user's framing**: *"all the dark work should probably be in a branch and whether or not I merge the work in progress or not is the determination will make later."* The protocol below makes that determination data-driven.

#### Protocol

1. **Pick a hypothesis.** One [improvements.md](improvements.md) item per improvement wave. Likeliest first: §3.2 #1 (`dark edit`) or §3.2 #2 (auto-diagnostics-after-write) — both CLI-surface only, no language change.
2. **Branch.** `git checkout -b improve/dark-edit` off `main`. The improvement lands here, not on `main`.
3. **Baseline sweep** (already in hand from Phase 2 — re-use the most recent `main` sweep_id within the last 14 days; otherwise re-run on `main`).
4. **Candidate sweep.** Bench grows a `--dark-revision <git-rev>` flag. The wrapper checks out the branch into a Dark worktree, rebuilds the CLI binary, and points the harness at it.
   - **Concretely**: `python -m harness sweep --dark-revision improve/dark-edit --projects all --languages dark`. Note: only re-run *Dark* projects when only Dark changed; TS/Py runs are unaffected, so re-using their baseline rows is honest and saves cost. Wrapper enforces "TS/Py results carry forward only if `dark-revision` is the only change."
5. **Compare.** `python -m harness ab <baseline_sweep_id> <candidate_sweep_id>` produces a markdown report:
   - Each Headline metric (§6.0): baseline value, candidate value, absolute delta, signed delta, p-value (paired bootstrap over project rows).
   - Per-tier and per-project breakdowns.
   - Regression callouts: any project that *regressed* by > 1 std deviation gets a flag, even if the aggregate moved the right way.
6. **Acceptance criterion** (proposed, tune in Phase 3 itself): improvement merges to `main` if at least 2 of the 4 §6.0 Headline metrics moved positively by > 1 std deviation **and** none regressed by > 1 std deviation. If only the *Dark-targeted* metrics moved (e.g. trace adoption rose but pass-rate didn't), that's a partial win — keep the branch open, hypothesize a follow-up, don't merge yet.
7. **If accepted**: merge, write a short retro into `evals/improvements/<branch-name>.md` (what changed, what moved, what didn't) — append-only history of every wave.
8. **If rejected**: don't merge. Branch stays alive for a possible re-attempt. Retro still gets written (negative results are still data).

#### Phase 3 deliverables (concrete files / commands)

- `python -m harness sweep --dark-revision <ref>` (new flag).
- `python -m harness ab <baseline> <candidate>` (new subcommand).
- `evals/improvements/<branch-name>.md` (one per wave).
- `evals/baselines.json` — pinned `(dark_sha, sweep_id)` pairs we accept as "current `main` baseline" without re-running. Refreshed when `main` moves.

#### What Phase 3 explicitly does NOT do

- **No MCP server.** The §2.1 tension between "constrain agent to Dark CLI only" (Phase 1–3 stance) and "MCP for ecosystem reach" (Phase 4+) is resolved here: **MCP is a Phase 4+ deliverable, not part of the eval bench's improvement waves.** Reason: the bench is measuring whether Dark can be the agent's *primary* platform. Mixing in MCP confounds the experiment — a Cursor-via-MCP win is not the same product as a Claude-Code-via-CLI win. They're complementary distribution paths but different experiments. Phase 4 ships the MCP server as ecosystem-reach work, separately from the bench.
- **No language/runtime changes** in the first 2 improvement waves. Every change in the first 2 waves is CLI-surface or agent-prompt-template only. Reason: keep the cycle time tight; language-runtime changes are slower to ship and slower to revert if the bench rejects them.
- **No Dark improvements that aren't on the [improvements.md](improvements.md) backlog.** Discipline. Speculative-feature improvements without an item don't run through the protocol.

#### Wave queue — proposed ordering for the first 5 waves (decided iter 22)

Once the [improvements.md](improvements.md) backlog has ~25 enumerated items, a queue is more useful than a backlog. Each wave below is a *coherent bundle* — items that share a Dark-code surface or all live in the prompt template, so one A/B sweep tests a single hypothesis. Ordered by ship-cost first (cheapest wave runs first), with intent that early waves *also* validate the harness's sensitivity to small changes before we commit expensive engineering.

| # | Wave name | Bundle | Cost | Hypothesis (which §6 metric) |
|---|---|---|---|---|
| 1 | **Prompt-only bundle** | CLAUDE.md template (§3.1 #1) · agent-generated SUMMARY.md (§3.6 #3) · surface `merge --dry-run` + `rebase --status` (§3.4 #4) · trace tip in `retry.md` (§4.7 / §3.3 promo) | **Zero Dark code.** Just `evals/harness/prompts/*.md` edits. | Trace adoption rate (§6 #4) ↑, fix-iter delta (§6 #3) ↑, median tokens (§6 #5) flat-or-slightly-up (SUMMARY.md adds tokens). **Also a harness sanity check**: if the bench can't detect this, it's broken. |
| 2 | **`--json` rollout** | All 9 missing flags from §3.1 #6 audit · fix `builtins --json` coercion bug | Single shared formatter; one Dark PR. Cheap CLI-surface only. | Median tokens (§6 #5) ↓ (parseable output is denser); constraint-escape attempts (§6 #13) ↓ (less shelling out to grep/awk). |
| 3 | **Authoring headliners** | `dark edit` (§3.2 #1) · auto-emit diagnostics after write (§3.2 #2) | Bundle: edit produces diagnostics. Medium-cost CLI-surface. | Median tokens (§6 #5) **big drop** on M/L tier; rework ratio (§6 #9) ↓; edit-to-first-green (§6 #8) ↓. The biggest predicted token-impact wave. |
| 4 | **Error-UX bundle** | Parse-error suggestions (§3.2 #4) · "did you mean" on miss (§3.1 #3) · auto-attach trace on fail (§3.3 #1) | Three error paths, one shared "make errors helpful" theme. CLI-surface. | First-parse-success attempts (§6 #12) ↓ materially; rework ratio (§6 #9) ↓; fix-iter delta (§6 #3) ↑. |
| 5 | **`dark publish` MVP** | §3.5 #1 (publish to a directory; `--single-file` Phase 4) · §3.5 #4 (`dark export`/`import` lightweight) | Largest of the early waves. Touches packaging + dependency closure. | Enables a new metric: "time to friend-runnable artifact." User-visible (§1 promise). Won't move §6 headlines directly, but unlocks the headline §1 narrative. |

**What's NOT in the first 5 waves** (deliberately deferred):
- `dark rename` (§3.4 #1) — graph-rewrite is expensive; defer until rename-heavy projects show up in the bench (not in the Phase 1 starter set).
- `dark suggest <NL>` (§3.1 #4) — full-text-over-docs MVP is feasible, but the embeddings stretch wants `Stdlib.LLM` working in CLI (open Q since iter 13). Defer to wave 6+.
- `dark uncommit` / `dark revert` (§3.4 #2) — hits the SCM machinery; expensive to get right; defer until bench confirms it's a real friction.
- `dark review` + `dark review-mark` (§3.6 #1, #5) — high-value for human reviewers, but doesn't move §6 *agent* metrics. Phase 4 candidate.

**Wave-1-first rationale.** Putting the prompt-only bundle first does double duty: ship cost is near-zero, *and* if the §6 metrics don't move on a prompt change the bench is too noisy to drive Dark-code investment. Cheap insurance against running expensive waves on a broken bench. (Aider's harness applies the same trick — they run a prompt-only delta as a calibration row.)

**Acceptance**: each wave runs the §7 Phase 3 protocol independently. If a wave fails its acceptance criterion, the next wave doesn't automatically start — re-baseline first.

#### Wave 1 sub-protocol: isolate which prompt change moves the metric (decided iter 37)

Wave 1 bundles 4 prompt-only sub-changes (per the wave queue table above). Running them as one composite A/B answers "did *something* move?" but not "*which something*?" When the budget allows (and prompt-only is *cheap*), a sub-A/B isolates each contribution.

**Configuration**: 5 prompt variants run as a sub-sweep. Each variant is one row in `evals/results.jsonl` tagged `wave: 1, variant: <name>`.

| Variant | CLAUDE.md template | SUMMARY.md ask | merge-tip | trace-tip in retry.md | Hypothesis |
|---|---|---|---|---|---|
| `baseline` | — | — | — | — | The pre-Phase-3 numbers (carry forward from Phase 2 if available) |
| `clauded-md` | ✓ | — | — | — | #17 time-to-first-fn ↓ alone |
| `summary` | — | ✓ | — | — | Tokens-per-pass ↑ slightly (SUMMARY.md adds tokens) but human-review-time would drop (un-tracked metric) |
| `merge-tip` | — | — | ✓ | — | Trace adoption rate (#4) untouched; expect *no movement* — this variant is the null-hypothesis test |
| `trace-tip` | — | — | — | ✓ | Trace adoption rate (#4) ↑, fix-iteration delta (#3) ↑ |
| `all` | ✓ | ✓ | ✓ | ✓ | Sum of the above (worth checking that the effects compose) |

**Cost**: 6 variants × 23 vetted projects × Dark-only (no TS/Py since this is Dark-prompt) × pass@1 = ~138 runs. At ~$0.13/run (Aider-comparable from §4.6.1), ~$18 per wave-1 sub-sweep. **Cheap**.

**Reading the results**:

- If `merge-tip` moves any §6 metric materially, something's wrong with our hypothesis (it's there for `dark merge` confidence, not for Discovery / Authoring metrics). Investigate.
- If `clauded-md` doesn't move #17 (time-to-first-fn) when the §3.1 #1 hypothesis says it should, the CLAUDE.md content needs revision *before* declaring wave 1 done.
- If `all` ≠ sum of individual movements, there's interaction between the changes. Prompt-template-only waves rarely have non-linear interactions, but record the finding.
- Pick the variants whose effect is positive enough to keep, drop the rest.

**Carry-over to wave 1 final**: only the variants with verified positive effect get bundled into the merged prompt template. Negative or null variants stay out. This is *better* than shipping all 4 changes wholesale and hoping.

**Why we don't sub-A/B every wave**: most other waves change Dark code, where sub-A/B'ing is expensive (each variant requires a separate `dark_sha`). Wave 1 is uniquely cheap because it's prompt-only.

### Phase 4 — 100 projects, sweep cadence + ecosystem reach

- Decide cadence based on Phase 3 cost data (likely weekly + per-PR for Dark improvements that touch the [improvements.md](improvements.md) surface).
- Public-facing report at this point — a leaderboard page if we want one.
- **MCP server** ships here, separately from the bench (per Phase 3 boundary above). Goal: Dark composable from Cursor / Claude Code as a tool, alongside the standalone-CLI experience the bench measures. Different product surface, different KPIs (adoption, retention) — not on the §6 dashboard.

### Phase 5+ — Iterate.

---

## 8. Risk / failure modes

- **Rubric tests don't actually verify the behaviour they claim.** OpenAI's Feb 2026 audit of SWE-bench Verified found 59.4% of the hardest tasks had tests that wouldn't catch the intended bug ([source](https://openai.com/index/introducing-swe-bench-verified/) and [CodeSOTA write-up](https://www.codesota.com/guides/swe-bench-explained)). Mitigation: every rubric is mutation-tested before acceptance — flip one obvious thing in the gold reference and confirm the rubric goes red. Plus a human spot-check on the first 30 projects. Externally validated *iter 35*: Anthropic's [Effective harnesses for long-running agents](https://www.anthropic.com/engineering/effective-harnesses-for-long-running-agents) explicitly notes that "separating generation from evaluation into distinct agents outperforms self-evaluation, because agents reliably skew positive when grading their own work." Our rubric-independence design (rubric runner is *not* the agent, never imports the artifact, lives in evals/projects/) is exactly this principle.
- **Agents memorising the rubric.** If specs ship in-repo, agents trained later may have seen them. Mitigation: keep specs out of public training data (private repo or rotated paraphrases per sweep); track a held-out "verification" subset that's never published.
- **Picking projects that flatter Dark.** A bench full of HTTP/persistence projects unfairly rewards Dark's `serve` + `DB` story. Mitigation: tier mix in [projects.md](projects.md) already enforces variety; keep ≥30% of the 100-project bench in pure-CLI/algorithm/string-processing where Dark gets no infra advantage.
- **Reference implementations rot.** If TS/Py reference impls fall behind their ecosystem (e.g. an HTTP framework deprecation), the comparison gets noisy. Mitigation: pin language toolchain versions per sweep (`node:22-alpine`, `python:3.13-slim`); regenerate references on a quarterly cadence and re-baseline.
- **Cost overrun.** 100 projects × 3 languages × pass@2 × multiple attempts gets expensive. At Aider's GPT-5-high rate (~$0.13/problem), one sweep ≈ ~$80 if Dark/TS/Py are similarly priced. Mitigation: tier-gated caching — only re-run the trivial tier when something material changes; full sweep on weekends or before big PRs.
- **Harness flakiness drowning the signal.** Network-dependent rubrics (HTTP projects) flake on slow CI. Mitigation: every HTTP rubric runs against `127.0.0.1` only, with an explicit health-poll loop before the test calls; non-deterministic projects have ≥3 reps with `min(passes) ≥ 2`.
- **Agent abandonment misclassified as pass.** Agent says "done" but rubric never ran. Mitigation: harness requires a non-empty rubric.json and refuses to score zero-test runs; emits `agent_abandoned: true` distinctly from `failed: true`. Externally validated *iter 35*: Anthropic's harness research identifies "false-completion" as a recurring failure mode in long-running agents — *"a later agent instance would look around, see that progress had been made, and declare the job done."* Our rubric-as-independent-arbiter already prevents this for single-session greenfield work; if the harness ever grows to span multiple sessions per project (Phase 4+ migration territory), the split-prompt + progress-artifact pattern from Anthropic's [post](https://www.anthropic.com/engineering/effective-harnesses-for-long-running-agents) is the way.
- **Non-interactive `dark serve` startup flakiness** *(probed iter 15 + iter 26)*. Background-mode invocation occasionally fails silently — process consumes but never binds the port. Affects every Phase 2 HTTP project. Mitigation: §4.4.1 specifies port-poll readiness check + retry-with-restart (up to 3) + `harness_flake: true` metric tag so flakes don't pollute the §6 dashboard. Phase 2 deliverable.

---

## References

- `~/vaults/Darklang Dev/02.Project Management/Current Experiment/project-survey.md` — AI-generated CLI Project Survey (12 classes, ~120 candidate projects). User has not yet validated.
- `~/vaults/Darklang Dev/02.Project Management/Current Experiment/where we're a bit short.md` — user-known Dark gaps.
- `~/vaults/Darklang Dev/02.Project Management/Current Experiment/TODOs to Improve AI's Development.md` — concrete known issues (parse-error UX, multi-line editing, rename, etc.).
- `~/vaults/Darklang Dev/02.Project Management/Current Experiment/Goals for Week.md` — user's explicit week-of goals (≈10 repeated projects, baseline numbers, simple test system).
- `~/vaults/Darklang Dev/05.Implementation/AI/Agent Next Steps.md` — concrete critique of Dark's own agent that maps onto Claude-Code-using-Dark frictions.
- [SWE-bench Verified](https://www.swebench.com/verified.html), [Aider polyglot](https://github.com/Aider-AI/aider/blob/main/benchmark/README.md), [MultiPL-E](https://github.com/nuprl/MultiPL-E) — bench design ancestors.
- [Convex](https://www.convex.dev/), [Instant DB](https://www.instantdb.com/) — competitive landscape.
- [Anthropic engineering: Effective harnesses for long-running agents](https://www.anthropic.com/engineering/effective-harnesses-for-long-running-agents) — *Surveyed iter 35*. Most of the post is about *multi-context-window long-running* development (out of scope for our single-session greenfield bench). Two insights validated existing design decisions: (1) "agents reliably skew positive when grading their own work" → independent rubric is right (cited at §8 risk #1); (2) "false-completion failure mode" → agent_abandonment detection is right (cited at §8 risk "Agent abandonment"). The split-prompt + `claude-progress.txt` + initializer/coder pattern is the right reference for the eventual port-harness-to-Dark migration (§4.0) if/when it grows multi-session scope.
- [Anthropic engineering: Harness design for long-running application development](https://www.anthropic.com/engineering/harness-design-long-running-apps) — companion post. Flagged for future deep-read if multi-session ever becomes in scope.
- [Claude Code best practices](https://code.claude.com/docs/en/best-practices) — Research → Plan → Execute → Review → Ship workflow pattern; testing-tool-affordance recommendation; code-intelligence-plugin for auto-diagnostics. Iter 25 cited inline at §4.7 ("What the template deliberately includes") and at [improvements.md](improvements.md) §3.2 #2.
