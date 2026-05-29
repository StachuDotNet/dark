# Conflicts and resolutions

The load-bearing substrate. The same primitive handles SCM merge conflicts,
runtime missing-name conflicts, capability denials, type-mismatch-on-
materialization, sync disagreements, and human-review timeouts. Building it
well unlocks SCM and sync work broadly — not just PDD.

## Ops, projections, and where conflicts live

State is a timestamped stream of ops. Everything you see — a name resolving to
a hash, a branch's current bodies, a merge result — is a *projection* folded
from that stream. Projection is separate from the ops, and composably so: the
same op stream can be folded into a name table, a dependency graph, a
side-by-side diff, or a conflict list, without any of those folds knowing about
each other.

A **conflict** is therefore not a row in a table. It is *any place a fold can't
unambiguously proceed*. Two ops want the same location. A call references a name
no op has yet defined. A capability is needed but no op granted it. A sync would
fold over a local change. A materialized body's type disagrees with its call
site. A human was asked a question and no answer-op has arrived.

Every one of these has the same shape:

> I want to make progress; I can't yet; here are my options.

The options are what a *resolution* picks among. A resolution is itself an op —
it lands on the stream, and the projection re-folds with the ambiguity removed.
This keeps conflict handling composable across every kind of Dark app:
**sync**, **stability**, and **AI-agent development** are separate concerns that
share one primitive and compose freely.

## The core hypothesis: conflict timings *are* conflict types

The original sketch listed conflicts as an ad-hoc table of sources (SCM,
runtime, capability, sync, ...). The deeper organizing claim is that those
sources collapse onto **the state at which evaluation happens**. A conflict is
characterized by *when* the fold tries to advance:

- **Parse-time** — folding source text into an expression tree.
- **Dev-time** — folding ops while a human or agent is actively editing
  (rename, move, signature change, refactor) against other in-flight ops.
- **Run-time** — folding an expression into a value (the interpreter).
- **Playback** — re-folding a recorded op/trace stream, deterministically,
  on another instance or at another time. A variant of run-time where every
  resolution must replay identically.
- **At-rest** — folding ops into durable projections between sessions (sync
  inbound, merge, snapshot bootstrap).

The hypothesis to develop: **the timing is the type.** A `TypeMismatch` is one
thing at parse-time (a syntactic placeholder), another at dev-time (a refactor
asking each call site to update), another at run-time (substitute or fail),
another at playback (must resolve to the recorded choice). The conflict payload
is shared; the *dispatch behavior keys off the timing*. The rest of this doc is
organized by that spine.

---

## Parse-time conflicts

Parse errors happen *before* a dispatch context exists — no agent identified,
no tolerance mode, no call site. The fold from text to expression tree simply
can't complete over some region.

The composable move: a parse failure does not raise. It folds the unparseable
region into a **placeholder expression** — the same shape PDD uses for a
`Pending` body — and the failure becomes a run-time conflict that fires *if and
when* evaluation reaches that region. Parse-time defers to run-time.

