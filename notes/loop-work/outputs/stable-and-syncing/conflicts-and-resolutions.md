# Conflicts and resolutions

The load-bearing substrate. One primitive handles SCM merge conflicts, runtime missing-name
conflicts, capability denials, type-mismatch-on-materialization, sync disagreements, and
human-review timeouts. Building it well unlocks SCM and sync broadly.

> **Bucket note (layering).** The conflict-*dispatch* is really a **runtime primitive** — a
> `conflictDispatch` hook on `ExecutionState` (see [pr-conflict-dispatch.md](pr-conflict-dispatch.md)),
> used by capabilities, the interpreter, *and* sync. So the **parse/dev/run/playback** timings
> below are pre-S&S substrate; only the **at-rest (sync) conflicts** (SyncDivergence, merge) are
> S&S-specific. The doc is kept whole here for readability; pre-S&S docs (capabilities, async)
> reference the *concept* inline rather than linking up. Extract the primitive into pre-S&S
> only if a pre-S&S doc ever needs a hard link to it — not needed today.

## A conflict is any place a fold can't proceed

State is a timestamped op stream; everything you see is a projection folded from it (the
keystone, [distributed-event-sourcing.md](../pre-s-and-s/distributed-event-sourcing.md)). A
**conflict** is not a row in a table — it's *any place a fold can't unambiguously advance*:
two ops want the same location; a call references an undefined name; a capability is needed
but ungranted; a sync would fold over a local change; a materialized body's type disagrees
with its call site. Every one has the shape *"I want to progress; I can't yet; here are my
options."* A **resolution** picks among the options — and is itself an op that lands on the
stream, so the projection re-folds with the ambiguity removed.

## The organizing claim: the timing *is* the type

Conflict sources (SCM, runtime, capability, sync…) collapse onto **the state at which the
fold tries to advance**. The conflict payload is shared; the *dispatch behavior keys off the
timing*:

- **Parse-time** — folding source text into an expression tree.
- **Dev-time** — folding edit ops (rename, move, signature change) against other in-flight ops.
- **Run-time** — folding an expression into a value (the interpreter).
- **Playback** — re-folding a recorded op stream, deterministically, elsewhere/later.
- **At-rest** — folding ops into durable projections between sessions (sync inbound, merge).

A `TypeMismatch` is a syntactic placeholder at parse-time, a per-call-site refactor at
dev-time, substitute-or-fail at run-time, must-match-the-recorded-choice at playback. Same
payload, timing-dependent dispatch. The doc is organized by that spine.

## Parse-time — defer to run-time

Parse errors happen *before* a dispatch context exists. The composable move: a parse failure
doesn't raise — it folds the unparseable region into a **placeholder expression**, and the
failure becomes a run-time conflict that fires *if and when* evaluation reaches it.

`main`'s `OnMissing` is `ThrowError | Allow` for unresolved names; the PDD **spike** added a
third case `AllowPending` (defer a missing name as a pending placeholder) — *spike-only, not a
`main` primitive*. Generalize that move to *syntactic* failures: every unparseable region
yields a placeholder; run-time decides. `ThrowError` keeps raising at parse-time for callers
who want it. So parse-time has essentially one shape — "this region didn't fold" — uniformly
reduced to run-time.

## Dev-time (SCM op-vs-op) — the important category

Two streams of editing ops, from different instances/branches/agents, fold over the *same*
locations. "Conflict" precisely means **two ops target one location and neither's effect
subsumes the other under the fold.**

| Conflict | What collides | Resolution stance |
|---|---|---|
| **Name → two hashes** | same name → different content hashes | *Not* commutative. Surface as data: side-by-side both bodies; pick/merge/fork. Auto only if one is the other's ancestor. |
| **Rename vs edit** | one renames `Foo.bar`, one edits its body | Commutative — edit applies to the renamed thing. Auto-resolve. |
| **Delete vs edit** | one deletes, one edits | Not commutative. Surface: "edited content you deleted; keep edit or honor delete?" |
| **Move vs edit** | one moves to a new module, one edits in place | Commutative like rename — edit applies to moved location. Auto. |
| **Signature vs call-site** | params/return change; call sites pass old shape | Not commutative. Surface *per call site* as a `TypeMismatch`: update each, or revert. |
| **Branch name divergence** | two branches bind one name to unrelated defs | Not commutative. The classic side-by-side merge case. |
| **Dependency-version skew** | merges depending on `Lib.x@h1` vs local `@h2` | Auto where compatible (type+sig equal); else surface the skew, both versions named. |

