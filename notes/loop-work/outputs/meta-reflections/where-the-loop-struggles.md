# Where the loop struggles

The process failure modes worth designing against. Companion to
[what-the-loop-is-good-at.md](what-the-loop-is-good-at.md).

## Diminishing returns set in well before the loop notices on its own

The loop explicitly flagged saturation: net-new value per iteration declined as
the doc set matured, and most late work was closing narrow open questions or
de-drifting. Left alone, the loop kept iterating past the point where each pass
earned its cost. The lesson: saturation needs an *external* stop signal. The
loop is good at flagging "value is declining" but bad at acting on it — it will
keep finding marginal work indefinitely.

## The append-only narration outlived its usefulness

A per-iteration research log made sense while the work was genuinely
exploratory. Once the work shifted to restructuring, the log became long and
low-value — the file structure itself already showed what changed, so the
narration just duplicated it. It was eventually retired. The signal: when the
artifacts make their own history legible, a separate running log is dead weight.

## Probes can be inconclusive and still consume an iteration

Some probe iterations couldn't test the thing they set out to test (a server
that wouldn't bind, a multi-request path that wouldn't connect). These weren't
wasted — the *flakiness itself* became a finding — but the loop can't guarantee
a probe answers its original question. Probes should be framed so that "I
couldn't reproduce this" is itself an acceptable, recordable outcome.

## Over-claiming in the scaffold propagates until something checks it

The initial scaffold asserted a list of capabilities that were partly
aspirational. Those claims survived several iterations before a verification
pass corrected them. Unverified assertions are sticky: nothing flags them as
suspect, so they read as settled. Bias every early claim toward "unverified
until probed."

## Long single-lane stretches

Runs of many same-kind iterations in a row (the move-type tracking caught these)
correlate with mechanical, low-judgment work. They get the backlog done but are
the loop's weakest mode — high throughput, low insight. Worth interrupting on
purpose rather than riding to exhaustion.

## "Out of work" is fuzzy — there's always more mechanical work

The intended stop condition was "stop when you run out of work that doesn't need
the user." In practice that line is blurry: there is almost always *another*
mechanical task available — one more spec to materialize, one more compaction,
one more stub to fill. The loop tended to keep finding such work rather than stop,
and items it had explicitly flagged as skippable got pulled back in the moment the
user nudged. The only thing that made the stop crisp was writing an *explicit
stop-condition entry* — naming what is genuinely blocked on user input versus what
is merely more-of-the-same — and treating that written log as the gate. Without
that deliberate act, "I could do one more" wins indefinitely.

## Building scaffolds ahead of the data they describe

The loop produced polished artifacts (a dashboard mock, pre-defined metrics) before
any real data existed to populate them. This is genuinely useful — it lets an
implementer build to a fixed target — but it carries a quiet risk: a mock with
hand-written, narrative-sounding numbers can read as if it reflects real results.
The guard is to keep placeholder content unmistakably a placeholder, and to never
let a scaffold's invented figures leak into anything a reader could mistake for an
outcome.
