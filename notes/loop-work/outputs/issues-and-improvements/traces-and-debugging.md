# Traces and debugging

Dark's trace surface is more developed than commonly assumed — the real `traces` command (verified against `cli/commands/traces.dark`) already ships `list`, `view`, `tail`/`follow`, `stats`, `find`, `hotspots`, `replay`, and `delete`. So much of the work here is making the agent **reach for the projections that already exist** by default, plus closing `--json` gaps. But a few genuinely-missing pieces are proposals below, *not* existing commands: a `--diff` mode on `replay`, a `gen-test`, and `inspect`. The lens: the ops (recorded executions) are all there; the agent doesn't consume the projections without a cue, and a couple of high-value projections still need building.

## Auto-attach the latest trace to a failing `run`

**Issue**: when `dark run X args` exits non-zero or returns a `Result.Error`, the agent has to first notice it failed and then call `traces tail` separately to see what happened — a discovery hop it doesn't always take, especially for the silently-wrong `Result.Error` case.

**Candidate fix**: on a failing `run`, the wrapper (or the agent prompt) follows up with `traces tail` automatically and feeds it into the next turn. Surfaces *for the agent* what Dark already has *for the human*. This is the expected single biggest win in this category.

## `replay --diff` + a `run --replay <trace-id>` shorthand

**Issue**: `traces replay <id>` exists, but it has **no `--diff` mode** today — it re-runs but doesn't diff the new output against the recorded one, so the regression-test value isn't there. And re-running the same inputs against fixed code is a long, two-step, manual-ID-hand-off path regardless.

**Candidate fix**: two parts. (1) Add a `--diff` to `traces replay` that compares the fresh result against the recorded one — this is the actual regression-testing primitive, and it does not exist yet. (2) Have every failing `run` return its trace ID prominently, and add `dark run --replay <prefix>` as sugar for "re-run the same inputs against current code and diff against the recorded output." The bug-fix loop then compresses to one command per fix attempt. Twin signal with auto-attach above — together the headline movement for this category.

## Build `gen-test <trace-id>`, then promote it in agent docs

**Issue**: turning a recorded execution into a regression test is the natural capstone of the trace loop — but `traces gen-test` **does not exist yet** (the name appears only in a code comment in `Tracing.fs`, not as a command). So there is nothing for the agent to reach for.

**Candidate fix**: (1) build `traces gen-test <id>` — emit a test that pins the recorded inputs to the recorded output, using the `replay --diff` machinery above. (2) *Then* add a line to the composed `docs for-ai`: "after any successful `run` of a non-trivial fn, consider `traces gen-test <id>` to capture it as a regression test." Part 1 is a real Dark-code change (not the zero-code prompt tweak this was mis-filed as); part 2 bundles into a prompt-only wave once part 1 lands.

## `hotspots` as a built-in review pass

**Issue**: agents finish projects without considering performance. `traces hotspots` lists the slowest fns from recent traces, but there's no cue, so "ships but slow" passes the rubric silently.

**Candidate fix**: when the agent signals done, the wrapper runs `traces hotspots` over the recent traces and surfaces any fn over ~10 ms — free perf review at no extra agent cost, since the wrapper does the work. Harness-side change, no Dark CLI change. Pairs with the end-of-run summary in agent-workflow.md, which can lead with the slow-fn callout so the human reviewer sees it first.

## `--json` for `traces view`

**Issue**: `--json` exists for `list`, `find`, `stats`, `hotspots`, and `follow`, but `view` silently ignores it — so agents regex the human-formatted view to pull the value at a specific eval point.

**Candidate fix**: add `--json` to `traces view`, emitting a structured `{trace_id, fn, args, result, eval_steps: [{path, value}]}` so the agent parses trace bodies structurally. Fewer trace-extraction errors and fewer "misread the trace and re-fixed correct code" failures. This is the traces-specific row of the broader `--json` rollout in cli-ergonomics.md.
