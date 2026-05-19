# Capabilities

The fourth substrate piece. **Must land before real PDD work.**
LLM-generated code will try to delete files, hit random network
endpoints, exfiltrate secrets. An ungated runtime is a footgun;
shipping PDD on top of one would be reckless.

Read with `CONFLICTS-AND-RESOLUTIONS.md` (denials are conflicts)
and `EVENT-STREAMS-AND-PARKING.md` (grants flow over the event
bus).

The vault is thin here — advisor notes mention "no safety; bash
does whatever — capabilities model, builtins are the only impure
boundary" with status: "design on paper; enforcement not wired up."
This doc moves the design forward.

## The shape — effects as types on builtins

The core idea: **every builtin declares the set of effects it can
have.** Pure builtins declare `{CapPure}`. Impure builtins
declare exactly what they do. The runtime gates each call at the
call site.

```fsharp
type Capability =
  | CapPure           // no observable side effects
  | CapReadFile       // can read from disk
  | CapWriteFile      // can write to disk
  | CapReadNet        // can fetch from network
  | CapWriteNet       // can post to network
  | CapReadEnv        // can read env vars
  | CapReadTime       // can read clock (non-deterministic)
  | CapReadRandom     // can read RNG (non-deterministic)
  | CapReadDB         // can read user DB
  | CapWriteDB        // can write user DB
  | CapExec           // can spawn subprocesses
  | CapSendSecret     // can transmit secrets (e.g., LLM-API calls)
  | CapAny            // forbidden in production; debug-only sentinel

type BuiltInFn = {
  // existing fields ...
  capabilities : Set<Capability>
}
```

Default every builtin to `{CapPure}` then opt-in only the ones
that need impure caps. A retrofit of existing builtins is a
mechanical one-time pass — for each `Builtin.X.Y`, decide what it
does, declare the caps. Easy to review.

`CapSendSecret` is the one that's special: it covers the case
where an LLM call ships user data (prompts may carry secrets the
user didn't realize they're sharing). Worth gating separately
from generic `CapWriteNet`.

## Where checked

At the call site in `Apply` for builtin invocations. Pseudocode:

```fsharp
match fn with
| Builtin name ->
    let fn = lookupBuiltin name
    let required = fn.capabilities
    let granted = state.capabilityCheck.granted
    let missing = required - granted
    if Set.isEmpty missing then
      invoke fn args
    else
      // emit a conflict, dispatch decides
      conflict <- Conflict.CapabilityDenied(missing, callSite)
      let! resolution = state.conflictDispatch conflict
      ... // act on Substitute / Park / AskHuman / FailLoudly
| Package _ | PackageID _ | Pending _ ->
    // user-defined fns inherit caps from their bodies
    ...
```

The check is one set-difference per builtin call. Cheap; almost
free relative to the call itself. Skip the check when `granted`
contains `CapAny` (debug mode); skip the check entirely if the
build flag disables capabilities (unsafe; production never does).

## Result type → flows into conflict dispatch

```fsharp
type CapDecision =
  | Granted
  | Denied of reason: string
  | DeniedAsk     // would-be-denied, but interactive mode is on
```

`DeniedAsk` is shorthand for "this would fail in strict mode, but
the user opted into `--ask`, so emit a conflict that the dispatch
resolves by asking the human." The conflict dispatch and the
human-ask flow is the substrate from
`CONFLICTS-AND-RESOLUTIONS.md` and the event bus from
`EVENT-STREAMS-AND-PARKING.md`. Capability denial **isn't a new
machinery**; it's a producer of standard conflicts.

## Grant model — layered

A user is granted capabilities at multiple layers. Top wins:

```
Layer                          Lifetime         Example
─────────────────────────────────────────────────────────────────
Per-invocation override        single call      --allow http,fileread
Interactive grant (--ask)      session          user accepts a prompt
Session declaration            session-end      session opens with profile
Install-time defaults          forever          dark install asks once
System floor                   forever          CapPure always granted
```

A call needs a cap → check session granted set → if not granted,
check session declaration → if not, check install defaults → if
not, in `--ask` mode prompt human → in strict mode deny.

The granted set on `ExecutionState` is what the cap-check reads.
Other layers feed into it.

## Interactive grants — over the event bus

When `--ask` mode encounters a needed-but-not-granted cap:

1. Emit `Conflict.CapabilityDenied(cap, site, reason: "not yet granted")`
2. Dispatch returns `Resolution.AskHuman(query)` with `query =
   "Allow CapWriteFile for this session?"`
3. Frame parks on `HumanResponded` filtered to the query ID
4. Viewer subscribes; renders the prompt
5. User chooses: deny / grant-once / grant-for-session /
   grant-and-remember
6. Their choice publishes `HumanResponded`; cap-state updates
7. Parked frame wakes; cap-check re-runs; succeeds; call proceeds

This means caps are **first-class in the runtime UX**. Not a
pop-up dialog hacked in — a regular event-stream consumer like
everything else.

## LLM-prompt side — generate-with-granted-caps-only

The LLM generate-prompt builds the list of available builtins
**filtered to the session's granted caps**. The model literally
doesn't see `Builtin.File.delete` if `CapWriteFile` isn't
granted. Reasons:

