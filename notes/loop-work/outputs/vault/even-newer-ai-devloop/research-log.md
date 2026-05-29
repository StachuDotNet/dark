# Research log — retired

> **No longer maintained.** Per round-2 user feedback (2026-05-05): *"not really helpful to me, and is far too long for me to bother reading."* Stopped appending iter 81; archived in full to [`samples/historical/research-log-loops-1-3.md`](samples/historical/research-log-loops-1-3.md) (1009 lines, 80+ iterations across three loops).

## Where the iter-by-iter discoveries live now

| Type of finding | New home |
|---|---|
| Status of the loop's task list | [`feedback-plan.md`](feedback-plan.md) — rolling status section at bottom |
| Verified Dark CLI capabilities + gotchas | [`improvements.md`](improvements.md) + [`improvements/`](improvements/) (per-recommendation files) |
| Bench project specs | [`projects/`](projects/) (22 standalone agent-facing spec files) |
| Decisions / trade-offs / surveys / citations | [`plan-analysis.md`](plan-analysis.md) |
| Harness operational spec | [`plan.md`](plan.md) |
| Reflection template (per-project post-mortem) | [`reflection-template.md`](reflection-template.md) |

## Why this file used to exist (and why it stopped)

The 5-min loop ran in three phases:

- **Loop 1** (iters 1-40 + final-validation): bootstrapping the plan + verifying Dark CLI claims by direct probe. The log captured what worked, what didn't, and what surprised the agent. Most of those findings have since migrated into the load-bearing docs.
- **Loop 2** (iters 41-80): scope expansion via Multi orchestration + materializing per-project specs.
- **Loop 3 / round-2** (iters 81+): user came back with 24 points of feedback; the work shifted from research to restructuring (per-file splits, queue mechanism, reflection template, cross-language extension). At that point the log stopped earning its keep — every change is now visible in the file structure itself, so a per-iter narration would just duplicate what `git log` (in repos that aren't gitignored) or the rolling status line already shows.

The archived log is kept for one reason: if a future contributor wonders *why* a particular doc looks the way it does ("why is `dark search` covered in §3.1 but `traces find` only in §3.3?"), the iter-by-iter narrative explains the audits + decision points behind each split.

## If the loop ever resumes appending

Not the default. If round-3 feedback says "actually keep the log going," restore the prior `## Format` block from the archive's first 10 lines and pick up at iter ~109+1.
