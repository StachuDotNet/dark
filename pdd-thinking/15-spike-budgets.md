# 15 — Spike Budgets

What you're spending, what to watch, when to stop.

## Three axes

| Axis | Budget | Tripwire | Hard stop |
|---|---|---|---|
| **Engineering time** | 2 weeks of focused hacking | 1 week with no Demo 1 running | 3 weeks with no Demo 6 running |
| **OpenAI API $$** | $10 | $7 used | $9.50 used → spike paused |
| **Anthropic API $$** | (whatever the existing key is) | TBD | TBD |
| **Cognitive load** | "I can hold the whole thing in my head" | "I'm reading 4 docs to remember why X" | "It's not fun anymore" |

The last axis matters most. If the spike stops being fun, stop the spike.

## Engineering time, by day

| Day | Target | If you miss |
|---|---|---|
| 1 | Phases A-F of `10-day-1-hacking-plan.md` — `FQFnName.Pending` + stub materializer + first F# test green | OK to bleed into Day 2; the carving phase is the most likely tarpit |
| 2 | Real materializer with OpenAI call; Demo 1 (`addOne`) returns 6 | If carving still blocking, just turn carving off and proceed with full build |
| 3 | Demos 1 + 4 green; basic trace output | Iterate on `tryGenerate` / JSON parsing — usually where Day 3 burns |
| 4 | Recovery wired; Demo 5 (tolerance) green | If recovery is gnarly, ship `KillFrame`-only and skip Demo 5 for now |
| 5 | Demo 2 (stock variance) — six pendings in parallel | If scheduler/parking isn't working, run pendings serially — still proves the idea |
| 6 | Demo 3 (recursive fib) — self-reference works | Hardest of the early demos; expect bumps |
| 7 | Internal review, polish, iterate on the trace UX | Buffer day |
| 8-9 | Demo 6 prep — HN headline, capabilities, multi-pending impure | This is where it gets ambitious |
| 10 | Demo 6 green | If not, defer to Day 12; do not burn out chasing |
| 11-12 | Stretch demos, trace tooling, blog/video writeup | |
| 13-14 | Demo 7 (recursive descent), or polish, or move on | |

**The single most important indicator is Day 3:** if Demos 1 + 4 are green on Day 3, the spike is healthy. If they're not, something's structurally off — back up to the day-1 plan and figure out which phase didn't actually finish.

## OpenAI dollar budget — what $10 buys you

Per the tonight verification: gpt-4o-mini, JSON-shaped output, ~66 input + ~20 output tokens per call ≈ $0.000033 per call.

| At cheap-rate | $10 buys |
|---|---|
| Average call (per above) | ~300,000 calls |
| 5× larger prompts (realistic for real demos) | ~60,000 calls |
| 5× larger + Sonnet/4o-full | ~3,000 calls |

So **at gpt-4o-mini you literally cannot run out in a sane spike**. The $10 risk is essentially "what if I accidentally hit Sonnet 1000 times." Mitigation:

- Hardcode model = `gpt-4o-mini` in the spike. No flag override yet.
- Add a $-counter that prints estimated spend after each call (`prompt_tokens * 0.00000015 + completion_tokens * 0.0000006`).
- If estimated spend ever crosses $5 in a session, abort and re-think.

## Token / cost telemetry

Every LLM call's `usage` block goes into the trace as a `cost` event:

```json
{ "t": 312, "ev": "cost", "model": "gpt-4o-mini", "in": 66, "out": 20, "dollars_est": 0.000033 }
```

`dark pdd trace cost <id>` sums them up and prints:

```
Total: $0.0042 across 127 LLM calls
  gpt-4o-mini:   127 calls, avg 80 in + 22 out tokens
  By demo:
    addOne:    $0.0001  (3 calls)
    fib:       $0.0008  (32 calls)
    ...
```

This is the single most useful piece of telemetry for a research spike. Build it early.

## Cognitive budget

Things that signal "the spike is getting too complex":

- I'm not sure which doc to read first.
- I forgot what `Pending.handle` is for.
- Two recent changes contradict each other.
- I keep referring to `progress.md` to know what's happening.
- I want to add a 16th .md file just to make sense of the other 15.

If any of these hit: stop. Read the glossary (`12-glossary.md`). If still confused, read `00-LOOP-SUMMARY.md`. If still confused, take a break.

## When to stop

| Situation | Action |
|---|---|
| Demo 6 green by Day 10 | Write the blog/video. Make it real. |
| Demo 6 green by Day 14 | Ship it as-is. Polish later. |
| Day 14, Demo 6 not green | Stop. Write a postmortem. What blocked? Did the design hold up? Pivot or abandon, but don't muscle through. |
| API spend exceeds $5 with no Demo 6 progress | Stop. Something's wrong with the materializer feedback loop. |
| Adding a doc just makes things worse, not clearer | Stop adding docs. Code. |

## What success looks like

Three levels:

### Bronze
- The runtime materializes one function via LLM and executes it correctly.
- The trace records the materialization.

This is the minimum-viable claim. Day 2-3.

### Silver
- A multi-step program (Demo 2: stock variance) runs with all pending fns materialized.
- Materialization happens in parallel (scheduler works).
- Some recovery happened along the way (tolerance works).

This is the spike's normal headline. Day 5-7.

### Gold
- Demo 6 runs. The user types one prompt and gets a working answer.
- The trace is the artifact you keep — source + cache + trace shipped together.
- Reviewing the trace feels like a *natural* dev activity, not a chore.

This is the paradigm claim. Day 10+.

## What failure looks like (so you know it when you see it)

- Scheduler complexity exceeds your mental capacity → simplify to serial materialization (slower but right).
- Sig consensus is constantly wrong → either Strategy B is needed sooner, or the LLM needs better prompting (revisit `16-prompt-shapes.md`).
- Recovery papers over bugs in your own code, not just LLM hallucinations → tighten the recovery policy to strict for non-materialization errors.
- Trace files are gigantic and useless → roll up calls; defer non-PDD-specific events to the existing tracing.

## A nice-to-have failure mode

If the spike *succeeds* but the paradigm feels wrong — i.e., it works but you don't actually want to use it — that's the most valuable result. Write down why. That's the next research project.

## Decision: when do you push the branch?

**Never during the spike.** This branch is throwaway. If something on `pdd` should live in `main`, cherry-pick the relevant commits onto a new branch and push *that*.

The branch protection is partly safety (don't pollute origin with experimental commits), partly forcing function (knowing it's throwaway makes you cut corners faster).

After the spike — successful or not — decide: re-implement clean on a new branch, or extract piece-by-piece.

## Connection to other docs

- `10-day-1-hacking-plan.md` — the Day 1 budget specifically.
- `14-demo-programs.md` — the demos that drive the schedule.
- `09-carving-the-codebase.md` — the carving has its own 60-minute stop-loss.
