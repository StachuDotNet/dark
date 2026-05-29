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

## PDD design + bench risks to watch

Distinct from the process risks above (how the work gets made), these are risks
in the *thing being built* — the PDD substrate and its bench — carried forward
from the red-team. They are the failure modes worth instrumenting the bench
against, not loop-integrity hazards.

- **Trace-replay divergence.** If a trace doesn't capture enough context (LLM
  nondeterminism, network, time), replays diverge and the "trace as a replayable
  value" framing breaks. Probe: take a trace, replay it 10 times, count matches;
  below 9/10 forces a re-think. Load-bearing because the whole traces-as-values
  story (`event-bus.md`) assumes replay is faithful.
- **Tolerance hides logic bugs.** Loose-mode-by-default lets a divide-by-zero
  quietly return 0 — the program looks fine but is silently wrong. Mitigation: run
  tests in strict mode periodically; don't let `EmptyBody`/default-substitution
  cover errors that are not non-materialization.
- **Sig-consensus thrash.** If LLM-generated signatures disagree with find-result
  signatures often enough (say ≥30%), a first-wins policy floods the trace with
  noise. Needs the constraint-driven consensus the coordinator's judge reaches for
  (`algorithm.md`).
- **Feels-wrong-even-though-it-works.** PDD ships end-to-end, you use it on a real
  task, it works — but it's uncomfortable, unpredictable, hard to debug. The most
  valuable failure mode: notice it, write up *why*, pivot. Hardest to instrument
  because nothing fails loudly.
- **LLM returns invalid Dark.** Already observed (string ops). Mitigation:
  retry-with-AST-feedback — roughly double the cost, roughly half the failure
  rate.
- **`defaultFor` wrong for custom types.** Records need field-by-field defaulting;
  today `TCustomType` returns `DUnit`, which type-checks but is semantically empty.
  Fix when it starts hurting.

## Deferred scope

Not risks but deliberate non-goals — things held out until the foundation exists,
recorded so they don't get re-litigated each pass:

- **Multi-user cloud cache.** Auth/billing/trust; defer until single-user value
  is proven.
- **PDD-LSP / IDE integration.** Wait for a Dark LSP to land first.
- **Speculative materialization on keypress.** Wasteful on tokens — experiment
  with it, don't productize.
- **Fine-tuned per-user models.** Real value, but not a Darklang-team capability;
  partner with an inference vendor rather than own it.
- **Mid-program fn iteration when a downstream constraint fails.** Important, but
  the design space is murky enough to deserve its own spike.
