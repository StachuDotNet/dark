# Dark as the optimal AI coding target

## Goal

A user opens a fresh Claude Code session in any directory and says:

> "Using only Darklang, build me \<a thing\>."

…and the agent does a great job — better than building the same thing in TypeScript or Python. "Better" measured by tokens and wall time to first-working and to done; output artifact size on disk; runtime perf on representative workloads; pass rate against a per-project rubric; and the edit cost of a small follow-up change. Optimizing for the agent loop also tightens the loop for humans, especially during code review.

The north-star deliverable: **two local release builds syncing — one server (an always-on desktop on the Tailscale network), one client — with branches, efforts, and experiments completed, all written in Dark using AI.** Sync rides the substrate described in [sync.md](sync.md): the wire carries only ops and commits, and every receiver regenerates its projections locally.

## Why Dark plausibly wins

The thesis is that Dark's structural advantages compound with the agent loop while its gaps are closeable. The competitive landscape (Convex, Instant DB) and the full argument live in the analysis companion; what matters for the spec is that the bench has to exercise Dark's *actual* differentiators — the live-programming workflow, traces-as-test-data, the persistent package tree, and deprecation-as-first-class.

Things Dark already has that other stacks don't:

- **Persistent package tree, amortised across projects.** No `node_modules`, no `requirements.txt`, no Docker layers — the thing under construction is a SQLite-backed package tree, not a directory of files. The total install isn't dramatically smaller than a Python venv with FastAPI, but the framing is **install once, build many**: adding a 100th project to a Dark install costs ~zero on disk, where a 100th Node project costs another `node_modules`. Agents are strong at structured CRUD over the tree, weaker at coordinating filesystem layouts and per-project dep installs.
- **Live programming workflow.** `fn` to define, `run` to test, `commit` to save — no build step between `fn` and `run`. The one cost the harness must price in: each `dark` CLI invocation pays ~0.7–1.1 s of cold-start (a fresh .NET process per call), so a 10-turn loop spends several seconds in pure CLI overhead, which dominates wall time at small budgets. The CLI-daemon optimisation is deliberately deferred; the harness surfaces cold-start as a supporting metric so the trade-off stays re-litigatable from data.
- **AI-shaped CLI surface already.** `docs for-ai` / `for-ai-internal`, `tree`, `search`, `view` (with `--with-trace` overlay), `traces …`, `find-values`, `agent`, and `review` (the interactive changeset TUI). Most languages ship nothing like this.
- **Built-in tracing, far richer than a scaffold.** Every `run`/`eval`/HTTP request stores a trace; the CLI around them is already substantial — `list`, `view`, `tail`/`follow` (NDJSON streaming), `stats`, `find`, `hotspots`, `replay`, `delete` (verified against `cli/commands/traces.dark`). Output is small and structured, well-suited to feeding back into the agent loop without flooding context. The competing `console.log`-and-rerun loop in TS/Py has nothing comparable. This is one of Dark's most under-leveraged advantages. (Several adjacent capabilities are *proposed, not built* — a `--diff` flag on `replay`, `gen-test`, `export`/`import`, `inspect` — and live as improvements in [issues-and-improvements/traces-and-debugging.md](../issues-and-improvements/traces-and-debugging.md); don't count them as existing strengths.)
- **Deprecation as a first-class concept.** Deprecated items disappear from `search`/`tree`/`ls` but `view` still works and prints a prominent deprecation header. Three kinds exist — `obsolete`, `harmful` (halts the runtime unless opted in), `superseded-by` (requires a replacement). For agents this is a working-set discipline tool: follow it and the produced code looks freshly-current.
- **Telemetry already streaming.** CLI and test events land in one greppable JSONL stream, with confirmed event names across `cli.*`, `commandExec`, `httpserver.*`, `seed.*`, and `test.suite.*`. The harness leans on this directly.

Things Dark does *not* yet have that competing stacks do (the improvement backlog):