LibParser already carries the seed of this with `OnMissing.ThrowError /
Allow / AllowPending` for unresolved *names*. Extend the same mechanism to
*syntactic* failures: every unparseable region yields a placeholder; the
run-time dispatch decides what to do when execution reaches it. `ThrowError`
(today's strict behavior) keeps raising at parse-time for callers who want it.

This means parse-time has essentially **one** conflict shape — "this region
didn't fold" — and it is uniformly reduced to a run-time conflict. The timing
collapse is clean here.

---

## Dev-time conflicts (SCM op-vs-op) — the important category

This is the most important category, and the original sketch under-served it
with a single example. Dev-time is where two streams of editing ops, produced
by different instances/branches/agents, fold over the *same* locations. Because
ops are content-addressed and timestamped, "conflict" precisely means: **two
ops target one location and neither's effect subsumes the other under the
fold.**

The concrete op-vs-op conflicts that are actually possible, and how each
resolves:

| Conflict | What collides | Resolution stance |
|---|---|---|
| **Name → two hashes** | Two instances point the same name at different content hashes | *Not* commutative. Surface as data: side-by-side both bodies; pick a side / merge / fork. Auto only if one side is the other's ancestor. |
| **Concurrent rename vs edit** | One op renames `Foo.bar`, another edits its body | Commutative — the edit applies to the renamed thing. Auto-resolve: apply both, re-point. |
| **Delete vs edit** | One op deletes a thing, another edits it | Not commutative — intent conflicts. Surface: "edited content you deleted; keep edit, or honor delete?" |
| **Move vs edit** | One op moves a thing to a new module, another edits it in place | Commutative like rename — apply edit to moved location. Auto. |
| **Signature change vs call-site** | One op changes a fn's params/return type; existing call sites still pass the old shape | Not commutative. Surface *per call site* as a `TypeMismatch` (see dev-time type changes below): update each call, or revert. |
| **Branch-vs-branch name divergence** | Two branches independently bind the same name to unrelated definitions | Not commutative. Surface as data: this is the classic side-by-side merge case. |
| **Dependency-version skew** | A merges in a thing depending on `Lib.x@h1`; B's side has `Lib.x@h2` | Auto where the two versions are compatible (type-and-signature equal); otherwise surface the skew with both versions named. |

The governing rule across all of these:

- **Auto-resolve where the operations commute** under the fold — rename/move/
  edit combinations, ancestor relationships, compatible version skew. The
  resolution is still *recorded* (see "auto-resolutions are recorded" below), so
  it can be inspected and overridden.
- **Surface as data otherwise** — produce a conflict value carrying both sides
  named and locatable, and let a resolution op pick. The side-by-side viewer is
  the default `AskHuman` presentation of exactly this data.

Crucially, none of this is special-cased SCM machinery. `getConflicts` (the
fork-point query: "same location modified on both sides") becomes a *producer*
of generic conflict values feeding the shared dispatch. SCM stops being a
parallel universe with its own raise paths; it is one folder over the op stream
among many.

---

## Run-time conflicts

Run-time is the interpreter folding an expression into a value. Today the
interpreter mostly `raise`s and unwinds — fine for a happy-path evaluator, wrong
for live development, parallel materialization, multi-instance sync, or anywhere
"wait a bit longer" or "ask someone" is a valid response.

The shift: every site that *might* fail emits a conflict and acts on the
resolution. Run-time conflict kinds:

- **`FnNotFound`** — a call references a name nothing has defined yet.
- **`PendingUnresolved`** — a PDD materializer couldn't produce a body.
- **`TypeMismatch`** — a materialized/returned body's type disagrees with the
  call site.
- **`CapabilityDenied`** — the body needs a capability the caller wasn't
  granted.
- **`ResourceExhausted`** — a budget (LLM tokens, time) is spent.
- **`RuntimeError`** — the existing typed error hierarchy
  (`Ints.DivideByZero`, `Lets.PatternDoesNotMatch`,
  `Strings.NonStringInInterpolation`, ...) lifted into a conflict so a policy
  can decide something other than "halt."

The default dispatch in strict mode is **`FailLoudly` for everything** — byte-
identical to today. The substrate adds a hook; it does not change semantics
until a policy is deliberately installed. In loose/dev mode the dispatch can
substitute defaults (`1/0 → 0`, `unwrap None → unit`) and record the
substitution in the trace; the developer reviews and fixes when ready. In
ask-human mode the developer *is* the dispatch.

### Errors-as-conflicts

The existing `RuntimeError` hierarchy is already organized by domain with
specific variants — it is configuration-ready in shape. What's missing is the
configurability. Each raise site becomes "emit a conflict; let the dispatch
decide; act on the resolution":

```fsharp
let! resolution =
  state.conflictDispatch
    (Conflict.RuntimeError (RuntimeError.Ints DivideByZeroError))
    callContext
match resolution with
| Resolution.Substitute dval -> return dval      // e.g. DInt64 0L
| Resolution.FailLoudly err  -> raiseRTE err      // today's behavior
| Resolution.Park selector   -> return! park selector
| Resolution.AskHuman query   -> return! askThenResume query
```

What is and isn't configurable:

- **Configurable** (becomes `Conflict.RuntimeError`): arithmetic errors,
  list/dict mismatches, pattern non-match, interpolation type errors,
  `Option`/`Result` unwraps, type-checker mismatches at call sites and let-
  bindings, `FnNotFound`. The test: *a user might reasonably want a policy
  other than halt.*
- **Not configurable** (stays an unconditional raise): internal invariant
  violations, "this should never happen", OOM, stack-machine corruption,
  serializer/OS-level failures. The test: *halting is the only sane response
  because the substrate is in an undefined state.*

### Risk: hot-path latency

Every potentially-erroring site now does a dispatch lookup before it would have
unwound. The auto-rule fast path is a single match arm returning `FailLoudly` —
no allocations, no async hops. Likely negligible; measure on tight integer
loops and, if needed, gate the dispatch behind a tolerance-mode check that
short-circuits in strict mode.

---

## Playback conflicts

Playback re-folds a recorded op/trace stream — on another instance, or later in
time — and is the strictest timing. The requirement is **determinism**: a
resolution chosen during the original run must replay to the identical outcome.

This drives one hard constraint on resolutions: they must be
**content-addressed**. A resolution produced by "auto-rule X over inputs Y"
hashes to the same value on any instance, so the same op stream folds to the
same projection everywhere. Human decisions become recorded resolution ops on
the stream; playback re-applies them rather than re-asking. A conflict that
*can't* be resolved deterministically (e.g. one that genuinely required a fresh
human choice not yet recorded) is itself a playback conflict — surfaced, not
silently re-rolled.

