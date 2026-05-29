# Traces and debugging

Dark's trace surface is far more developed than commonly assumed — sixteen-plus `traces` subcommands including `replay --diff`, `gen-test`, `hotspots`, and `inspect`. The work here is almost never about building new primitives. It's about making the agent **reach for the projections that already exist** by default, and closing the one or two real `--json` gaps. The lens: the ops (recorded executions) are all there; the agent just doesn't consume the projections without a cue.

## Auto-attach the latest trace to a failing `run`

**Issue**: when `dark run X args` exits non-zero or returns a `Result.Error`, the agent has to first notice it failed and then call `traces tail` separately to see what happened — a discovery hop it doesn't always take, especially for the silently-wrong `Result.Error` case.

**Candidate fix**: on a failing `run`, the wrapper (or the agent prompt) follows up with `traces tail` automatically and feeds it into the next turn. Surfaces *for the agent* what Dark already has *for the human*. This is the expected single biggest win in this category.

## `run --replay <trace-id>` shorthand

**Issue**: re-running the same inputs against fixed code today means `traces replay <id> --diff` — long, two-step, with a manual ID hand-off.

**Candidate fix**: every failing `run` returns its trace ID prominently, and `dark run --replay <prefix>` is sugar for "re-run the same inputs against current code, diff against recorded output." The bug-fix loop compresses to one command per fix attempt. Twin signal with auto-attach above — the two together are the headline movement for this category.

## Promote `gen-test <trace-id>` in agent docs

**Issue**: `traces gen-test <id>` already turns a recorded execution into a regression test, but the agent has no reason to know it exists, so even agents that reach for traces don't capture them as tests.

**Candidate fix**: a line in `docs for-ai` — "after any successful `run` of a non-trivial fn, consider `traces gen-test <id>` to capture it as a regression test." Turns "did I write a test?" into a one-token answer. Zero Dark-code change; bundles into a prompt-only wave.

## `hotspots` as a built-in review pass

**Issue**: agents finish projects without considering performance. `traces hotspots` lists the slowest fns from recent traces, but there's no cue, so "ships but slow" passes the rubric silently.

**Candidate fix**: when the agent signals done, the wrapper runs `traces hotspots` over the recent traces and surfaces any fn over ~10 ms — free perf review at no extra agent cost, since the wrapper does the work. Harness-side change, no Dark CLI change. Pairs with the end-of-run summary in agent-workflow.md, which can lead with the slow-fn callout so the human reviewer sees it first.

## `--json` for `traces view`

**Issue**: `--json` exists for `list`, `find`, `stats`, `hotspots`, and `follow`, but `view` silently ignores it — so agents regex the human-formatted view to pull the value at a specific eval point.

**Candidate fix**: add `--json` to `traces view`, emitting a structured `{trace_id, fn, args, result, eval_steps: [{path, value}]}` so the agent parses trace bodies structurally. Fewer trace-extraction errors and fewer "misread the trace and re-fixed correct code" failures. This is the traces-specific row of the broader `--json` rollout in cli-ergonomics.md.
