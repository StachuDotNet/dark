# Hot Reload — intention only

> **Placeholder.** Revisit when projections + UX patterns are closer
> to shape. Do not implement yet.

## What we want, in one sentence

Edit a fn, value, or schema — every view, sub-app, agent, and peer
that depends on it picks up the change without restart, ceremony,
or stale-cache surprises.

## UX intention

- **Edits feel instant.** Save in one place, the rest of the system
  catches up on its own.
- **Failure is visible, not hidden.** If a propagation breaks a
  dependent, the dependent surfaces the break — it does not
  silently keep running the old version.
- **The author chooses the blast radius.** Some changes are
  experiments (this branch only). Some are intended to ripple
  (push to dependents). Both are first-class.
- **Reverting is symmetric.** Anything that propagates can be
  un-propagated with the same shape of action.

## Why this is parked

The shape of hot-reload is downstream of how projections work. A
projection that re-derives lazily wants different invalidation
semantics from one that subscribes to an event stream. Designing
hot-reload before projections settle means designing it twice.

## Constraint on other designs

> **Projections and their usage patterns should be designed with
> this in mind.** A projection that can't be invalidated cheaply,
> or whose consumers can't be told "your input changed," is a
> projection that fights hot-reload later.

That's the whole reminder. Come back to this doc once projections
have a usage pattern worth wiring to.