- **No diff-based editing primitive.** `dark fn` rewrites the whole function body — a single-line tweak to a 50-line function costs 50 lines of output. Hits median-tokens and edit-to-first-green hardest.
- **No automatic post-write diagnostics.** After a successful `fn`, the agent has to explicitly check whether anything downstream broke; there's no "you changed X, here are the 4 callers that no longer typecheck" loop.
- **No read-before-edit guard.** Agents can overwrite work they didn't realise existed; mainstream IDEs enforce a read implicitly.
- **No trace-as-test affordance.** Dark *captures* traces but the agent has no terse way to say "this trace is the failing case, replay it against my new version."
- **No agent-shaped error vocabulary.** Parse errors are message-only — no "did you mean", no suggested fix, no `docs syntax` pointer. (A `[1L, 2L]` comma mistake produces a long stack trace with no hint about the separator.)
- **No project-level instruction file the agent auto-reads.** Mainstream agents pick up `CLAUDE.md` / `AGENTS.md`; Dark's for-ai docs are a separate command the agent must know to call.
- **Two-pass build for some type changes.** The first build carries a stale `package-ref-hashes.txt`; the agent has to know to touch and rebuild.
- **Branch context doesn't persist between invocations.** Agents thread `--branch <name>` on every call; easy to forget and silently land on `main`.
- **No async / concurrency primitives, no native test support.** A parallel downloader isn't directly expressible; agents roll their own assertion code per project.
- **Familiarity gap in training data.** Agents have seen far more TS/Py than Dark, so first-pass quality will trail for the same prompt. Tight feedback loops mitigate but don't erase this — Dark's expected early win is a *fix-iteration* advantage, not a *pass@1* one.
- **No reactive queries, no MCP server or bundled skills.** Out of scope for the bench but real for ecosystem reach.

## Eval harness — measure that we're getting better

### Layout and language

**Decision: hybrid Python wrapper + language-native rubrics.** The wrapper that orchestrates Claude Code invocations, captures token usage, and writes metrics is a small Python package. Each project's rubric is written in the same language as the artifact under test; the rubric never imports the artifact — it shells out and inspects stdout / HTTP responses.

Why not build the harness in Dark itself? Three reasons: (1) **don't depend on what you're measuring** — a Dark regression that breaks the framework would blind us exactly when we need visibility; (2) **bootstrapping cost** — the harness needs HTTP polling, parallel process management, transcript JSON parsing, subprocess timeouts, and jsonl writing, all idiomatic in Python today; (3) **avoid a `dark eval` subcommand creating circular pressure** to ship Dark features just so the harness runs. TS would also work; Python wins on subprocess and taste. Once the harness has been stable for several sweeps and Dark has shipped the relevant improvements, porting the wrapper to Dark is a worthwhile dogfooding milestone — but not earlier, since it conflates the measurement tool with the thing measured.

The harness lives under `evals/` at the repo root: a Python wrapper, per-project directories (each holding the agent-visible `spec.md`, one rubric per language, and a perf driver), per-run output directories, an append-only `results.jsonl`, and the most-recent `report.md`.

The agent-visible `spec.md` is the project spec from `notes/projects/` — a goal line plus an acceptance-criteria checklist, tagged greenfield/brownfield (no `modules`/`language` fields; those were deliberately dropped). The harness adds only sweep-side bookkeeping *outside* the spec (a size bucket for balancing, the per-language rubric, smoke commands) so the spec the agent reads stays identical to the long-lived one. The agent sees **only** `spec.md` — the rubric is hidden in the same directory, and runs where the agent reads the rubric directly are detected and invalidated (the wrapper symlinks `spec.md` into a permission-stripped working directory; the rubric is not exposed).

### Bench projects

The bench draws from the `notes/projects/` catalog (~125 specs, grouped by category there). For sweep balancing the harness assigns each a size bucket (trivial / small / medium / large) — bookkeeping, not a spec field. An agent with no time budget writes the reference implementations; a human spends at most 30 minutes reviewing each before it's accepted as the gold copy. The rubric runner is language-agnostic — it shells out to the artifact and inspects stdout / HTTP, never imports the implementation.

Gold references are kept ephemeral rather than committed in-tree: the Dark gold lives in a `bench-gold-<project>` branch accessed via `dark --branch …`, built once per quarter and deleted between sweeps to force fresh impls; TS/Py/Go/Rust gold lives in a per-sweep cache, purged before each new impl without peeking. This eliminates the "gold reference rots in-tree" failure mode.

### Per-run metrics

