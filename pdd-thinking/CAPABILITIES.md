# Capabilities

> **v0 design — deepened from sketch via loop T15 (2026-05-20).**
> Grounded in main: `BuiltInFn` shape in
> `LibExecution/RuntimeTypes.fs:1607` (no caps field today, but
> `Previewable` enum is adjacent), 9-assembly Builtins layout
> (`Builtins.Pure / Http.Client / Http.Server / Random / Time /
> Cli / CliHost / Language / Matter`), the AI-opt-in
> foundational constraint, and the conflict dispatch from
> `CONFLICTS-AND-RESOLUTIONS.md`.

The fourth substrate piece. **Must land before real PDD work.**
LLM-generated code will try to delete files, hit random network
endpoints, exfiltrate secrets. An ungated runtime is a footgun;
shipping PDD on top of one would be reckless.

## What exists on main today

- **No capability tags** on `BuiltInFn`. The field doesn't exist
  yet.
- **`Previewable` enum** (`Pure | ImpurePreviewable | Impure`)
  *is* on the BuiltInFn — it's adjacent to capabilities but
  coarser. It answers "can we safely preview" (caching question),
  not "what effects does this have" (security question). Both
  signals are useful; keep both.
- **9 Builtin assemblies, domain-split**:
  - `Builtins.Pure` — already declares purity in the name
  - `Builtins.Http.Client` — outbound network
  - `Builtins.Http.Server` — inbound network + server lifecycle
  - `Builtins.Random` — non-deterministic randomness
  - `Builtins.Time` — non-deterministic clock
  - `Builtins.Cli` — CLI-shape stuff (stdin/stdout/args)
  - `Builtins.CliHost` — host-side process management
  - `Builtins.Language` — language reflection (typecheck, parse,
    eval) — mostly pure but eval grants the caller's caps
  - `Builtins.Matter` — package store ops (PM, SCM)
- **No grant flow.** No install-time UX, no `--allow` flags, no
  `--ask` mode. Builtin calls are always permitted.

**Implication for cap retrofit:** the assembly split *already*
implies effect categories. The work is **per-assembly defaults +
per-fn overrides** — not a per-builtin-from-scratch annotation
pass. Much smaller than the sketch implied.

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

## Per-assembly default caps

