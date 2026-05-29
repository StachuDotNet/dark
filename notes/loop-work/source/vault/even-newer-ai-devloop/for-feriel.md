# For Feriel

> I had AI iterate on some plans to collect initial numbers. maybe check it out? Sorry, I didn't get a chance to see what you pushed, so we likely overlapped.

> *(Stachu, late evening 2026-05-06. The whole `ai-devloop/` dir is what you have here — copied from my laptop via the vault. Specs only, no code yet. Below is what you need to know if you want to take a swing at it.)*

## What this is

Roughly 10 hours of AI iteration on the plan for a cross-language coding bench: agent builds the same project in Dark vs TS / Py / Go / Rust; we measure tokens, wall time, pass rate, fix-iter delta. Goal is "wake up to real numbers" comparing Dark against the others on a small set of projects.

**State**: spec is done (34/34 round-2 items closed). **None of the executable code exists yet.** The harness wrapper, rubric scripts, reference impls, and Multi-extension are all spec only. So this is a "ready to build" handoff, not a "ready to run" one.

## Where to start reading

You're already in the dir. In priority order:

1. **README.md** — map of everything
2. **feedback-plan.md** — what got iterated on, what landed (rolling status at the bottom)
3. **plan.md** — the harness spec (~9 pages)
4. **launch-checklist.md** — Phase A → D operational runbook for tonight's launch
5. **tonights-queue.md** — the 10 priority projects
6. **blockers.md** — pre-launch checklist + the 4 real blockers + 2 soft concerns

The deeper layers (per-project specs, per-recommendation files, plan-analysis, prompt-template, etc.) are listed in README's file table.

## CLI shorthand

The docs use `dark <cmd>` as shorthand. The actual invocation is `./scripts/run-cli <cmd>` from the repo root. Either:

```bash
alias dark='./scripts/run-cli'   # one line, then docs read literally
```

or substitute mentally. There is no globally-installed `dark` binary.

## Three blockers before anything runs

1. **`evals/` doesn't exist yet.** The harness code goes there (per [`plan.md` §4.0](plan.md#40-harness-layout--language-decided-2026-05-02)) — Python wrapper + per-project rubrics + gold references. That dir is *not* gitignored, so creating it = committing it.
2. **`claude --print "ping"` should work on whoever's host.** Subscription auth lives host-side (per iter-51 correction in [`plan-analysis.md`](plan-analysis.md)); we deliberately *don't* use `ANTHROPIC_API_KEY`. Test before launch.
3. **Multi extension is spec'd, not built.** [`multi-orchestration.md`](multi-orchestration.md) covers the `multi bench` subcommand layer, but tonight you can skip Multi and use a plain Python loop — the bench tasks are independent enough.

## Realistic scope for one sitting

The phasing.md "Phase 1" estimate is 1-2 days. To get something by morning, scope down hard:

- **Min-viable (~6-8h)**: skeleton wrapper (Python) + 5 core projects only + Dark only + markdown report (no fancy dashboard). Skips cross-language, skips Multi, skips gold references, skips per-project rubric files (just exec smoke commands + grade by exit code + spec-match grep).
- **Stretch (~10-12h)**: + 5 more projects (the rest of tonights-queue) + basic dashboard (matplotlib + Jinja2 per [`dashboard-spec.md`](dashboard-spec.md)).
- **No-rush**: spec is enough; build over a week per the original [`phasing.md`](phasing.md) Phase 1 → Phase 2 timeline.

## What I think is worth your time

If you want to spend ~1 hour on this rather than ~8: read `feedback-plan.md` + `plan.md` + skim the 5 core project specs in `projects/`. That's enough to decide whether the spec direction is right *before* anyone writes code. The spec is the load-bearing thing; the code is replaceable.

If you want to spend the night running it: start with the **Min-viable** scope above. The 5 core projects are listed in `tonights-queue.md` (password-gen, cron-describe, markdown-toc, validation-applicative, parser-combinators). Each has a fully-specced `projects/<name>.md` describing exactly what behaviours the rubric checks.

## Cron status

I had a recurring 5-min cron firing the planning loop. **Cancelled** before this handoff — no jobs running.

## Heads-up: terminology choice

The plan called the 5 priority projects "core projects" (used to be "sentinels", the user thought it was too fancy and asked me to rename). All instances renamed; just FYI in case any old chat scrollback says "sentinel" — that's the same set.