For each `(project, language, agent_config, attempt_n)` we record a `run_id`, and every metric correlates by it.

Telemetry isolation is the load-bearing plumbing decision. Each run gets its own fresh `telemetry.jsonl` by pointing the run at a dedicated run directory — configured through the CLI-adjacent `.darklang` config, not an environment variable. No Dark code change is required for this. End-to-end wall time is then the last telemetry event's wall-clock minus the first's, clean because runs are isolated. (If concurrent runs ever share a directory, context-stamping each event is a small, deferred Dark change.)

| Metric | How collected |
|---|---|
| Input / output / cached tokens | `claude --output-format json`, parse `usage.{input,output,cache_read_input,cache_creation_input}_tokens` |
| Tool-call count, by tool | Count `tool_use` entries in the transcript, grouped by `name` |
| Wall time, end-to-end | Wrapper records start/end around the agent invocation |
| Wall time in Dark CLI vs agent thinking | Sum of `cli.total.ms` ÷ wall time |
| Number of `dark` invocations | Count `cli.total` events |
| Failed `dark` invocations | Count nonzero exits captured per invocation in `cli-invocations.jsonl` |
| Final artifact size | `du -sb` over the run's `artifact/` |
| Build success / fail | Process exit code of the `dark` build subcommand |
| Test pass rate | Rubric runner exit code + parsed pass/fail count |
| Runtime perf | Per-project driver calls the artifact N times, records median + p95 |
| Lines of code | Dark: fn count + body line sums via `list`/`view`; TS/Py: `tokei`/`cloc` |
| Functions created / used / deprecated | `dark status` + `dark list --deprecated` |
| Edits to first green | Count of `commit` events before the first all-rubric-pass |
| Rework rate | (commits after first green) / (commits up to first green) |
| Agent abandonment | Agent stops cleanly *and* rubric not yet green |
| HTTP request handling | `httpserver.request` events: count + p95 |

### Cross-language baselines

The same projects in five languages — Dark, TypeScript, Python, Go, Rust — run by the same agent (Claude Code) given the same task prompt with only that language's tooling exposed. Compare metrics; track the gap over time.

We adopt MultiPL-E's design: one canonical spec per project, language-agnostic tests (the runner shells out, never imports). Its finding that one language consistently beats another on the same spec is a useful prior — language-shape friction is real and measurable even when the prompt is identical. The harness exists to make that friction visible for Dark and then drive it down.

Cadence: the cross-language baselines (TS/Py/Go/Rust) run weekly, manually triggered, averaged over runs to smooth the LLM non-determinism that survives `temperature=0`; Dark sweeps run every day or two, manually triggered when an improvement is shipping. The Dark numbers are the moving signal; the cross-language baselines are the relatively-stable comparison floor.

#### Constraint-mode policy

The bench runs in one of two constraint modes, each a **separate measurement series** whose rows never aggregate with the other's.

**Strict mode (headline)**: the agent gets only the language-specific tooling — `dark`, or `node`/`npm`, or `python`/`uv`, etc. No bash, no shell builtins, no `curl`/`jq`/`grep`/`tar`. The wrapper enforces this by sandboxing the working directory and whitelisting `PATH`. Escape attempts (any `bash`/`sh`/`awk`/`sed`/…) are counted as a diagnostic and *rejected* — the agent gets a "command not allowed in strict mode" stderr.

**Realistic mode (secondary)**: the agent gets the full agentic toolset — language tools plus common Unix utilities — in a separate sweep namespace.

Why both, and why strict is headline: strict mode measures **what we're actually trying to learn** — when an agent uses Dark *as a primary platform*, how does it do? If it shells out to `jq` for JSON munging, we're measuring `jq`, not Dark's `Stdlib.Json`. Realistic mode is the honesty check — most people give Claude Code bash, and if the strict headline tells a Dark-wins story while realistic shows agents shelling out most of the time, the headline is misleading. A/B improvement waves only run in strict, to keep the signal clean. As the backlog lands, the strict-vs-realistic delta should narrow because Dark itself becomes good enough that agents *don't* reach for bash — that's the win condition. Run strict every sweep; run realistic occasionally as a sanity check.

#### Framework-pinning policy