Playback is what makes "the auto-rules are content-addressed across instances"
a real guarantee rather than a hope, and it is why auto-resolutions must be
recorded even when invisible.

---

## At-rest conflicts (sync, merge, bootstrap)

At-rest is folding ops into durable projections between sessions — most
importantly, **sync inbound**, where an op arrives from a remote peer and folds
over local state.

- **`SyncDivergence`** — inbound op `O` targets location `L`; local state
  already binds `L` to a different hash. Auto-rule "namespace-owner-wins": if
  `O`'s author owns `L`, accept; otherwise convert `O` into an approval-request
  op and `Park` for the owner's decision. Either way the sync **proceeds** — the
  conflict is data on the stream, not a blocker.
- **Merge** is dev-time op-vs-op (above) evaluated at-rest: the same
  commutative-vs-surface rules apply.
- **Bootstrap from a content snapshot** has *no* implicit name-resolution
  conflicts to worry about — there are no `.dark` files to re-resolve. Anything
  ambiguous flows through the same dispatch.
- **`ConstraintViolated` (at-rest)** — when the fold reaches a resting state, an
  `App`'s `invariants` ([distributed-event-sourcing.md](distributed-event-sourcing.md))
  run over it; a returned `Violation` becomes a `ConstraintViolated` conflict.
  Runtime invariants produce the same conflict at run-time instead. Default is
  surface-not-block (the violations list is a projection); only a *hard* invariant
  resolves to `FailLoudly`.

Because conflicts and resolutions are ops, cross-instance conflict resolution
needs no extra plumbing: a remote peer's conflict and its resolution land
locally via the same op stream the rest of the state travels on.

---

## The resolution dispatch

A conflict produces a `Resolution` via a layered dispatch — independent of which
timing produced the conflict:

```
   Conflict
      │
      ▼
   1. Default auto-rule    — "this kind, in this context, resolves this way
      │                       unless overridden"
      ▼ (none applies)
   2. Policy lookup        — "user/session/branch declared a policy for this kind"
      │
      ▼ (none applies)
   3. Park + ask           — "park the affected frame(s); surface to the right
      │                       agent via the event bus; wake on response"
      ▼ (declined / unavailable / timed out)
   4. Fail loudly          — "raise a typed error; let the enclosing tolerance
                              policy decide to substitute or propagate"
```

**A conflict that gets auto-resolved is still recorded.** The auto-resolution
may depend on namespace/context, but it is known and overridable later. Silent
auto-resolutions are how things get spooky; the recorded resolution-op keeps
them honest. "Why didn't this surface to me?" — because rule X fired; here is
the op that says so.

### Resolution outcomes

A `Resolution` is one of:

- **Substitute** — provide a default (typed default, empty, the other side, the
  merged version) and continue.
- **Park** — pause the affected frames; wait for an event that unblocks them.
- **PickSide** — for op-vs-op: ours / theirs / merged; for run-time: a strategy
  id.
- **RetryWith** — a different materializer / corpus / model.
- **AskHuman** — surface a query via the event bus; park until the answer-op
  arrives.
- **FailLoudly** — emit a typed error; let the enclosing tolerance policy
  handle it. (Sometimes the right answer is "no, I won't make this up." Tests
  run fail-loud by default.)

### Type shapes

```fsharp
type Conflict =
  | FnNotFound        of name: FQFnName.T * site: CallSite
  | PendingUnresolved of handle: Pending * reason: PendingFailure
  | TypeMismatch      of expected: TypeRef * got: TypeRef * site: CallSite
  | CapabilityDenied  of cap: Capability * site: CallSite * reason: string
  | OpVsOp            of location: Location * current: Thing * proposed: Thing
  | SyncDivergence    of location: Location * local: Hash * remote: Hash
  | RuntimeError      of err: RTE.Error
  | ParseFailed       of region: SourceSpan
  | HumanTimedOut     of query: HumanQuery * elapsed: Duration
  | ResourceExhausted of kind: ResourceKind
  | ConstraintViolated of violation: Violation * timing: EvalTiming  // an App.invariants result
  // extensible

type Resolution =
  | Substitute of value: Dval
  | Park       of waitOn: EventSelector     // see design/event-bus.md
  | PickSide   of choice: SideChoice
  | RetryWith  of strategy: StrategyId
  | AskHuman   of query: HumanQuery
  | FailLoudly of error: RTE.Error

type ConflictDispatch = Conflict -> CallContext -> Ply<Resolution>
```

`CallContext` carries the in-flight agent (human or agent kind, and delegation
authority if an agent), branch, tolerance mode, granted capabilities, call site,
and active trace. The dispatch is a single function — pattern-matching on the
conflict variant routes to per-kind handlers — so it is swappable per session,
per tolerance mode, and per test. Handlers are installed by the relevant
subsystem: SCM kinds by the merge folder, capability kinds by the cap checker,
materialization kinds by PDD.

