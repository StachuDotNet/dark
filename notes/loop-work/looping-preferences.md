# Looping preferences (Stachu) — how to run an AI work-loop for me

A reusable brief to point any future "loop" session at. Distilled from running
multi-hour loops with me and the corrections I gave along the way. Read it once at
the start of a loop and keep it in mind.

## The shape of a good loop

- **One living worklist file is the source of truth.** Reorder the incoming
  feedback/spec into a dependency-respecting todo doc first. Each pass: read it, do a
  chunk, update it.
- **Delete todos when they're genuinely done** (well *and* correctly) — the worklist
  shrinks toward empty; empty = done. Add newly-surfaced todos as you go.
- **Large chunks per pass, not dribbles.** Do as much as productively fits, then stop
  at a natural point.
- **Commit locally every pass. Never push** to upstream remotes. Never `git stash` or
  `git reset --hard` to dodge an error.
- **Leave a one-line status** each pass. Keep a frozen copy of every original so I can
  see the "before"; edit the working copies fully freely (be bold).

## What I value (in priority order)

1. **Correctness over throughput.** Iteration count is not progress. When the work is
   genuinely saturated, *stop manufacturing marginal edits* — say so plainly. Don't
   churn already-good docs; that degrades them.
2. **Verify, don't assert.** Assert nothing that an iteration hasn't either checked or
   explicitly flagged as unverified. Probe real ground truth (run the CLI, read the
   actual code) over plausible-sounding claims.
3. **Ground against reality, not a branch experiment.** If I'm on a spike branch, check
   claims against `main` (`git show main:path`), not the working tree. Solid ideas beat
   ideas built on primitives that only exist in the spike. Label spike-only things.
4. **Tight and consolidated.** Fewer, smaller files; little content should live in more
   than one place; reduce total lines. Consolidate or split only where the line is clear.
5. **Dependency order is real.** Identify the buckets and their dependencies; a doc may
   reference only its own or earlier buckets. Re-read until that's actually true.
6. **Surface blockers** rather than guessing past them. Flag what's genuinely blocked on
   my input vs. what's just more mechanical work.

## Show, don't just tell — I like visuals

- **Embedded fake CLI/TUI experiences.** ASCII mockups of the actual terminal session a
  user would see beat a paragraph describing it. A 12-line mocked session is worth more
  than three paragraphs of prose.
- **Dense code snippets that show off the design** — real type definitions, the shape of
  a fn, an op-stream fold. Dense and high-signal, not toy.
- Small diagrams where they clarify. These are *tight* — they fit the "short, not long"
  bar, they don't violate it. A spec should be reviewable by looking at it.

## Habits that earned their keep

- **Adversarial / completeness reads** find real gaps that link-checking can't —
  cross-doc tensions, dropped concerns, over-claims. Worth doing repeatedly.
- **Close the oldest open question end-to-end** (decision + rationale + where recorded)
  before adding new surface.
- **Synthesis once content exists:** rewriting scattered material into a findable
  summary beats appending more.
- **For each intended effort/PR, sketch the shape:** uncompiled high-level code, empty
  fn bodies, pseudocode, good types, tests, UX touchpoints, and prereqs to pull into
  earlier efforts. Iterate on the sketches — they're the real product.
- **Move-type variety** is a health signal: a long run of one kind of move (probe /
  decide / concretize / refine / compact / restructure) means you're low on varied work.

## Hard "don'ts" I've corrected before

- **Don't print (`print-md`) mid-loop.** Printing floods the physical printer. Print
  only once, at the very end, after everything's done — or when I ask. Then in
  buckets/chunks I can pull off the tray.
- **Don't conflate "the agent stopped" with "the work is correct"** — self-grading
  skews positive. Define done by a falsifiable artifact, not a vibe.
- **Don't bury open decisions in a summary doc** — keep them inside the specific doc
  they belong to.
- **Don't write a fat instruction file when a composed/expanding helper would do.**

## Mechanics for an unattended overnight loop

- **5-minute interval**, work in chunks; a pass only fires when idle, so it never
  overlaps itself — keep the interval short so the next pass starts promptly.
- **State recovery:** one file the loop rewrites; atomic writes (temp + rename); a
  per-run lock; parseable task IDs so it only touches its own work. A 3am reboot should
  cost nothing.
- **Headless hygiene** (when driving Dark itself): terse plaintext / structured output,
  no banners/ANSI without a TTY, a deterministic done-signal, readiness signals over
  log-scraping. ("Behaves well headless" is itself a product requirement for me.)
- **Cost:** flat-rate subscription, track tokens as a proxy; warn before a run eats a
  big fraction of my daily quota; never silently switch to a metered key.