Concrete retrofit table. One row per existing Builtins assembly,
its default cap-set, and the per-fn overrides (where most fns in
the assembly are pure but a few aren't, or vice versa).

| Assembly | Default caps | Override examples |
|---|---|---|
| `Builtins.Pure` | `{CapPure}` | none (all should be pure) |
| `Builtins.Http.Client` | `{CapReadNet, CapWriteNet}` | distinguish GET (Read) vs POST/PUT/DELETE (Write); `CapSendSecret` on any fn that ships auth headers |
| `Builtins.Http.Server` | `{CapBindPort, CapReadNet, CapWriteNet}` | server-bind needs `CapBindPort` (new cap); per-request handlers run with caller's caps |
| `Builtins.Random` | `{CapReadRandom}` | n/a |
| `Builtins.Time` | `{CapReadTime}` | n/a |
| `Builtins.Cli` | `{CapPure}` mostly | `printLine` → `{CapWriteStdout}`; `readLine` → `{CapReadStdin}`; arg-access → `{CapReadEnv}` |
| `Builtins.CliHost` | `{CapExec, CapReadFile, CapWriteFile}` | spawn-subprocess is the heaviest; most-fns need ≤ 1 of these |
| `Builtins.Language` | `{CapPure}` (mostly reflection) | `evaluate` grants the *caller*'s caps to the evaluated code |
| `Builtins.Matter` | `{CapReadPackage}` | write-ops (`AddFunction`, `MoveItem`, etc.) → `{CapWritePackage}`; PDD-related → `{CapInvokeLLM, CapSendSecret}` (AI-opt-in) |

New cap tags introduced here (beyond the original list in the
sketch):

- `CapBindPort` — Http.Server-style port-binding (escalated from
  `CapWriteNet` since it's a long-running surface)
- `CapWriteStdout`, `CapReadStdin` — split from CLI-generic
  (helps detect "this code prints things" without granting
  full CLI access)
- `CapReadPackage`, `CapWritePackage` — for the SCM op surface;
  most users have read; only owners get write to their namespace
- `CapInvokeLLM` — **the AI-opt-in gatekeeper**. Denied by
  default. Granting it unlocks PDD + materialization +
  agent-spawn.

The cap tag list is **deliberately open-ended**. New caps get
added when new builtin assemblies arrive. The check is uniform
regardless of cap count.

## Schema — grants + audit log

Two new SQLite tables. Schema-hash bumps; kill-and-fill replays.

```sql
-- Per-account default grants. Loaded into the session's
-- granted-set on login.
CREATE TABLE IF NOT EXISTS capability_grants_v0 (
  account_id    TEXT NOT NULL REFERENCES accounts_v0(id),
  capability    TEXT NOT NULL,           -- 'CapReadFile' | 'CapWriteNet' | ...
  scope         TEXT,                    -- NULL = global; or namespace-glob like 'User.Stachu.*'
  granted_at    TIMESTAMP NOT NULL DEFAULT (datetime('now')),
  granted_by    TEXT REFERENCES accounts_v0(id),
  revoked_at    TIMESTAMP,
  expires_at    TIMESTAMP,               -- NULL = no expiry
  PRIMARY KEY (account_id, capability, scope)
);
CREATE INDEX IF NOT EXISTS idx_caps_grants_active
  ON capability_grants_v0(account_id)
  WHERE revoked_at IS NULL;


-- Audit log: every cap check + outcome.
-- Append-only. The "decision recording" path for both
-- granted-and-fine and denied-and-conflict cases.
CREATE TABLE IF NOT EXISTS capability_log_v0 (
  id              TEXT PRIMARY KEY,                -- random uuid
  account_id      TEXT NOT NULL REFERENCES accounts_v0(id),
  delegation_id   TEXT REFERENCES delegations(id), -- if agent: under what delegation
  capability      TEXT NOT NULL,
  decision        TEXT NOT NULL,                   -- 'Granted' | 'Denied' | 'DeniedAsk'
  conflict_id     TEXT REFERENCES conflicts_v0(id),-- if decision was a conflict
  call_site       TEXT,                            -- optional caller hint
  builtin_name    TEXT NOT NULL,
  branch_id       TEXT REFERENCES branches(id),
  checked_at      TIMESTAMP NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS idx_caplog_account_time
  ON capability_log_v0(account_id, checked_at DESC);
CREATE INDEX IF NOT EXISTS idx_caplog_capability
  ON capability_log_v0(capability, checked_at DESC);
```

**Audit pivots that fall out for free:**

- `SELECT capability, COUNT(*) FROM capability_log_v0 WHERE
  account_id=? GROUP BY capability` — what caps does my agent
  actually use?
- `SELECT * FROM capability_log_v0 WHERE decision='Denied'` —
  what cap-grants would unblock current work?
- `SELECT * FROM capability_log_v0 WHERE capability='CapSendSecret'
  AND checked_at > ?` — security audit: who recently exfil'd what?
- per-`delegation_id` slice = "what did this agent do under this
  authority?"

**Sync:** grants are syncable (per-account ⇒ content-addressable
via account_id + cap + scope hash). The log is local-by-default;
explicitly sharable for audit but probably not on every sync
cycle. (Open decision Q-caps-1 below.)

## Where checked — F# integration

In `LibExecution.Interpreter` (specifically the Apply path for
`Builtin` calls), insert the cap check before invocation:

```fsharp
let executeBuiltinCall
  (state : ExecutionState)
  (vmState : VMState)
  (fn : BuiltInFn)
  (args : List<Dval>)
  : DvalTask =
  uply {
    // ─── new: cap check ────────────────────────────────
    let required = fn.capabilities                    // new field on BuiltInFn
    let granted = state.session.capsGranted
    let missing = required - granted

    if not (Set.isEmpty missing) then
      // Route through the dispatch (per CONFLICTS-AND-RESOLUTIONS)
      let conflict =
        Conflict.CapabilityDenied
          { caps = missing
            site = currentCallSite vmState
            builtin = fn.name }
      let! resolution = state.conflictDispatch conflict (callContext state vmState)
      match resolution with
      | Resolution.Substitute dval ->
        // The dispatch decided to skip the call and substitute.
        logCapDecision state fn "Substitute" conflict
        return dval
      | Resolution.FailLoudly err ->
        logCapDecision state fn "Denied" conflict
        return! raiseRTE err
      | Resolution.Park selector ->
        // Wait for a grant event; on wake re-enter this fn.
        return! park selector
      | Resolution.AskHuman query ->
        // Same as Park but the wait is on the human-answer event.
        return! askThenResume query
      | _ -> ... // RetryWith / PickSide n/a for caps
    else
      // ─── existing path ──────────────────────────────
      logCapDecision state fn "Granted" None
      return! fn.fn (state, vmState, [], args)
  }
```

The strict-mode default for `Conflict.CapabilityDenied` is
`FailLoudly` (matching today's "always-permitted" behavior is
fully reverse-compat: until you add caps to a fn, the missing
set is empty and the check fast-paths through).

## Per-builtin declaration site

Add `capabilities : Set<Capability>` to `BuiltInFn`:

```fsharp
type BuiltInFn = {
  // existing fields...
  capabilities : Set<Capability>      // new
}
```

Provide a default in the assembly's fn-builder helpers so
existing builtins default to their assembly's default cap-set
without per-fn edits:

```fsharp
// in Builtins.Pure helpers:
let pureFn name params returnType description fn = {
  // ...
  capabilities = Set.singleton Capability.CapPure
}

// in Builtins.Http.Client helpers:
let httpClientReadFn ... = { capabilities = Set.ofList [CapReadNet] }
let httpClientWriteFn ... = { capabilities = Set.ofList [CapReadNet; CapWriteNet] }
```

Per-assembly retrofit ~9 helper changes + a per-fn audit for
exceptions = small mechanical PR.

## Install-time grant UX

First-run on a fresh install:

```
$ dark
Welcome to Darklang. Setting up your install.

Some operations need explicit permission to run. We'll ask once
per category; you can change these later with `dark caps`.

  Network access (read + write):
    [Y/n/ask] y          # always allow

  File system access (read + write):
    [Y/n/ask] ask        # ask each session

  Run subprocesses (e.g. git, ssh):
    [Y/n/ask] n          # never; deny silently

  Send secrets across the network (e.g. LLM keys):
    [y/N/ask] n          # never (AI-opt-in default!)

  Read the system clock (non-deterministic):
    [Y/n/ask] y

  Generate randomness (non-deterministic):
    [Y/n/ask] y

  Read/write the package store:
    Read: [Y]
    Write: only in `User.Stachu.*` (auto-set from your account)

Saved to ~/.config/darklang/caps.toml.
```

This populates `capability_grants_v0` for the user's account.

**AI-opt-in gating**: `CapInvokeLLM` and `CapSendSecret` default
to **never**. The user has to deliberately go in and enable them
(or accept the prompt when an opt-in flow asks). The install
flow never even mentions them by default — they're prompted only
when an explicit AI feature is first invoked.

Granular controls layered on top:

- `dark caps list` — show current grants
- `dark caps grant <cap> [--scope=X] [--expires=Y]`
- `dark caps revoke <cap>`
- `dark caps ask <cap>` — set policy to prompt on next use
- Per-invocation: `dark --allow=ReadFile,WriteNet -- run myFn`
- Per-invocation deny: `dark --deny=Exec`

## Connection to Previewable (don't conflate)

`Previewable` and `Capability` are **orthogonal**:

- `Previewable.Pure` = same inputs → same output (cacheable)
- `Previewable.ImpurePreviewable` = output varies but safely
  previewable (DateTime.now)
- `Previewable.Impure` = not previewable
- `Capability` = what *effects* this fn has on the world

A fn can be:

- Pure + `{CapPure}` — most pure fns
- Pure + `{CapReadEnv}` — reads env once but always the same
  during a session (configurable: cache yes, since the env is
  stable; cap-check yes because reading env is an effect)
- Impure + `{CapReadTime}` — DateTime.now
- Impure + `{CapWriteFile}` — File.write

The cap signals "do I have permission?" The previewable signals
"can I show a result without running the side-effect?" Both
useful; both stay on `BuiltInFn`.

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