```fsharp
let dispatch : ConflictDispatch = fun conflict ctx -> uply {
  match! tryAutoRule conflict ctx with        // Layer 1
  | Some res -> recordResolution conflict ctx res "AutoRule"; return res
  | None ->
  match! tryPolicy conflict ctx with          // Layer 2
  | Some res -> recordResolution conflict ctx res "Policy"; return res
  | None ->
  let askedAgent = whoToAsk conflict ctx      // Layer 3 (+ Layer 4 on timeout)
  let! res = parkAndAsk conflict ctx askedAgent
  recordResolution conflict ctx res "Human"
  return res
}
```

`whoToAsk` makes the dispatch agent-aware: a cap denial for agent X owned by
human Y asks Y; an op-vs-op in Y's namespace asks Y; a `FnNotFound` in agent X's
code asks X's owner; an unroutable conflict fails loudly to the active session
user.

---

## How resolutions reach the stream

The dispatch decides; the **event bus** carries. A `Resolution.Park` parks a
frame on an `EventSelector`; the resolving event wakes it. The dispatch and the
event substrate are deliberately decoupled — the dispatch only needs to name
what to park on, and the bus handles the wake. Conflict-resolution events are
exactly the parking points the bus is built around; see `design/event-bus.md`
for the parking-on-conflict-resolution mechanics.

The resolution, once chosen, is an op on the distributed stream like any other.
The `App` type's conflict and resolve members (see
`design/distributed-event-sourcing.md`) are where these ops enter and re-fold
into projections. Because the resolution is content-addressed (the playback
constraint), every instance folding the stream lands on the same projection.

---

## Why this gates SCM and sync

The conflicts-and-resolutions primitive **is the contract that lets two parties
— instances, users, agents — coordinate.** Without it, sync bottoms out at
"fail" (useless) or "silently overwrite" (dangerous). With it:

- Sync becomes "propose ops; surface conflicts; resolve via the dispatch."
- Branches become "your resolutions diverge from theirs at these points; here is
  the dispatch outcome."
- Multi-instance propagation works because the dispatch is consistent across
  instances and the auto-rules are content-addressed (playback).
- Snapshot bootstrap works because there are no implicit name-resolution
  conflicts — anything ambiguous flows through the dispatch.

---

## Open questions

- **Who installs the auto-rules?** Probably a stack: user override → session →
  branch → namespace owner → system default.
- **`Park` granularity?** Probably per-frame, with events threaded through the
  scheduler.
- **Resolution strategies, not just resolutions.** A conflict may have several
  available resolutions, some mutually exclusive. The dispatch likely needs a
  `ResolutionStrategy` value expressing "try A; if it fails, try B; ..." rather
  than a single `Resolution`.
- **Parse-time uniformity.** Defer-to-runtime (placeholder + run-time conflict)
  is the uniform path; an explicit `ParsePolicy` on the parser is the less-
  invasive path. Open which becomes primary.

## Open decision: does WIP sync, and how does it fold?

WIP is working state — uncommitted ops, speculative candidates mid-race, a body
the human hasn't accepted (see "stable" in `design/algorithm.md`). Whether WIP
crosses instances is unsettled, and the answer reshapes the dev-time and at-rest
conflict story above, so it belongs here as a real open decision rather than a
sync footnote.

The tension:

- **(a) WIP doesn't sync.** Cleaner: WIP stays local, sidesteps a pile of op-
  semantics questions — *as long as* WIP is stored separately from the committed
  op stream. Under this reading WIP never enters the fold that produces dev-time
  conflicts, so the conflict dispatch only ever sees committed ops.
- **(b) WIP needs a sync story.** You may want your own WIP on a second machine,
  or to hand a coworker an unfinished body. Then either:
  - **Lightweight** — gist-like snapshots, no full op semantics. WIP travels as
    opaque blobs that never participate in conflict folding.
  - **Heavyweight** — WIP gets the full op treatment and syncs exactly like
    committed package items, which means WIP ops *do* fold into the op-vs-op
    conflict table above and can clash across instances.

We have not picked (a) vs (b). The choice decides whether WIP is inside or
outside the conflict fold, and therefore whether the dev-time op-vs-op rules
apply to in-flight work or only to committed work. (`design/sync.md` records the
matching wire-side lean: WIP local by default, opt-in to share.)

The one thing that holds **regardless** of (a)/(b): **whenever WIP becomes
real/committed, references to it rewrite from by-location to by-hash**, so a
committed body is stable long-term and folds deterministically on every instance
(the playback constraint above). Separating WIP from committed is therefore not a
flag on a row — it determines which ops apply, which sync, and which show up in
`dark search`.
