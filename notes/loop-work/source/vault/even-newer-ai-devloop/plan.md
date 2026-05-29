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

## 2. Why Dark plausibly wins → see [plan-analysis.md](plan-analysis.md)

The thesis (Dark's structural advantages + improvement-backlog gaps) and §2.1 competitive landscape (Convex / Instant DB) live in [plan-analysis.md](plan-analysis.md). Migrated iter 106 to keep this file focused on the *spec* (what we're building) rather than *why*.

Quick pointer for plan readers: the project's bench has to exercise Dark's actual differentiators (live-programming workflow, traces-as-test-data, persistent package tree, deprecation-as-first-class) — see §4 below for how each lands in the harness, and [improvements.md](improvements.md) for the §3 backlog.

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
      rubric.{dark,ts,py,go,rust}  # one per language; reads artifact path/URL from argv; exits 0/1
      driver.sh           # representative-input perf driver
      # NOTE iter 84 (round-2 P0 #5): no `gold/` directory committed.
      # Per-language gold references live ephemerally:
      #   - Dark: in a `bench-gold-<project>` git branch, accessed via `dark --branch ...`. Built once per quarter; deleted between sweeps to force fresh impls.
      #   - TS / Py / Go / Rust: in `evals/bench/sweeps/<sweep_id>/gold-cache/<lang>/<project>/`. Purged before each new impl, *without peeking*.
      # Eliminates the "gold reference rots in-tree" failure mode (§8 risk).
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
languages: [dark, ts, py, go, rust]
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

Same projects in **5 languages: Dark, TypeScript, Python, Go, Rust** *(scope expanded iter 81 per round-2 user feedback)*. Same agent (Claude Code) given the same task prompt with only the language-specific tooling exposed (e.g. only `dark`, only `npm/node`, only `python/uv`, only `go`, only `cargo`). Compare metrics. Track the gap over time.

**Cadence (revised iter 81)**:
- **Cross-language baseline** (TS/Py/Go/Rust): runs **weekly**, manually triggered. The user (or coworker) kicks it off; not on cron. Numbers are averaged over multiple runs to smooth out the LLM-non-determinism that survives `temperature=0`.
- **Dark sweeps**: run **every day or two**, manually triggered when an improvement is shipping. Not on a fixed cron. The Dark numbers are the moving signal; the cross-language baselines are the relatively-stable comparison floor.

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

1. **Pin the runtime version per language** (extended iter 81):
   - TS: `node:22-alpine`
   - Py: `python:3.13-slim`
   - Go: `golang:1.23-alpine` (or `1.24` whichever is current at sweep time; pin once per quarter)
   - Rust: `rust:1.83-slim-bookworm` (similar quarterly pin)
   - All locked at the sweep_id boundary; changing a runtime starts a new measurement series.
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

### 4.7 Agent task prompt template → see [prompt-template.md](prompt-template.md)

The literal `system.{dark,ts,py,go,rust}.md` / `task.md` / `retry.md` template files the wrapper sends to the agent live in [prompt-template.md](prompt-template.md) (extracted iter 110, ~190 lines, discrete artifact). The hash of these contents is part of every `sweep_id` (§4.5) — change them and you've started a new measurement series.

### 4.8 Orchestration via Multi → see [multi-orchestration.md](multi-orchestration.md)

The Multi-extension spec (`multi bench` subcommands, queue.json schema deltas, branch isolation, telemetry correlation, rate-limit handoff, claude-task phase tracking) lives in [multi-orchestration.md](multi-orchestration.md) — extracted iter 118 (~180 lines, discrete artifact). Cross-references `queue-mechanism.md` (per-task lifecycle) and §4.4 / §4.5 above.

### 4.9 Nightly cadence → see [nightly-cadence.md](nightly-cadence.md)

The nightly cron-fired sweep spec (cron expression, what runs, ordering, sweep_id naming, dependency on prior night, failure handling, cost-cap escalation, on-call alert thresholds) lives in [nightly-cadence.md](nightly-cadence.md) — extracted iter 118 (~170 lines, operational spec). Cross-references `multi-orchestration.md`, `launch-checklist.md`, and `dashboard-spec.md`.

### 4.10 Tonight's launch checklist → see [launch-checklist.md](launch-checklist.md)