The governing rule: **auto-resolve where ops commute** (rename/move/edit combos, ancestor
relationships, compatible skew) — the resolution is still *recorded* and overridable;
**surface as data otherwise** — a conflict value carrying both sides named and locatable,
for a resolution op to pick (the side-by-side viewer is the default `AskHuman` presentation).

None of this is special-cased SCM machinery: the fork-point query ("same location modified on
both sides") becomes a *producer* of generic conflict values feeding the shared dispatch.

## Run-time — every fail site emits a conflict

The interpreter folds an expression into a value. Today it mostly `raise`s and unwinds —
wrong for live development, parallel materialization, or sync, where "wait" or "ask" is valid.
The shift: every site that *might* fail emits a conflict and acts on the resolution. Kinds:
`FnNotFound`, `PendingUnresolved` (spike), `TypeMismatch`, `CapabilityDenied`,
`ResourceExhausted`, and the existing typed `RuntimeError` hierarchy lifted so a policy can
decide something other than halt.

**The default dispatch in strict mode is `FailLoudly` for everything — byte-identical to
today.** The substrate adds a hook; it changes no semantics until a policy is installed. In
loose/dev mode it can substitute defaults (`1/0 → 0`) and record the substitution; in
ask-human mode the developer is the dispatch.

```fsharp
let! resolution = state.conflictDispatch (Conflict.RuntimeError (RuntimeError.Ints DivideByZeroError)) ctx
match resolution with
| Resolution.Substitute dval -> return dval        // e.g. DInt64 0L
| Resolution.FailLoudly err  -> raiseRTE err        // today's behavior
| Resolution.Park selector   -> return! park selector
| Resolution.AskHuman query   -> return! askThenResume query
```

The line between configurable and not: **configurable** = a user might reasonably want a
policy other than halt (arithmetic, unwraps, pattern non-match, type mismatches, `FnNotFound`);
**not configurable** = halting is the only sane response because the substrate is in an
undefined state (internal invariant violations, OOM, serializer/OS failures — stay an
unconditional raise). *Latency:* the strict-mode fast path is one match arm returning
`FailLoudly` (no allocations); measure on tight integer loops and gate behind a tolerance
check if needed.

## Playback — resolutions must be content-addressed

Playback re-folds a recorded stream elsewhere/later, and is the strictest timing: a resolution
chosen in the original run must replay to the identical outcome. So resolutions must be
**content-addressed** — "auto-rule X over inputs Y" hashes the same on any instance, so the
same stream folds to the same projection everywhere. Human decisions become recorded
resolution ops; playback re-applies rather than re-asks. A conflict that *can't* resolve
deterministically (a genuinely fresh human choice not yet recorded) is itself a playback
conflict — surfaced, not silently re-rolled. This is why auto-resolutions must be recorded
even when invisible.

## At-rest (sync, merge, bootstrap)

Folding ops into durable projections between sessions — most importantly **sync inbound**.

- **`SyncDivergence`** — inbound op `O` targets `L`; local already binds `L` to a different
  hash. Auto-rule "owner-wins": if `O`'s author owns `L`, accept; else convert `O` into an
  approval-request op and `Park` for the owner. Either way the sync **proceeds** — the conflict
  is data on the stream, not a blocker.
- **Merge** is dev-time op-vs-op evaluated at-rest: same commutative-vs-surface rules.
- **`ConstraintViolated`** — when the fold reaches rest, an `App`'s `invariants` run; a
  `Violation` becomes this conflict (runtime invariants produce it at run-time). Default is
  surface-not-block; only a *hard* invariant resolves to `FailLoudly`.

Because conflicts and resolutions are ops, cross-instance resolution needs no extra plumbing —
a remote peer's conflict and its resolution arrive via the same op stream as everything else.

## The resolution dispatch

A conflict produces a `Resolution` via a layered dispatch, independent of timing:

```
 Conflict ─► 1. Default auto-rule ─► (none) ─► 2. Policy lookup ─► (none)
          ─► 3. Park + ask (via the event bus; wake on response)
          ─► 4. Fail loudly (typed error; enclosing tolerance policy decides)
```

**An auto-resolved conflict is still recorded.** Silent auto-resolutions are how things get
spooky; the recorded resolution-op keeps them honest — "why didn't this surface? because rule
X fired; here's the op that says so."

```fsharp
type Conflict =
  | FnNotFound        of name: FQFnName.T * site: CallSite
  | PendingUnresolved of handle: Pending * reason: PendingFailure   // spike-only body
  | TypeMismatch      of expected: TypeRef * got: TypeRef * site: CallSite
  | CapabilityDenied  of cap: Capability * site: CallSite * reason: String
  | OpVsOp            of location: Location * current: Thing * proposed: Thing
  | SyncDivergence    of location: Location * local: Hash * remote: Hash
  | RuntimeError      of err: RTE.Error
  | ParseFailed       of region: SourceSpan
  | HumanTimedOut     of query: HumanQuery * elapsed: Duration
  | ResourceExhausted of kind: ResourceKind
  | ConstraintViolated of violation: Violation * timing: EvalTiming   // an App.invariants result
  // extensible

type Resolution =
  | Substitute of value: Dval
  | Park       of waitOn: EventSelector       // parks a frame on the event bus
  | PickSide   of choice: SideChoice          // ours / theirs / merged
  | RetryWith  of strategy: StrategyId        // different materializer / model
  | AskHuman   of query: HumanQuery
  | FailLoudly of error: RTE.Error            // "no, I won't make this up." Tests run fail-loud.

type ConflictDispatch = Conflict -> CallContext -> Ply<Resolution>

let dispatch : ConflictDispatch = fun conflict ctx -> uply {
  match! tryAutoRule conflict ctx with                              // Layer 1
  | Some res -> recordResolution conflict ctx res "AutoRule"; return res
  | None ->
  match! tryPolicy conflict ctx with                               // Layer 2
  | Some res -> recordResolution conflict ctx res "Policy"; return res
  | None ->
  let askedAgent = whoToAsk conflict ctx                           // Layer 3 (+ 4 on timeout)
  let! res = parkAndAsk conflict ctx askedAgent
  recordResolution conflict ctx res "Human"; return res }
```

`CallContext` carries the in-flight agent (human/agent + delegation authority), branch,
tolerance mode, granted capabilities, call site, and active trace — so the dispatch is
swappable per session, per tolerance mode, and per test. `whoToAsk` makes it agent-aware: a
cap denial for agent X owned by human Y asks Y; a `FnNotFound` in X's code asks X's owner; an
unroutable conflict fails loudly to the active user.

The dispatch only *names* what to park on; the **event bus**
([event-bus.md](../pre-s-and-s/event-bus.md)) carries the wake. Once chosen, a resolution is a
content-addressed op on the stream, so every instance folds to the same projection.

## Why this gates SCM and sync

It's the contract that lets two parties — instances, users, agents — coordinate. Without it,
sync bottoms out at "fail" (useless) or "silently overwrite" (dangerous). With it: sync is
"propose ops; surface conflicts; resolve via the dispatch"; branches are "your resolutions
diverge from theirs here"; multi-instance propagation works because the dispatch is consistent
and auto-rules are content-addressed (playback).

## Open questions

- **Who installs auto-rules?** Likely a stack: user override → session → branch → namespace
  owner → system default.
- **Resolution strategies, not just resolutions.** A conflict may have several mutually
  exclusive resolutions; the dispatch likely needs a `ResolutionStrategy` ("try A, else B").
- **Parse-time uniformity.** Defer-to-runtime (placeholder) vs. an explicit `ParsePolicy` on
  the parser — which becomes primary.

## Open decision: does WIP sync? (punted)

WIP is uncommitted working state. Whether it crosses instances is unsettled and reshapes the
dev-time/at-rest story, so it's a real decision, not a footnote:

- **(a) WIP doesn't sync** — cleaner; WIP stays local *and stored separately from committed
  ops*, so it never enters the conflict fold.
- **(b) WIP syncs** — either lightweight (opaque gist-like blobs that never fold) or heavyweight
  (full op treatment, so WIP ops *do* clash in the op-vs-op table).

Punted (we don't yet know how to do (b) safely); the wire-side lean is WIP-local-by-default,
opt-in to share. The invariant that holds regardless: **when WIP becomes committed, references
rewrite from by-location to by-hash**, so a committed body folds deterministically everywhere.