- Belt-and-suspenders with the runtime gate
- The LLM can't accidentally generate code that immediately hits
  a denial cycle
- The LLM may produce *cleaner* code knowing it has fewer tools
- The generated body's effective-cap-set is the union of caps of
  the builtins it calls — which is bounded by the session-granted
  set, so the runtime check almost-always passes when the LLM
  followed the prompt

The runtime gate is still authoritative — LLMs lie. But the
prompt-side filter shrinks the surface where lies matter.

## User-defined fns — effective caps

User-defined (and PDD-materialized) fns don't *declare* caps;
they have an *effective* set computed from what they call.

```fsharp
effectiveCaps(fn) =
  fn.body
  |> walkExprs
  |> Set.unionAll (fun expr ->
      match expr with
      | Apply(Builtin name, _) -> (lookupBuiltin name).capabilities
      | Apply(Package fn, _) -> effectiveCaps(lookup fn)  // memoized by hash
      | Apply(PackageID id, _) -> effectiveCaps(lookupByID id)
      | _ -> {})
```

Three open questions:

1. **When computed?** At parse-time, at call-time, lazily on
   demand? Probably at parse-time for `Package(hash)` (bodies are
   immutable; cache by hash). At first-call for `PackageID`.
   Recomputed when a `PackageID` body changes.
2. **What about recursion?** Effective caps of `fib` references
   `fib`. Fixed point. Standard — start empty, iterate to
   convergence. In practice converges in one pass for almost all
   cases.
3. **Annotated overrides?** A user might want to *declare* a fn's
   caps stricter than computed (defense-in-depth) or looser
   (intentional capability hiding for an abstraction boundary).
   Probably yes for stricter; never for looser.

## Sequencing — capabilities first, PDD on top

The integration order matters:

```
1. Land the capability tags + Cap field on BuiltInFn
2. Retrofit existing builtins (mechanical pass)
3. Wire the cap-check into Apply for builtin invocations
4. Install default cap-dispatch behavior (strict-mode = FailLoudly;
   loose-mode = Substitute; ask-mode = AskHuman)
5. Build the install-time grant flow (`dark install` asks)
6. Build the per-invocation flags (--allow / --deny / --ask)
7. Land the event-bus-driven interactive grant flow
8. Build the effective-cap computation for user-defined fns
9. ... only NOW is the substrate ready for PDD
10. PDD layers on top:
    - Materializer asks for needed caps via the same flow
    - Generate-prompt filters by granted caps
    - Trace records every cap-decision (already covered by
      ConflictResolved events)
```

PDD doesn't add new mechanism to the cap system. It hooks into
the existing event bus + dispatch.

## What this unlocks

- **Safe LLM-generated code execution.** The whole bet.
- **Auditable trust.** Every cap-grant + cap-use is in the trace.
- **Composable trust.** Different sessions can have different
  cap profiles. A "review" session might be `CapReadFile` only.
  A "prod" session might require explicit secret-bearer caps.
- **Sandboxing built into the language.** Other languages bolt
  this on (containers, seccomp, gVisor); we have it for free at
  the call site.
- **Cap-aware refactoring.** Tools can "show me fns that touch
  `CapSendSecret`" — useful for security review, license
  compliance, dependency analysis.

## Open questions (beyond the three on effective caps)

- **Per-user vs per-session?** Caps probably live per session but
  with per-user defaults. A user has a "trust profile" that
  feeds default grants.
- **How does cap-state sync?** If a user grants a cap on one
  instance, does the next instance know? Probably not — caps
  are local. But the *audit* of cap-uses is content-addressed
  and syncs (it's in the trace).
- **Cap-revocation mid-session.** Should you be able to revoke
  `CapWriteNet` after granting it? Probably yes; any in-flight
  call holding the grant continues, but new calls fail.
- **Granularity within a cap.** `CapWriteFile` is broad — should
  there be `CapWriteFile(directory)`? Probably yes eventually,
  but not v1.
- **Network caps with target restrictions.** `CapWriteNet` →
  `CapWriteNet(host)`? Same reasoning — v2.
- **Caps for Dark code itself.** What's the cap of "modify the
  package store"? `CapWritePackage` or treat it as `CapWriteDB`?
  Probably a new cap; this is privileged-enough to warrant its
  own gate.
- **Test mode.** Tests should default to `CapAny` so they can
  exercise impure paths, but each builtin records what it
  *would* have needed, and the test framework asserts no
  unexpected effects. Separate sketch.

## Compared to existing systems

This is roughly **capability-based security** (Joe-E, Pony,
WebAssembly Component Model). Differences worth noting:

- We don't pass capabilities as values (no "capability handles"
  threaded through code). Caps are ambient on `ExecutionState`,
  checked at call sites. Simpler model; less granular.
- Caps are tagged on builtins, not types. Other systems make
  caps part of the type system; we keep them as a separate set.
- The check is at *invocation*, not at *value transfer*. We're
  protecting effects, not data flow. (If we wanted data-flow
  caps we'd need an effects-system, which is a bigger lift.)

This is the simplest model that solves the LLM-generated-code
problem. If we later want stronger guarantees, we can lift to
an effects-system; the cap-tag-per-builtin work doesn't go to
waste.
