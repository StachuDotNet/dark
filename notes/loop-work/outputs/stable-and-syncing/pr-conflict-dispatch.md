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

| File (on `main`) | Change |
|---|---|
| `LibExecution/ConflictTypes.fs` *(new)* | `Conflict`, `Resolution`, `CallContext`, `ConflictDispatch` (the type shapes from the design doc). ~60 LoC, RT-light — compile after `RuntimeTypes.fs` (it mentions `RuntimeError.Error`/`Dval`). |
| `LibExecution/RuntimeTypes.fs` | `ExecutionState` gains `conflictDispatch : ConflictDispatch` (per-execution group). **PT untouched** — a runtime hook, not serialized; no hash churn. |
| `LibExecution/Execution.fs` (`createState`) | Default: `fun conflict _ctx -> Ply(Resolution.FailLoudly (Conflict.toRTE conflict))`. The one construction site (others are `with`-copies — see the EventBus PR). |
| `LibExecution/Interpreter.fs` | At **one** representative site (`FnNotFound`, line ~302), replace `raiseRTE (RTE.FnNotFound n)` with: ask the dispatch, then act on the `Resolution`. Every other `raiseRTE` stays untouched this PR. |

```fsharp
// the seam at a fail site — proof shape, applied to FnNotFound only this PR
let! resolution = state.conflictDispatch (Conflict.RuntimeError (RTE.FnNotFound n)) (ctx vm)
match resolution with
| Resolution.FailLoudly rte -> return raiseRTE vm.threadID rte   // == today's behavior
| Resolution.Substitute dval -> return dval                       // dev/loose mode
| Resolution.Park selector   -> return! park selector             // later (needs the bus)
| _                          -> return raiseRTE vm.threadID (RTE.FnNotFound n)
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
