# Hot Reload

Edit a fn, value, or schema — and every view, sub-app, agent, and peer that
depends on it picks up the change without restart, ceremony, or stale-cache
surprises.

Mechanically, hot-reload is **one consumer of a `BodyChanged` event**: when a
definition's body changes, the substrate publishes `BodyChanged hash` on the
event bus, and the parties that depend on the old body react. It is not a
separate subsystem — it is a subscription pattern over the same bus everything
else rides (see [event-bus.md](../pre-s-and-s/event-bus.md)).

## UX intention

- **Edits feel instant.** Save in one place; the rest of the system catches up
  on its own.
- **Failure is visible, not hidden.** If a propagation breaks a dependent, the
  dependent surfaces the break — it does not silently keep running the old
  version.
- **The author chooses the blast radius.** Some changes are experiments (this
  branch only). Some are meant to ripple (push to dependents). Both are
  first-class.
- **Reverting is symmetric.** Anything that propagates can be un-propagated with
  the same shape of action.

## Mechanism: `BodyChanged` as an event

The durable fact is the op that changes the body — a commit in the op stream.
Editing a definition appends that op; the substrate then publishes a
`BodyChanged hash` event derived from it. The bus already lists this kind (see
[event-bus.md](../pre-s-and-s/event-bus.md), Event kinds):

| Kind | Producers | Subscribers |
|---|---|---|
| `BodyChanged hash` | hot-reload, refine, sync | frames using the old body, viewer |

Three producers, one event shape:

- **Local edit** — the author changes a definition; the edit op is appended and
  `BodyChanged` is published.
- **Refine** — a materializer or agent rewrites a body; same op, same event.
- **Sync** — an op arriving over the wire changes a body the local instance
  depends on; the sync layer republishes it as `BodyChanged` locally.

Subscribers decide what "react" means for them:

- A **frame** parked on or actively using the old body re-derives against the
  new one, or surfaces a break if the new body no longer type-checks against its
  use site.
- A **viewer** re-renders the affected pane. Its Model is a projection of bus
  events, so a `BodyChanged` is just another event folded in.
- A **dependent definition** re-checks against the new signature; a break
  becomes a visible diagnostic, not a silent stale call.
- A **peer** receives the underlying op over sync and runs this same path
  locally — hot-reload across machines is sync plus a local `BodyChanged`.

## Ops vs projections

The op (the body-change commit) is the durable, syncable fact. `BodyChanged` is
the ephemeral signal that says "a body you care about moved — re-derive." The
bus marks this bus **not durable**: the `commits` table is the durable form, so
hot-reload never needs to replay the signal — a fresh instance rebuilds by
replaying ops, and re-derivation falls out of normal evaluation.

Everything a dependent shows after a reload — a re-rendered view, an updated dep
graph, a fresh diagnostic — is a **projection** recomputed from the new op
state, not a separately mutated cache that has to be invalidated by hand.

## Constraint on projection design

> Projections and their usage patterns should be designed with hot-reload in
> mind. A projection that can't be cheaply re-derived, or whose consumers can't
> be told "your input changed," is a projection that fights hot-reload.

Two re-derivation styles want different invalidation, and the bus accommodates
both:

- A projection that **re-derives lazily** (recompute on next read) just needs to
  drop its cached value when it sees a `BodyChanged` for an input it depends on.
- A projection that **subscribes to a stream** re-runs its fold incrementally as
  `BodyChanged` events arrive.

Either way the contract is the same: a projection declares the inputs it depends
on, so the bus can route the relevant `BodyChanged` to it. A projection with no
declared inputs cannot be told its world moved — and that is the shape to avoid.

## Open questions

- **Break surfacing.** When a reload breaks a dependent, where does the break
  show — at the changed definition (the cause), at the dependent (the effect),
  or both? Likely both, linked.
- **Blast-radius UX.** "Experiment on this branch" vs "push to dependents" needs
  a concrete gesture; it overlaps with the sharing modes in
  [sync.md](../stable-and-syncing/sync.md).
- **In-flight frames.** A frame mid-evaluation against a body that just changed:
  finish on the old body, or restart on the new one? Default to finishing the
  in-flight call and applying the new body on the next call.