The Phase-A-through-D operational runbook for the kickoff sweep (core projects first, full bench second, dashboard render, share/inspect-the-data) lives in [launch-checklist.md](launch-checklist.md) — extracted iter 111 (~170 lines, ops-time content with its own identity). Cross-references §4.8 (Multi orchestration) and §4.9 (Nightly cadence) above + §6.2 (Dashboard) below.


## 5. Open questions → see [plan-analysis.md](plan-analysis.md)

Open questions and resolved-with-iter-pointers list migrated iter 107 to keep this file focused on the spec.

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

#### Sweep-level metrics — expected-outcome accounting (added iter 70)

These were implicit in the iter-65+ expected-to-fail framework but never enumerated as first-class metrics. Each operates on the rows of a single sweep + the project frontmatter's `expected_outcome` value.

22a. **Gap-detection events** (sweep-level, *positive*) — count of runs where `expected_outcome: fail-likely` and the rubric *passed*. Each event = "Dark closed a gap that was previously blocking this spec." High-value signal: should produce a banner in the dashboard ("🎉 Gap closed: parser-combinators flipped from fail-likely to pass — `no-async-primitives` may have landed.")

22b. **Unexpected regressions** (sweep-level, *negative*) — count of runs where `expected_outcome: pass` and the rubric *failed*. Distinct from `harness_flake: true` (which is bench-side breakage). Each unexpected regression deserves a per-project investigation; the dashboard surfaces them prominently.

22c. **Stable-fail tally** (sweep-level, *neutral*) — count of runs where `expected_outcome: fail-likely` and the rubric failed *as expected*. Not a problem; reported for completeness so the dashboard's pass-rate isn't artificially deflated by the 5 expected-fails.

These are **net-aware**: the headline pass-rate-at-budget should already exclude `fail-likely` rows from its denominator (or report two pass-rates: "all projects" vs "expected-pass projects"). **Decision (iter 70)**: report both, but the *primary* north-star number is over `expected-pass` projects only. Otherwise the bench gets dragged by intentional documented failures.

#### Workaround tracking (added iter 70 — flagged iter 67/69 from `parallel-downloader`/`tar-zip-creation`/`redis-driver` specs)

These metrics measure how often Dark agents reach for shell-out workarounds vs idiomatic Dark. Distinguish "Dark wins by being good" from "Dark passes by escaping into bash."

