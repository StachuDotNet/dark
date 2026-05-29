# Process risks

Things that can quietly corrupt the loop's output if left unguarded. These are
*process* risks (how the work gets made), distinct from the bench's own design
risks (which live with the harness spec).

## Self-grading skews positive

An agent grading its own work reliably skews favourable. The loop's own
reflection template builds in a hedge: the agent reports self-confidence
*separately* from the actual outcome, and the outcome is judged by an
independent arbiter the agent never controls. The same principle applies to the
loop itself — the loop's claim that an iteration "added value" should be
checkable against an external artifact, not taken on the loop's word.

## False completion

A long-running loop can declare a job done because progress is visible, without
the job actually being finished. Guard: completion is defined by a falsifiable
artifact existing and passing, never by the agent asserting "done." A loop that
can mark itself complete on vibes will.

## Drift between docs and ground truth

Claims age. A capability list written early goes stale as the underlying system
changes, and nothing in a docs-only loop forces a re-check. Periodic
verification passes (re-running the probe behind a claim) are the only thing that
keeps a maturing doc set honest. Without them, confidence accretes faster than
correctness.

## Cross-reference rot in a growing artifact

As an artifact grows, internal references drift — a number gets renumbered, a
pointer goes stale, a table gets duplicated. The loop handled this with periodic
compaction passes whose value was *consistency*, not size reduction. The risk is
skipping them: drift compounds silently and a reader eventually trips on a
contradiction.

## Throughput masquerading as progress

A loop optimized to "keep iterating" will always find something to do. Iteration
count is not progress. The honest measure is whether each pass moved a real
question forward or made real content more findable — and that measure has to
come from outside the loop, because the loop will always rationalize the next
pass.
