# Conflicts + Resolutions

The load-bearing substrate. The same primitive handles SCM merge
conflicts, runtime missing-name conflicts, capability denials,
type-mismatch-on-materialization, sync disagreements, and human
review timeouts. **Building this well unlocks SCM + sync work
broadly — not just PDD.**

Today: when something is missing or two things disagree, we mostly
`raise` and unwind. That works for a happy-path interpreter but
not for live development, parallel materialization, multi-instance
sync, or anything where "wait a bit longer" or "ask someone" is a
valid response. We need a richer model.

## What is a conflict

A **conflict** is *any place the system can't unambiguously
proceed*. Two operations want the same location. A function call
references a name nothing yet defines. A capability is needed but
not granted. A sync would overwrite a local change. A
materialization produces a body whose type disagrees with the
call site. A human was asked a question and hasn't answered.

The unifying observation: every one of these has the same shape —
**"I want to make progress; I can't yet; here are my options."**
The options are what a *resolution* picks among.

Today's LibMatter has a concrete conflict model for SCM ops
(`TwoNamesPointedToSameThing`, `UpdateConflict` between sessions,
etc.). That model is the right starting point — but the
**concept** generalizes well beyond SCM and should be lifted into
LibExecution as a base primitive.

## Where conflicts arise

| Source | Example | Today's behavior |
|---|---|---|
| **SCM op-vs-op** | Two patches both add `Foo.bar` | LibMatter detects, blocks merge |
| **Runtime fn-not-found** | Body calls `frobulate` which doesn't exist | `raiseRTE FnNotFound` |
| **Pending unresolved** | PDD materializer can't produce a body | Today: also raises |
| **Type mismatch on materialization** | LLM returned a body whose sig disagrees with the call site | Mostly logged + retried; sometimes falls through |
| **Capability denied** | Body calls `Builtin.File.delete` but caller has no `CapWriteFile` | Not enforced today |
| **Sync disagreement** | Remote has a different body for the same hash slot | Not handled (we don't sync yet) |
| **Human timed out** | Waiting on a human review; SLA exceeded | Not modeled |
| **WIP would be overwritten** | A sync would clobber local working-copy edits | Not handled |
| **Resource exhausted** | LLM budget hit | Today: just fails |

These are all the same shape. They should all flow through the
same dispatch.

## The resolution dispatch

A conflict produces a `Resolution`. The dispatch is layered:

```
   Conflict
      │
      ▼
   1. Default auto-rule
      ↳ "this kind of conflict, in this kind of context,
         is resolved this way unless overridden"
      │
      ▼ (no default applies)
   2. Policy lookup
      ↳ "user/session/branch has a declared policy that
         covers this conflict kind"
      │
      ▼ (no policy applies)
   3. Park + ask
      ↳ "park the affected frame(s); surface to a human
         via the event bus; wake on response"
      │
      ▼ (human declined / unavailable / timed out)
   4. Fail loudly
      ↳ "raise a typed error to the call site; let the
         enclosing tolerance policy decide whether to
         substitute or propagate"
```

Auto-rules are explicit and inspectable. **A conflict that gets
auto-resolved is still recorded** — same as LibMatter's "the
auto-resolution may depend on the namespace, but is still known,
and may be overridden later." This is critical: silent
auto-resolutions are how things get spooky. The audit trail keeps
them honest.

## Resolution outcomes

A `Resolution` is one of:

- **Substitute** — provide a default (typed default, empty, the
  other side, the merged version). Continue.
- **Park** — pause the affected frames; wait for an event that
  unblocks them.
- **Pick a side** — for op-vs-op: ours / theirs / merged. For
  fn-not-found: try a different strategy.
- **Retry with different strategy** — invoke a different
  materializer / search a different corpus / use a different
  model.
- **Ask human** — surface a query via the event bus; park until
  answer.
- **Fail loudly** — emit a typed error; let the enclosing
  tolerance policy handle. (Sometimes the right answer is "no, I
  refuse to make this up." Tests run with fail-loud-by-default.)

The dispatch picks one. The outcome is recorded with the conflict.

## LibExecution integration

This is where the substrate work lives. Today `LibExecution` raises
on `FnNotFound`. The shift:

- **`Conflict` type** in `LibExecution.RuntimeTypes` — a sum type
  covering the conflict kinds. Extensible.
- **`ConflictDispatch`** field on `ExecutionState` — pluggable per-
  conflict-kind handler. Defaults installed by `LibExecution`;
  overrides installed by callers (LibMatter installs SCM ones; PDD
  installs materializer ones; capability checker installs cap
  ones).
- **Interpreter** changes from `raise FnNotFound` to "emit a
  conflict; await resolution; act on it." Most calls are still
  fast: the auto-resolution for `FnNotFound` in strict mode is
  *immediate raise*, so happy paths don't pay.
- **Parking primitive** (see `EVENT-STREAMS-AND-PARKING.md`) is
  what makes "wait" actually work — the conflict-resolver returns
  a "parked on event X" signal; the scheduler runs other frames.

This is the **same machinery LibMatter already wants** for op-vs-op
conflicts. LibMatter today raises validation errors; instead, those
become conflicts on the same bus. The user-facing UX (the side-by-
side comparison from `flows/conflict-resolution.md`) becomes a
default `Ask human` resolution.

## F#-shaped type signature

```fsharp
type Conflict =
  | FnNotFound of name: FQFnName.T * site: CallSite
  | PendingUnresolved of handle: Pending * reason: PendingFailure
  | TypeMismatch of expected: TypeRef * got: TypeRef * site: CallSite
  | CapabilityDenied of cap: Capability * site: CallSite * reason: string
  | OpVsOp of location: Location * current: PackageThing * proposed: PackageThing
  | SyncDivergence of hash: Hash * local: Body * remote: Body
  | HumanTimedOut of query: HumanQuery * elapsed: Duration
  | ResourceExhausted of kind: ResourceKind
  // extensible

type Resolution =
  | Substitute of value: Dval                       // typed default / empty / chosen-side
  | Park of waitOn: EventSelector                   // see EVENT-STREAMS-AND-PARKING.md
  | PickSide of choice: SideChoice                  // ours/theirs/merged for SCM, strategy-id for runtime
  | RetryWith of strategy: StrategyId               // e.g. "use gpt-4o instead of mini"
  | AskHuman of query: HumanQuery                   // emits an event the viewer subscribes to
  | FailLoudly of error: RTE.Error                  // unwind via tolerance policy

type ConflictDispatch = Conflict -> CallContext -> Ply<Resolution>
```

`CallContext` carries the in-flight session/branch, capability set,
tolerance mode, and any user-declared policy. The dispatch is a
single fn (per the right per-kind dispatching internally), making it
swappable per session / per tolerance-mode / per test.

## Examples mapped to today

- **Today's `FnNotFound` raise** → `Conflict.FnNotFound` →
  auto-rule (strict mode: `FailLoudly`; loose mode: `Substitute
  (defaultFor returnType)`; PDD-mode: `RetryWith "materialize-via-
  LLM"`).
- **Pending unresolvable after budget** → `Conflict.
  PendingUnresolved` → policy says try corpus search next, then
  ask human.
- **LibMatter `TwoNamesPointedToSameThing`** → `Conflict.OpVsOp`
  → policy says `AskHuman` (side-by-side webview); auto-resolution
  available for trivial cases (e.g. one side is a rename, the
  other is unrelated).
- **Capability denied** → `Conflict.CapabilityDenied` →
  interactive mode: `AskHuman`; non-interactive: `FailLoudly`;
  session has `--allow http`: `Substitute` (proceed).
- **WIP would be overwritten by sync** → `Conflict.SyncDivergence`
  → policy says `AskHuman` (does the user want their WIP, the
  remote, or to fork their WIP to a new branch?).

In every case, the conflict, the available resolutions, and the
chosen resolution are recorded in the trace. Auditable.

## Why this is gating for SCM + sync

The conflicts+resolutions primitive **is the contract that lets two
parties (instances, users, agents) coordinate**. Without it, sync
either bottoms out at "fail" (useless) or "silently overwrite"
(dangerous). With it:

- Sync becomes "propose a set of ops; surface any conflicts;
  resolve via the dispatch."
- Branches become "your set of resolutions diverges from theirs
  at these points; here's the dispatch outcome."
- Multi-instance state propagation becomes "the dispatch is
  consistent across instances; the auto-rules are content-
  addressed."
- Bootstrapping from a content snapshot (no `.dark` files) works
  because there are *no implicit name-resolution conflicts to
  worry about* — anything ambiguous flows through the dispatch.

`SYNC-AND-STABILITY.md` picks up from here.

## Open questions

- **Who installs the auto-rules?** Per-namespace? Per-session?
  Per-user? Probably a stack: user override → session → branch
  → namespace owner → system default.
- **How are resolutions content-addressed?** A resolution chosen
  by `auto-rule X with input Y` should hash to the same thing on
  any instance, so traces replay identically across machines.
- **What's the granularity of `Park`?** Per-frame? Per-instruction?
  Per-session? Probably per-frame; events thread through the
  scheduler.
- **Composability**: a conflict can have multiple available
  resolutions; some are mutually exclusive. The dispatch needs a
  way to express "try A; if that fails, try B; if that fails,
  ..." — a `ResolutionStrategy` value, not just a `Resolution`.