The worry: if TS/Py agents pull arbitrary libraries, sweep-to-sweep variance balloons; if we pin a framework, we handicap exactly the ecosystem advantage Dark wants to win against.

**Decision: hybrid pinning.** Pin the runtime version per language (e.g. `node:22-alpine`, `python:3.13-slim`, current Go and Rust images), re-pinned quarterly and locked at the sweep boundary. Pin a **dependency-snapshot timestamp, not a framework list** — agents `npm install` / `uv add` freely, but through a registry snapshot pinned to the sweep date, so a May sweep sees May's package versions on re-run. No framework allowlist — the agent picks what it would pick in real life, and we *measure* what got picked (a diagnostic, not a gate). Dark is already pinned by its SHA: the package tree at that SHA *is* the snapshot, with no external-library equivalent to control. Large projects may carry a `framework_hint` in frontmatter to spare the agent burning its budget reading docs for a heavy framework — used sparingly.

Not simpler (one framework per language): the catalog spans pure-CLI, HTTP services, TUI apps, and data-munging — a single pin would mismatch most projects. Not full freedom (no snapshot): upstream churn would make February and August sweeps disagree purely from drift; the snapshot makes that drift explicit and quarterly.

### Parallelism and sandboxing

Each run is isolated in its own run directory holding the full transcript, telemetry, artifact, and metrics. Each project gets a stable SQLite seed by copying the package tree rather than mutating the source. N parallel Claude Code instances are configurable; concurrency is not a day-one requirement.

