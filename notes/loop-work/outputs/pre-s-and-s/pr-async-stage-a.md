# PR: async Stage A — effects + isolation + cancellation

The spine's effort 2 (**[vision]** — the foundation the scheduler rests on, not on the floor's
critical path). Built from [async.md](async.md) (the staged migration). Stage A gives the
runtime the three things the scheduler (Stage C) needs *before* any parking exists — and
changes no observable behavior on its own.

**Goal.** (1) Every builtin declares its **effect**; (2) a VM can spawn an **isolated child
VM**; (3) execution carries a **cancellation** signal. Metadata + plumbing only — nothing parks
yet, async stays invisible at the Dark surface.

**Prereqs.** None (leaf). Unblocks the scheduler (effort 6, consumes all three).

> **`effects` vs `caps` — two orthogonal axes, not redundant.** A builtin now carries *both*
> `caps : Set<CapCategory>` ([capabilities.md](capabilities.md)) and `effects : Effect` (here).
> They answer different questions: **`caps` = resource *domain*** (HttpClient / FileSystem /
> CliHost …), read by the **gate** (security: may this code touch that domain?); **`effects` =
> concurrency *character*** (Pure / AsyncRead / OrderedIO / Blocking …), read by the
> **scheduler** (perf: can these overlap, must this stay ordered?). E.g. `HttpClient.get` is
> `caps={HttpClient}` *and* `effects=AsyncRead`. They correlate only at the edges — `Pure`
> effect ⟺ `caps={}`, and `Harmful` lives in both — so they stay two fields, not one.

## .fs changes — the important part

| File (on `main`) | Change |
|---|---|
| `LibExecution/RuntimeTypes.fs` | Add **`effects : Effect`** to `BuiltInFn` (joins the existing `previewable`/`sqlSpec` metadata — same shape of change). Define the `Effect` DU. `previewable` is the crude precursor; `effects` is the richer cousin (don't remove `previewable` this PR — migrate later). |
| **Every `Builtins.*` assembly** (all 9) | Each builtin definition adds `effects = …`. **This is the bulk: ~620 sites** (counted on `main` via `sqlSpec` — 439 in Pure, 87 Matter, 68 Cli, 10 Random, the rest ≤6 each). Mostly *low-judgment* — Pure's 439 are nearly all genuinely `Pure` — but the *correctness-critical* part: a mis-declared `Pure` that actually does IO is a soundness bug (next section). The effectful assemblies (Http.Client/Server, Cli/CliHost, Random, Time) are the few that need real thought — and they're small (≤68 each). |
| `LibExecution/RuntimeTypes.fs` (VMState) | Child-VM isolation: `VMState` already has `threadID` + a call-frame `parent`. Add a `spawnChild` that gives the child its **own mutable state** (branch-local registers/threadID) while sharing only the **concurrency-safe** `ExecutionState` (the `ConcurrentDictionary` caches, buses). Mutable per-VM state is never shared. |
| `LibExecution/Execution.fs` / `Interpreter.fs` | Thread a **`cancel : CancellationToken`** (or a cancel selector) through the eval loop; check it at safe points (between instructions / before a builtin call), unwinding cleanly on cancel. |

```fsharp
// the effect taxonomy (from async.md / the coworker's effect model)
type Effect =
  | Pure                          // no effect — schedulable freely, never needs a cap
  | AsyncRead | AsyncWrite        // IO; AsyncReads are parallelizable when ConcurrentSafe
  | OrderedIO                     // must keep program order (stdout, append)
  | ConcurrentSafe                // safe to overlap with siblings
  | Blocking                      // holds a thread — schedule off the hot path
  | Resource of name : String     // holds a named resource (a port, a file handle)
  | Harmful                       // gated; ties to capabilities.md + the harmful-flag stream

// BuiltInFn gains one field, alongside previewable/sqlSpec:
//   effects : Effect
```

## ProgramTypes / SQL — none

`BuiltInFn` is a **runtime** type (the F# builtin definition), not serialized PT. So **no PT
change, no hash churn, no migration.** Effects live on the F# side; nothing user-facing or
durable changes.

## Test plan

| Step | Test (`.fs`) | Done-signal |
|---|---|---|
| every builtin declares an effect | `EffectMetadataTests`: enumerate all `BuiltInFn`s, assert each has `effects` set (compiler enforces presence; test checks no `Pure` lies) | total coverage |
| `Pure` is honest | for each builtin tagged `Pure`, assert it touches no IO builtin / no `ExecutionState` effect surface (static check over its impl where feasible) | no false `Pure` |
| child-VM isolation | spawn a child, mutate its registers/state, assert the parent VM's state is unchanged; shared caches still visible | isolation holds |
| cancellation unwinds | start a long child, cancel it, assert it stops at the next safe point and releases resources | clean unwind, no leak |

`.dark` tests: none — no surface change (async stays invisible). Adding `.dark` async tests
waits for the scheduler PR.

## CLI impact

**None.** Internal metadata + plumbing. (A later `dark view --effects` could surface a fn's
effect set, but that's not this PR.)

## UX change

**Nothing visible.** Async remains invisible; this PR only equips the runtime. The payoff
appears two PRs later when the scheduler overlaps independent `ConcurrentSafe AsyncRead` calls.

## Risks / problems not yet raised

- **Mis-declared effects are silent soundness bugs.** A builtin wrongly tagged `Pure`/`Concurrent
  Safe` lets the future planner overlap something order-dependent. Mitigation: default a builtin
  with no explicit effect to the *most* restrictive (`OrderedIO`/`Blocking`), so a forgotten
  declaration is conservative, not dangerous; the `Pure`-is-honest test guards the hot path.
- **Effect taxonomy completeness.** The 8 variants may not cover every builtin cleanly (e.g. a
  builtin that's `AsyncRead` *and* `Resource`). Likely `effects` is a small **set**, not one
  value — revisit if a single variant proves too coarse.
- **Shared-state safety in child VMs.** Exactly *which* `ExecutionState` fields are safe to share
  is the crux — the `ConcurrentDictionary` caches and buses are; any mutable non-concurrent field
  must be per-VM. Audit each field at implementation.
- **Cancellation safe-points.** Unwinding mid-builtin can leak a resource; cancellation must only
  take effect at declared safe points (between instructions, not inside a `Resource` builtin's
  critical section).

## Above / below

- **Below:** nothing.
- **Above expects:** the scheduler (effort 6) reads `effects` to plan overlap, uses `spawnChild`
  for parked-frame isolation, and `cancel` for timeouts; capabilities reads `effects`/`Harmful`.
  All depend only on the `Effect` type + `spawnChild`/`cancel` seams frozen here.
