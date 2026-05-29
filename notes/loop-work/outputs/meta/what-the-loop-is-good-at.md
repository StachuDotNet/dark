# What the loop is good at

Reflection on the *process* — the recurring 5-minute loop that produced the
ai-devloop plan. Not a record of decisions (those live in `design/` and
`projects/`); a record of which loop behaviours earned their keep.

## Probing real ground truth, not vibes

The loop's strongest output came when an iteration *ran the actual CLI* and
replaced a placeholder claim with a verified one. Every "stress-test" iteration
that touched the live binary improved the plan's honesty: tracing turned out
richer than the scaffold claimed, deprecation had more lifecycle than memory
implied, and the "single distributable" claim turned out to be aspirational
rather than real. The pattern that worked: assert nothing in the plan that an
iteration hasn't either run or explicitly flagged as unverified.

## Closing long-open questions one at a time

Open questions that sat for many iterations got resolved cleanly when a single
iteration adopted them as its whole job — cost attribution, constraint-mode
policy, framework pinning, storage retention. Picking the *oldest* open question
and resolving it end-to-end (decision + rationale + where it's recorded) was
consistently higher-value than adding new surface.

## Forcing a falsifiable prediction onto every change

The design that the loop converged on — every improvement wave names the one
metric that should move if it shipped right — is itself a product of the loop
noticing that "we shipped something" slides into vibes without a prediction.
The loop applied the same discipline to itself: each iteration named the kind of
move it was making and what new value it added.

## Move-type variety as a self-governor

The loop tracked which *kind* of move each iteration made (probe, decide,
concretize, refine, compact, survey, restructure) and deliberately rotated to
under-used kinds. This kept it from grinding one lane — e.g. after a long run of
spec-materialization it forced a non-spec move. The rotation is a cheap,
legible health signal: a stretch of identical move-types is the loop telling you
it's run out of varied work.

## Synthesis passes once content exists

Late iterations that *rewrote* an existing artifact into an executive summary —
rather than appending more — were among the highest-density outputs. Once the
raw material exists, "make what's here findable" beats "add more."