23. **Process-exec-as-workaround count** (diagnostic, Dark-only, per-run + sweep aggregate) — count of `Stdlib.Cli.Process.exec` / `Process.spawn` / `Process.shellPipeline` invocations in the Dark agent's artifact source. Operationally: regex-match the agent's source files for these call patterns; sum.
   - For *expected-pass* projects: any non-zero is a code-smell (the spec didn't anticipate workarounds).
   - For *fail-likely* projects: workarounds are expected and tracked, not flagged. Distinguish "agent shelled out as intended" from "agent shelled out unnecessarily."
   - Tracked per-spec so we know *which* projects pull Dark toward shell-fallback.

24. **Idiomaticity ratio** (Dark-only, sweep-level) — fraction of Dark runs where `process-exec-as-workaround count == 0` for a project that's `expected_outcome: pass`. Higher = Dark's stdlib + idioms are sufficient. Lower = agents are reaching for bash.
   - Threshold: alarm if `< 80%` for the `expected_outcome: pass` subset. That means agents shell out for *more than 1 in 5* projects that *should* be Dark-native.
   - Trend over time: should go up as the §3 backlog ships (e.g. `csv` library port lands → csv-to-json no longer needs hand-rolling, idiomaticity for that spec rises).

25. **Workaround-correlated cost overhead** (cross-cutting, Dark-only, per-spec) — for each project, compute `(median Dark cost when workaround was used) - (median Dark cost when workaround was NOT used)`. Positive values mean workarounds are *more expensive* (agents pay tokens to compose around the gap); negative means workarounds save tokens (ideal-case for an honest agent). Reported in the per-sweep report's harness-health panel for any project where the difference exceeds noise.

#### Harness self-health metrics (added iter 47; track the bench's reliability, not Dark's quality)

These metrics answer "is the harness itself working?" rather than "how is Dark doing?" When they trend wrong, the *bench* needs hardening, not Dark. Diagnostic-tier; not on the dashboard front page. They go in a separate "Harness health" panel in the per-sweep report (§6.2).

22. **Multi-queue settling time** (median) — wall ms between `multi bench enqueue` returning and the first task transitioning to `running`. Measures the §4.8 wrapper-to-Multi handoff. If this trends up, Multi's processor is stuck (queue contention, processor crash, manual pause). Trip threshold: > 30s sustained over a sweep — investigate Multi's processor.
23. **Pricing-config drift** — at end of sweep, compare the wrapper's accumulated cost (per §6.0 formula) against the actual API spend reported by the provider's billing endpoint (Anthropic's `/v1/messages/usage`). Diff > 5% means `pricing.json` is stale or the formula is wrong. Reported in the per-sweep report's harness-health panel: `Computed: $4.12 · Billed: $4.27 · Drift: +3.6%`.
24. **Container startup time (Dark only)** — wall sec from `multi new <branch>` to first ralph-loop iteration log line. Multi's existing telemetry probably tracks this; the bench surfaces it. If trending up, the devcontainer image is degrading.
25. **Sweep-lock contention count** — per-sweep count of times two `python -m harness sweep` invocations tried to overlap (per the iter-46 open Q on sweep-locks). Should be zero in normal operation; > 0 means a cron timing bug or human-launched-while-cron-was-running.
26. **`harness_flake: true` rate, by failure subclass** — a finer breakdown of metric #20. Subclasses: `container-start-fail`, `multi-queue-stuck`, `pricing-drift`, `sweep-lock-contention`, `agent-process-crash`, `network-timeout`. Helps debugging: a flake spike with `container-start-fail` is a Docker issue; with `network-timeout` is an Anthropic API issue.
27. **Cron-firing punctuality** — wall ms between cron expression's nominal time and actual sweep-start. Anomaly detector for missed cron firings or backlog. Should be < 60s except under host load. Not strictly a bench metric (it's a host-health metric) but lives here because the bench depends on it.

These 6 metrics are **always reported in the per-sweep report's harness-health panel**, even on a clean sweep where they're all green. Reason: a panel that goes silent is a panel that gets ignored; a panel that always shows green↗ is one a reader actually checks.

#### Display rules

- Phase 1 ships *all* Headline metrics or it isn't done. Supporting metrics ship best-effort.
- The dashboard plots the four Headline metrics on the same axis (sweep_id) so regressions in any one show up in the same eye-frame.
- Diagnostic metrics get a "diagnostics" tab; not on the front page.
- **Per-wave isolation metrics** (iter 24): each Phase 3 wave has a *primary* diagnostic that should move materially if the wave shipped well. Wave 1 (prompt-only) → #17 time-to-first-fn ↓ + #4 trace adoption ↑. Wave 3 (`dark edit` + auto-diagnostics) → #5 median tokens ↓ + #9 rework ratio ↓. Wave 4 (error-UX) → #13 first-parse-success ↓. The doc-bug batch wave → #16 doc-bug encounters → 0. If the primary diagnostic doesn't move, the wave shipped wrong (or the bench is too noisy). Captured per-wave in [`evals/improvements/<branch>.md`](../evals/improvements/) retros.

The loop should propose deletions/additions to this list as it learns.

### 6.2 Dashboard + exportable reports → see [dashboard-spec.md](dashboard-spec.md)

The dashboard + report-generator pillar (per-sweep report + over-time dashboard, file layout, regen command, render libraries, share protocol) is in [dashboard-spec.md](dashboard-spec.md) — extracted iter 111 (~180 lines, discrete artifact). The hand-built mock preview lives at [samples/dashboard-mock.html](samples/dashboard-mock.html).

---

## 7. Phasing → see [phasing.md](phasing.md)

The Phase 0 → Phase 5+ project plan + the A/B improvement protocol (the loop's accept-or-revert mechanism for Dark improvements) lives in [phasing.md](phasing.md) — extracted iter 118 (~190 lines, discrete project-plan artifact). Cross-references `improvements.md` (the wave catalogue Phase 3 cycles through).


## 8. Risk / failure modes → see [plan-analysis.md](plan-analysis.md)

Risk catalogue + mitigations (rubric-gaming, memorisation, flatter-Dark project selection, reference-impl rot, cost overrun, HTTP flakiness, agent abandonment, dark-serve-non-TTY) migrated iter 107.

---

## References → see [plan-analysis.md](plan-analysis.md)

Vault sources + bench-design ancestors + competitive landscape + Anthropic engineering posts + Claude-Code-best-practices migrated iter 107.
