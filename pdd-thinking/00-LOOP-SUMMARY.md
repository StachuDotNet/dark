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
17. `progress.md` — running log of each loop iteration
18. `FINAL-REPORT-2026-05-13.md` — written + printed at 8am (don't read mid-loop, it doesn't exist yet)

## What "iterate" means in this loop

Each wake-up:
1. Read `progress.md` to see where I am.
2. Pick the next file/topic to deepen. Order is a guide, not a rule.
3. Write/refine substantive content. **Concrete sketches > generalities.**
4. Commit with a descriptive message.
5. Append a 2-3 line entry to `progress.md`.
6. Schedule the next wakeup.

Wake cadence: 20-30 min per iteration is fine — design work, not polling.

## Hard rules

- **Never push** this branch.
- **Commit frequently** — every meaningful chunk of writing, even mid-doc.
- **Cut whatever you want** from F# code. Disable projects in `.sln`, delete dirs, comment out builtins. This is throwaway.
- Respect the no-stash / no-hard-rebase / keep-`_assert-in-container` rules from memory.
- **By 8am Stachu's time (UTC… need to figure that out — see progress.md)**, stop iterating, write `FINAL-REPORT-2026-05-13.md` (<=5 printed pages), and call `~/bin/print-md` on it.

## Stachu's specific guidance this session

- **Fork the F# codebase here.** Branch `pdd`. Commit a lot, never push.
- **Cut corners freely.** Anything not needed for this experiment — delete, comment out, disable in fsproj/sln.
- **LibExecution is the key.** If we figure that out, the rest follows. **Spend the bulk of design effort here.**
- **Expand on the algorithm.** Human-in-loop, sig consensus — both deserve their own files.
- **Both `find` and `generate` should give up in <1s** by default. Configurable up to ~1min for "I care about this fn". Empty body is fine. Just signature is fine. Anything to keep moving.
- **Tolerant runtime** — at first, very. Tighten over time.
- **Builtin restrictions early.** CLI installers should let users pick what agents can do (HttpClient yes/no, file access yes/no, etc.). Don't punt on this.

## Endgame for this session

When Stachu sits down at his desk at 8am:

- This directory has 10+ design files, each substantive.
- One of them (`02-libexecution-changes.md`) has concrete F# code sketches he can start coding from.
- One of them (`09-carving-the-codebase.md`) has a precise list of what to disable first.
- One of them (`10-day-1-hacking-plan.md`) gives him a "what do I type first" plan.
- A printed final report is on his desk (5 pages, dense).
- All committed, all readable in his preferred order.
