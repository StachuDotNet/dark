# PDD Experiment — Loop Entrypoint

> This is the file the `/loop` command points to. Read it first each iteration.

## Branch

`pdd` (local-only, off `main`, **never push**)

## What this is

An experimental fork of the Darklang F# codebase to prototype **pseudocode-driven development as a runtime feature**. Not "AI-generated stubs the human fills in later" — but "the interpreter itself drives the LLM to materialize code on demand, in parallel, speculatively."

Background and full thinking: `thinking/pseudocode-driven-dev-2026-05-13.md` (already on the AOT branch, also accessible here on `pdd` if you cherry-picked).

## Optimization target

**Make Stachu happy at 8am with a good start to a few days of hacking on this.**

Design > code. Cut corners. Don't push. Commit often.

## Files in this directory (`pdd-thinking/` at repo root)

Originally placed in `notes/pdd/` but moved here so they're tracked
(repo's `notes/` is gitignored, this branch wants them committed).

Read in order:

1. `00-LOOP-SUMMARY.md` — this file (entry point)
2. `01-vision.md` — algorithm + paradigm (what we're building)
3. `02-libexecution-changes.md` — **the most important file** — F# interpreter changes
4. `03-find-vs-generate.md` — the two-coroutine scheduler, <1s default budgets
5. `04-signature-consensus.md` — coordinating parallel attempts on the same name
6. `05-tolerant-runtime.md` — partial values, holes, recovery
7. `06-builtin-permissions.md` — capability model for wild generated code
8. `07-human-in-loop.md` — when does the human enter
9. `08-tracing-as-artifact.md` — the trace *is* the program
10. `09-carving-the-codebase.md` — what F# / packages to cut for this experiment
11. `10-day-1-hacking-plan.md` — what to actually type when you sit down
12. `11-open-questions.md` — things I (Claude) am unsure about
13. `12-glossary.md` — terminology (pin these names down)
14. `13-libpdd-materializer.md` — concrete F# shape of the new `LibPDD` project
15. `14-demo-programs.md` — six end-to-end test programs (Demo 6 is the north star)
16. `15-spike-budgets.md` — engineering time / API $ / cognitive budgets
17. `16-prompt-shapes.md` — actual prompt templates with gpt-4o-mini outputs (v3 prompt is the keeper)
18. `17-day-1-quick-reference.md` — **single-page at-the-desk cheat sheet** (print this!)
19. `18-minimum-viable-spike.md` — if you only have 4 hours: the tightest possible Demo-1 path
20. `19-red-team.md` — what could go wrong: design risks, impl footguns, project risks, smoke detectors
21. `20-elevator-pitches.md` — 60s / 3-min / 10-min pitches for different audiences

Plus:
- `progress.md` — running log of each loop iteration (20+ iterations as of 02:23 EDT)
- `FINAL-REPORT-2026-05-13.md` — **the durable summary**, drafted iter 15, polished after

## What "iterate" means in this loop

Each wake-up:
1. Read `progress.md` to see where I am.
2. Pick the next file/topic to deepen. Order is a guide, not a rule.
3. Write/refine substantive content. **Concrete sketches > generalities.**
4. Commit with a descriptive message.
5. Append a 2-3 line entry to `progress.md`.
6. Schedule the next wakeup.

Wake cadence: tightened to 270s (4.5 min) per user request mid-night — stays inside the prompt-cache TTL so each iteration re-uses warm context.

## Hard rules

- **Never push** this branch.
- **Commit frequently** — every meaningful chunk of writing, even mid-doc.
- **Cut whatever you want** from F# code. Disable projects in `.sln`, delete dirs, comment out builtins. This is throwaway.
- Respect the no-stash / no-hard-rebase / keep-`_assert-in-container` rules from memory.
- **By 08:00 EDT (12:00 UTC) 2026-05-13**, stop iterating, finalize `FINAL-REPORT-2026-05-13.md` (≤5 printed pages), and call `~/bin/print-md` on it.

## Stachu's specific guidance this session

- **Fork the F# codebase here.** Branch `pdd`. Commit a lot, never push.
- **Cut corners freely.** Anything not needed for this experiment — delete, comment out, disable in fsproj/sln.
- **LibExecution is the key.** If we figure that out, the rest follows. **Spend the bulk of design effort here.**
- **Expand on the algorithm.** Human-in-loop, sig consensus — both deserve their own files.
- **Both `find` and `generate` should give up in <1s** by default. Configurable up to ~1min for "I care about this fn". Empty body is fine. Just signature is fine. Anything to keep moving.
- **Tolerant runtime** — at first, very. Tighten over time.
- **Builtin restrictions early.** CLI installers should let users pick what agents can do (HttpClient yes/no, file access yes/no, etc.). Don't punt on this.

## Endgame for this session — STATUS as of iter 20 (02:24 EDT)

When Stachu sits down at his desk at 8am:

- ✅ This directory has 21 design files + final report + progress log, all substantive.
- ✅ `02-libexecution-changes.md` has concrete F# code sketches with line numbers (`Interpreter.fs:317`, `RuntimeTypes.fs:88,1250`).
- ✅ `09-carving-the-codebase.md` has a precise list of what to disable first (`fsdark.sln:44,61`).
- ✅ `10-day-1-hacking-plan.md` gives a step-by-step "what do I type first" plan, 6 phases.
- ✅ `17-day-1-quick-reference.md` is a **single-page at-the-desk cheat sheet** with v3 prompt verbatim.
- ✅ `18-minimum-viable-spike.md` has the 4-hour fast cut if morning is short.
- ⏳ Printed final report on desk — pending the 06:30-07:00 EDT print run.
- ✅ All 20 iterations committed on `pdd`. Never pushed. AOT branch backed up to `origin/aot-and-cold-start-improvements`.
- ✅ OpenAI key safely at `~/.config/darklang/llm-keys.env` (mode 600, outside repo).
- ✅ ~$0.001 spent verifying the JSON-prompt path works with gpt-4o-mini. ~$9.999 of $10 remains.
