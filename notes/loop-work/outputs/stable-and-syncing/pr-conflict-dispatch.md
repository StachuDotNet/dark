# PR: Conflict-dispatch skeleton

The spine's floor effort 4, built from [conflicts-and-resolutions.md](conflicts-and-resolutions.md).
The hook that sync, capabilities, and runtime errors all later route through — added now so it
exists, **changing no behavior** until a policy is installed.

**Goal.** Add a `conflictDispatch` seam to `ExecutionState`: at a fail site, instead of raising
directly, the interpreter asks the dispatch what to do. The default returns `FailLoudly`, which
re-raises the exact same `RuntimeError` — so the build is **byte-identical to today**. This PR
lands the *types* + the *seam at one site*; later PRs route more sites and install policies.

**Prereqs.** None (leaf). The `Park` resolution will later park on the event bus
([event-bus.md](../pre-s-and-s/event-bus.md)), but the **default dispatch never Parks**, so the
EventBus PR is not a hard prereq for the skeleton.

## .fs changes

`main` raises via `raiseRTE (threadId) (rte : RuntimeError.Error)` →
`RuntimeErrorException`, caught at `Execution.fs`. The skeleton wraps that.

> **Validated in prework** (real code on `loop-fun:prework/conflict-dispatch`, off clean `main`).
> **Compiles clean** (0 errors) in the loop-fun devcontainer — the `Conflict`/`Resolution`/
> `CallContext`/`ConflictDispatch` types + the `ExecutionState.conflictDispatch` field + the
> `createState` FailLoudly default all build against real `main`. Findings that *correct* this spec:
> - **Types live IN `RuntimeTypes.fs` (the and-chain), NOT a separate `ConflictTypes.fs`.** They
>   mention `RuntimeError.Error`/`Dval` (defined there) *and* `ExecutionState` references
>   `ConflictDispatch` — a later file can't satisfy both. (Same circular constraint the EventBus
>   PR hit with `RuntimeBuses`.) The original "new ConflictTypes.fs after RuntimeTypes" was wrong.
> - **`Resolution.Park` is omitted from the skeleton** — it needs the scheduler's `EventSelector`
>   (vision-domain, doesn't exist yet). Skeleton ships `Substitute`/`FailLoudly`; `Park` lands with
>   the scheduler PR. Confirms "the default never Parks, so EventBus isn't a hard prereq."
> - **The `FnNotFound` seam site resolves *instructions*, not values** — so `Substitute(Dval)`
>   doesn't fit *there* (the site returns `InstrData`); it only meaningfully honors `FailLoudly`.
>   `Substitute` belongs at value-producing fail sites. The site also needs `vm` threaded for
>   `CallContext.threadID`. So the "one representative site" is real but FailLoudly-only.

| File (on `main`) | Change |
|---|---|
| `LibExecution/RuntimeTypes.fs` | Add `Conflict`/`Resolution`/`CallContext`/`ConflictDispatch` **to the `ExecutionState` and-chain** (they mention `Dval`/`RuntimeError`); `ExecutionState` gains `conflictDispatch : ConflictDispatch`. **PT untouched** — runtime hook, no hash churn. |
| `LibExecution/Execution.fs` (`createState`) | Default: `fun conflict _ctx -> uply { match conflict with CRuntimeError e -> return RFailLoudly e \| CFnNotFound n -> return RFailLoudly (RTE.FnNotFound n) }`. The one construction site (others are `with`-copies). |
| `LibExecution/Interpreter.fs` | At **one** async fail site (the *package*-`FnNotFound`, ~line 317, inside `uply` — the builtin one at ~302 is sync) route the raise through the dispatch, honoring `FailLoudly` (= today). Other `raiseRTE` sites untouched. |

```fsharp
// the seam at the package-FnNotFound site (async, ~317) — FailLoudly-only this PR
let! resolution = state.conflictDispatch (Conflict.FnNotFound n) { branchId = state.branchId; threadID = vm.threadID }
match resolution with
| Resolution.FailLoudly rte -> return raiseRTE vm.threadID rte   // == today's behavior (the default)
| _                          -> return raiseRTE vm.threadID (RTE.FnNotFound n)
// Substitute(Dval) doesn't fit here (this site returns InstrData, not a value); it applies at
// value-producing fail sites. Park lands with the scheduler PR (needs EventSelector).
```

`CallContext` is assembled from what's already on hand — `ExecutionState.branchId` + the
`VMState` (threadID, call stack, tracing); no new plumbing to carry it.

## ProgramTypes / SQL — none

PT untouched (runtime hook). No durable state, so **no migration**. Resolutions only become
durable ops in the *sync* PR (the playback-determinism requirement); the skeleton is in-memory.

## Test plan

| Step | Test (`.fs` unless noted) | Done-signal |
|---|---|---|
| default = today | `ConflictDispatchTests` (new): call an undefined fn; assert the same `RuntimeErrorException(FnNotFound)` as before | identical raise |
| seam works | install a dispatch returning `Substitute (DInt64 0L)`; call undefined fn; assert `0` returned | substitution observed |
| no global slowdown | micro-bench a tight loop with the default dispatch vs `main` | within noise |
| `.dark` unchanged | existing `.dark` error tests (e.g. `1/0`, unwrap `None`) still fail-loud by default | suite green, no diffs |

## CLI impact

**None this PR.** The hook is internal. The follow-on (errors-as-conflicts rollout) adds a
`--tolerant` / dev-mode flag that installs a substituting dispatch; flagged there, not here.

## UX change

**Nothing visible — by design.** Same errors, same messages, same exit codes. "We added a seam
and changed nothing" *is* the deliverable; it's what makes it safe to land early.

## Risks / problems not yet raised

- **Hot-path latency.** A dispatch call before every routed raise. Mitigated by routing *one*
  site this PR and keeping the default a single match arm (no allocation); measure before
  routing the hot arithmetic sites in a later PR, and gate behind a strict-mode short-circuit
  if needed.
- **Lossless `Conflict.toRTE`.** `FailLoudly` must reproduce the *exact* original
  `RuntimeError`, or error messages/tests drift. The `Conflict.RuntimeError` variant carries the
  original `rte` verbatim so round-tripping is identity.
- **`CallContext` lifetime.** Assembled per-call from VMState+ExecutionState; must not capture
  stale frame state across a `Park`/resume (relevant once Park lands).

## Above / below

- **Below:** nothing.
- **Above expects:** the caps PR emits `Conflict.CapabilityDenied`; the sync PR emits
  `Conflict.SyncDivergence` and makes resolutions durable content-addressed ops; the
  errors-as-conflicts rollout routes the remaining `raiseRTE` sites. All depend only on the
  `Conflict`/`Resolution`/`ConflictDispatch` shapes frozen here.