HTTP projects carry a known reliability hazard: starting `dark serve` in non-interactive background mode is flaky from a non-TTY shell — the process can consume but never bind the port, and the mode-switching is non-deterministic. The harness handles this with a robust readiness check (poll the listening port every ~200 ms with a generous timeout — *don't* poll the log, which doesn't always flush before the port binds), retry-with-restart up to a few times (tagged `harness_flake: true` so flakes don't pollute the dashboard), and, as a last resort, wrapping the serve invocation under a pty allocator. Hot-reload is disabled in bench mode to avoid an "address already in use" race from a second listener — toggled through `.darklang` config rather than an environment variable.

### Storage and reporting

Every run appends a row to `results.jsonl`; `report.md` is regenerated per sweep with per-project results, language deltas, regressions vs the prior sweep, and biggest wins. Sweep IDs are `(dark_sha, agent_version, prompt_template_hash)` triples, so improvement plots over time and regressions tie back to a specific input change.

Retention has three tiers, automated by a wrapper subcommand. `results.jsonl` is kept forever — each row is a few hundred bytes, a decade of weekly sweeps is on the order of megabytes, append-only. Per-run directories are kept uncompressed for the most-recent few sweeps, then compressed to one tarball per sweep (transcript, telemetry, artifact, metrics, the resolved prompt) and the loose directory removed. Tarballs are kept at least a year, flagged in the report footer before any disk-pressure deletion, never auto-deleted. Pulling back an old run un-tars on demand. We stay on jsonl rather than parquet: append is trivial, `jq`/`awk` work out of the box, and the volume is far too small to justify the extra dependency.

### The improvement loop

Cycle: sweep → analyse (which projects failed, which burned the most tokens, which had the biggest delta vs TS/Py) → hypothesise a backlog change → branch and ship → sweep candidate → A/B-compare → accept or revert. The improvement pillar is downstream of the harness: bug reports come *out* of the harness.

**Attempt model — two-shot with feedback (Aider's design).** First attempt blind; on failure, the model gets the rubric output and tries again. This separates first-shot ability from fix-iteration ability — both matter, and Dark's tight-feedback story (`fn` → `run` → trace) should win disproportionately on the *second* attempt. The harness reports pass@1, pass@2-with-feedback, and **fix-iteration delta** = pass@2 − pass@1; we expect Dark's headline win to live in that delta.

### Agent task prompt template

What the wrapper sends to the agent is reproducibility-critical: its content hash is part of every sweep ID, so changing it starts a new measurement series.

Three messages per attempt: a system-prompt augmentation (the agent's CLAUDE.md), an initial task message (templated over `spec.md`), and — only on pass@2 — a feedback message templated over the failing rubric output. The system prompt names the language's tools and workflow; for Dark it points at `fn`/`run`/`commit`, the first-look commands (`docs for-ai`, `tree`), and the syntax gotchas (full path on `fn`, semicolon-separated lists, `++` concat and `$"…"` interpolation, type-free match patterns, top-level-only function definitions, parens around complex pipe LHS). The retry message, for Dark, adds the trace tip — `traces tail` and `traces replay <id>` — which is the cheapest possible lever on the fix-iteration delta and itself a candidate improvement wave to A/B. (Adding a `--diff` to `replay` is one of those waves, not an existing flag — see traces-and-debugging.md.)

What the template **excludes**: example solutions (would compress the cross-language signal), mentions of specific rubric tests (specs describe behaviours, rubrics encode checks), be-terse/be-verbose instructions (we measure tokens, we don't game them via the prompt), and CoT scaffolding (Claude Code has its own; layering ours distorts the comparison). What it **includes**: tool-affordance hints, which are fair game because available tools are an objective property of each environment, not a thinking style. Rule of thumb — an instruction is okay if it's true regardless of which approach the agent takes.

Reproducibility knobs, pinned and folded into the prompt-template hash: `temperature=0` (the eval bench wants determinism; higher temperatures multiply variance), a generous max-output-tokens-per-turn so complex tasks aren't truncated, and a max-turns cap as a runaway detector (the cost budget is the real cap). Defer top_p/top_k to provider defaults; derive a seed from `run_id` where the provider supports one. Change any knob and you've started a new measurement series.

### Orchestration and cadence

The orchestrator (queue schema, branch isolation, telemetry correlation, rate-limit handoff, phase tracking) and the scheduled-sweep cadence (what runs, ordering, sweep-ID naming, failure handling, cost-cap escalation, alert thresholds) are specified in their own companion documents. The dashboard and exportable per-sweep / over-time reports likewise live in a dedicated spec.

## Implementation order

The target is the north-star above: two local release builds syncing, one server and one client, with branches, efforts, and experiments completed, all written in Dark using AI. The harness is what tells us whether each step toward that target actually helped. Work it in roughly this order:

1. **Skeleton harness, end-to-end.** Collect one trustworthy row of Dark-vs-TS-vs-Py data on a tiny project subset (`hello-cli`, `csv-to-json`, `url-shortener-cli` — the last is the only one that puts Dark in a winning posture). It need not be parallel, fast, or pretty; it needs to produce real numbers. Sequential runs, prompt caching off for honest first-pass numbers, strict constraint mode, per-run telemetry isolation via `.darklang` config. Done when a sweep runs to completion, writes its rows, and the report renders the populated headline metrics — the numbers don't have to look good, they have to be real.

2. **Scale and harden.** Grow to a few dozen projects; add parallelism, the pass@2-with-feedback attempt model, mutation-tested rubrics as a hard entry gate, the first HTTP projects (with the readiness/retry handling above), and prior-sweep regression callouts in the report.

3. **First improvement waves (the A/B protocol).** Convert "we shipped a Dark improvement" into "we have data showing it helped." One backlog hypothesis per wave, landed on a branch off `main`; baseline sweep (reuse a recent `main` sweep when fresh enough), candidate sweep against the branch's rebuilt CLI, then an A/B report with per-metric deltas, significance, and per-project regression flags. Merge only if enough headline metrics move positively and none regress meaningfully; otherwise keep the branch open and write the retro anyway — negative results are data. Order waves cheapest-first: a **prompt-only** bundle first (zero Dark code — also a harness sanity check: if the bench can't detect a prompt change, it's too noisy to drive Dark investment), then a parseable-output rollout, then the authoring headliners (`dark edit` + auto-diagnostics, the biggest predicted token win), then an error-UX bundle, then a `dark publish` MVP that unlocks the share-with-a-friend narrative. Each wave runs the protocol independently; a failed wave re-baselines before the next starts.

4. **Sync to the north-star.** With the loop proven on greenfield builds, turn it on the deliverable itself: two release builds — an always-on desktop server on the Tailscale network and a client — syncing branches, efforts, and experiments over the [sync.md](sync.md) substrate, all of it authored in Dark by the agent. The wire carries only ops and commits; both ends regenerate their projections locally; remotes are configured through `.darklang` config, never environment variables.
