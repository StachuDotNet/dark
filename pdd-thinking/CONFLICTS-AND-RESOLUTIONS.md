# Conflicts + Resolutions

> **v0 design — deepened from sketch via loop T14 (2026-05-20).**
> Grounded in main: `LibDB/Rebase.fs` (RebaseConflict type +
> getConflicts), `LibDB/Merge.fs`, `LibDB/PackageOpPlayback.fs`
> (validation flow), `ProgramTypes.fs:670-722` (Constraints +
> commented BranchMergeConflict reference).

The load-bearing substrate. The same primitive handles SCM merge
conflicts, runtime missing-name conflicts, capability denials,
type-mismatch-on-materialization, sync disagreements, and human
review timeouts. **Building this well unlocks SCM + sync work
broadly — not just PDD.**

## What exists on main today

Concrete check via `git show main:...`:

- **`LibDB/Rebase.fs`** has `RebaseConflict = { owner; modules;
  name; itemType }` — narrow shape, just the *location* of a
  conflict. `getConflicts` queries: "same (owner, modules, name,
  itemType) modified on both sides since fork point."
- **`LibDB/Merge.fs`** has the merge-into-parent flow. Likely
  calls into Rebase's conflict detection.
- **`LibDB/PackageOpPlayback.fs`** applies ops to projection
  tables; validation is implicit (the op shapes are typed; broken
  refs flow through `package_dependencies` propagation).
- **`ProgramTypes.fs:670-722`** has a comment block describing
  "Constraints alongside merge and propagation conflicts, routed
  through the same `status` / `review` / LSP flow." Mentions a
  commented-out `BranchMergeConflict` type. The vision is
  *already* "unify conflict surfacing"; nothing has crossed F#
  module boundaries yet.

What's *missing* relative to this design:

- A unified `Conflict` type covering SCM + runtime + capability
  + sync. Today only `RebaseConflict`.
- A `ConflictDispatch` field on `ExecutionState`. Today no such
  hook; SCM goes one way, runtime errors raise.
- Persistence of conflicts + resolutions as auditable rows.
  Today the conflicts are computed on demand from `locations`
  joins, not stored.
- The Park outcome — main has no parking primitive, full stop.
  (See `EVENT-STREAMS-AND-PARKING.md`.)

Design below assumes the existing SCM conflict detection
(Rebase.fs's `getConflicts`) becomes a *producer* of unified
Conflict values, feeding the new dispatch.

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

## Persistence — `conflicts_v0` + `conflict_resolutions_v0`

Two new SQLite tables. Schema-hash bumps; kill-and-fill replays.

```sql
-- A conflict that was detected (auto-resolved or surfaced).
-- Content-addressable: the id is sha256 of (kind || canonical-payload || created_at-bucket).
-- This makes the conflict syncable like any other op.
CREATE TABLE IF NOT EXISTS conflicts_v0 (
  id              TEXT PRIMARY KEY,             -- content hash
  kind            TEXT NOT NULL,                -- 'FnNotFound' | 'PendingUnresolved' | ...
  payload_blob    BLOB NOT NULL,                -- binary-serialized Conflict variant
  detected_by     TEXT NOT NULL                 -- which subsystem emitted it
                    REFERENCES accounts_v0(id),
  call_context    BLOB,                         -- serialized CallContext (frame, branch, agent)
  created_at      TIMESTAMP NOT NULL DEFAULT (datetime('now')),
  status          TEXT NOT NULL DEFAULT 'Open'  -- 'Open' | 'Resolved' | 'Abandoned'
);
CREATE INDEX IF NOT EXISTS idx_conflicts_status
  ON conflicts_v0(status) WHERE status = 'Open';
CREATE INDEX IF NOT EXISTS idx_conflicts_kind ON conflicts_v0(kind);


-- The resolution chosen for a conflict.
-- One conflict can have one resolution (1:1) — but the row records
-- the path: auto-rule / policy / human-asked / fail.
CREATE TABLE IF NOT EXISTS conflict_resolutions_v0 (
  conflict_id     TEXT PRIMARY KEY
                    REFERENCES conflicts_v0(id),
  outcome         TEXT NOT NULL,                -- 'Substitute' | 'Park' | 'PickSide' | 'RetryWith' | 'AskHuman' | 'FailLoudly'
  outcome_blob    BLOB NOT NULL,                -- serialized Resolution variant
  decided_by_rule TEXT,                         -- 'AutoRule:<name>' | 'Policy:<name>' | 'Human' | 'Default'
  decided_by      TEXT REFERENCES accounts_v0(id),   -- who picked, if Human
  decided_at      TIMESTAMP NOT NULL DEFAULT (datetime('now'))
);

-- Optional later: conflict_log_v0 (history of repeated occurrences;
-- a conflict that fires N times gets one row in conflicts_v0 + N in
-- the log, to track frequency without bloating the main table).
```

Auto-resolutions get a row too — that's the audit trail. "Why
did this conflict not surface to me?" — because rule X fired
automatically. Recorded.

**Sync:** `conflicts_v0` and `conflict_resolutions_v0` rows are
content-addressable and syncable. When a remote peer surfaces a
conflict, it lands on the local instance via the same op stream.
This is how cross-instance conflict-resolution works without
extra plumbing.

## ConflictDispatch — concrete F# shape

On `ExecutionState`:

```fsharp
type ExecutionState = {
  // ...existing fields...
  conflictDispatch : ConflictDispatch
}

and ConflictDispatch =
  Conflict -> CallContext -> Ply<Resolution>

and CallContext = {
  agent       : AccountId         // who's doing this (per IDENTITY.md)
  agentKind   : IdentityKind      // Human or Agent (different defaults)
  delegation  : Option<DelegationId>   // if agent: under what authority
  branchId    : BranchId
  toleranceMode : ToleranceMode   // Strict | Loose | Debug
  capsGranted : Set<Capability>
  callSite    : CallSite          // where in the program
  trace       : TraceId           // active trace
}
```

The dispatch is **one fn**; internal pattern-match on Conflict
variant routes to per-kind handlers. Each handler is configurable
(installed by LibMatter for SCM kinds, by capability machinery
for cap kinds, by PDD for materialization kinds).

```fsharp
// Default dispatch chain inside the fn:
let dispatch : ConflictDispatch = fun conflict ctx -> uply {
  // Layer 1: per-kind auto-rule
  match! tryAutoRule conflict ctx with
  | Some res -> recordConflict conflict ctx res "AutoRule"; return res
  | None ->

  // Layer 2: declared policy (per session / per branch / per namespace)
  match! tryPolicy conflict ctx with
  | Some res -> recordConflict conflict ctx res "Policy"; return res
  | None ->

  // Layer 3: park + ask the relevant agent
  let askedAgent = whoToAsk conflict ctx
  let! res = parkAndAsk conflict ctx askedAgent
  recordConflict conflict ctx res "Human"
  return res
  // (FailLoudly is the parkAndAsk timeout / decline path)
}
```

`whoToAsk` picks the right human:
- Cap denial for agent X owned by human Y → ask Y
- SCM op-vs-op in Y's namespace → ask Y
- FnNotFound in agent X's code → ask X's owner
- Unknown / can't route → fail loudly to the active session user

This is what makes the dispatch agent-aware (per IDENTITY.md).

## Examples mapped to today + the new design

### FnNotFound: a builtin call references something unknown

**Today:** `LibExecution.Interpreter` calls
`raiseRTE (RTE.FnNotFound ...)`.

**New:**

```
1. Conflict emitted:
     Conflict.FnNotFound { name = "Foo.bar"; site = ... }

2. Dispatch checks auto-rule:
     toleranceMode = Strict  → no auto-rule
     toleranceMode = Loose   → AutoRule "substitute-default"
     PDD-mode               → AutoRule "retry-via-materializer"

3. Records the conflict + chosen resolution in conflicts_v0 +
   conflict_resolutions_v0.

4. Returns the Resolution to the caller (which acts on it).
```

### SCM op-vs-op: two patches both add Foo.bar

**Today:** `Rebase.fs.getConflicts` returns `RebaseConflict`;
merge halts.

**New:**

```
1. Rebase.fs.getConflicts now produces Conflict values:
     Conflict.OpVsOp { location = ...; current = ...; proposed = ... }
   for each RebaseConflict.

2. Each Conflict goes through dispatch:
     auto-rule "namespace-owner-wins" → if proposer != owner,
       resolution = PickSide owner
     no auto-rule fires → AskHuman (the side-by-side webview)

3. Resolution recorded. The owner sees the request via the
   event bus; their decision becomes a new op.
```

### Capability denial (NEW — main has no cap system)

**New:**

```
1. agent X's frame: call HttpClient.post
2. cap-check: needs CapWriteNet; agent doesn't have it.
3. Conflict.CapabilityDenied { cap = CapWriteNet; site = ... }
4. Dispatch:
     auto-rule: if agent has 'ask-on-deny' policy → AskHuman
     else → FailLoudly (typed RTE)
5. AskHuman routes to agent X's owner via the event bus.
   Owner sees: "csv-helper wants CapWriteNet. Allow / Allow once
   / Deny." Their click produces a delegation update op.
6. Agent's frame parks (per EVENT-STREAMS-AND-PARKING) on the
   delegation-updated event; wakes when owner decides.
```

### Sync divergence

**New (after share-5 lands per STABILITY-AND-SHARING):**

```
1. Sync inbound: op O arrives at this peer; targets location L
2. Local state: L already points at different hash H'.
3. Conflict.SyncDivergence { location = L; local = H'; remote = O.hash }
4. Dispatch:
     auto-rule "namespace-owner-wins": if O.author owns L, accept;
       else convert O into ApprovalRequest op
     else → Park (wait for owner decision)
5. Either way recorded. The sync proceeds; the conflict is
   *not* a blocker (per STABILITY-AND-SHARING 2025-11-12 stance).
```

## Errors-as-conflicts (the bigger move)

> Added 2026-05-20 per user direction: *"some of the existing
> things that are 'built in' as runtime errors or parse-time
> errors should perhaps be abstracted in a way such that someone
> can decide how such conflicts should be resolved/handled."*

Today, dozens of error sites in the interpreter and type-checker
unconditionally raise:

- `Interpreter.fs` has **~35 `raiseRTE` call sites**
- `TypeChecker.fs` has **~26** more
- `RuntimeError` on main is already a typed hierarchy
  (`Ints.DivideByZeroError`, `Lists.TriedToAddMismatchedData`,
  `Lets.PatternDoesNotMatch`, `Strings.NonStringInInterpolation`,
  `Bools.AndOnlySupportsBooleans`, etc.) — organized by domain,
  each with specific variants

The shape is *already* configuration-ready. What's missing is the
*configurability*: every raise becomes "emit a Conflict; let the
dispatch decide; act on the resolution."

### The move

Replace every `raiseRTE (RuntimeError.Ints DivideByZeroError)`
with:

```fsharp
let! resolution =
  state.conflictDispatch
    (Conflict.RuntimeError (RuntimeError.Ints DivideByZeroError))
    callContext

match resolution with
| Resolution.Substitute dval -> return dval     // e.g. return DInt64 0L
| Resolution.FailLoudly err  -> raiseRTE err     // honor the original behavior
| Resolution.Park selector   -> return! park selector
| Resolution.AskHuman query  -> return! askThenResume query
| Resolution.RetryWith _     -> // doesn't apply to RTEs; treat as FailLoudly
```

The default dispatch in strict mode (production, tests) is
`FailLoudly` — **identical behavior to today**. The substrate
isn't changing semantics; it's adding a hook.

In loose/dev mode, the dispatch can substitute defaults (1/0 →
0, unwrap None → unit, etc.) and record the substitution. The
trace shows what got recovered.

In ask-human mode, the developer is the dispatch — the program
pauses on division-by-zero with "0 here? abort? retry with
different input?" Useful for active dev.

### Categorization — what's configurable, what isn't

**Configurable (becomes Conflict.RuntimeError):**

- `Ints.DivideByZeroError`, `NegativeExponent`, `ZeroModulus`,
  `OutOfRange`
- `Lists.TriedToAddMismatchedData`, list-index-out-of-bounds
- `Dicts.TriedToAddKeyAfterAlreadyPresent`,
  `TriedToAddMismatchedData`
- `Lets.PatternDoesNotMatch`
- `Matches.NoMatchingPattern`
- `Strings.NonStringInInterpolation`
- `Bools.AndOnlySupportsBooleans` etc. (probably; debatable)
- `Stdlib.Option.unwrap` on None
- `Stdlib.Result.unwrap` on Error
- Type-checker mismatches at call sites (per-arg)
- Type-checker mismatches at let-bindings
- Missing builtin (FnNotFound — already covered as a Conflict)
- Recursion / stack overflow (could substitute default; usually
  better to fail)

**Not configurable (stays an unconditional raise — these are
substrate bugs, not program errors):**

- Internal invariant violations (`Exception.raiseInternal "msg"`)
- Anything in the F# substrate that's "this should never happen"
- OOM, stack-machine corruption, serializer failures
- Operating-system-level errors (file-handle exhaustion, etc.)

The distinction: **a configurable error is one where a user
might reasonably want a different policy than "halt."** A
non-configurable error is one where halting is the *only* sane
response — because the substrate itself is in an undefined state.

### Parse-time errors

Parse errors are trickier — they happen *before* the dispatch is
even instantiated. The CallContext doesn't exist yet; there's no
agent identified; the toleranceMode is undefined.

Two approaches:

- **(a) Defer to a parser-policy struct.** The CLI / editor /
  agent that invoked the parser passes a `ParsePolicy` along
  with the source: `Strict | Loose | AskHuman | UseFallback`.
  `Strict` is today. `Loose` substitutes a "syntactic
  placeholder" expression and emits a Conflict.ParseFailed for
  the runtime dispatch to handle when the expression is
  evaluated. This is the same shape as Pending — parse-time
  fails become runtime conflicts.
- **(b) Always try a fallback.** LibParser already has
  `OnMissing.Allow` / `OnMissing.AllowPending` / `OnMissing.Strict`
  policies for unresolved names. Extend the same mechanism to
  *syntactic* failures: emit a `Pending`-shaped expression at
  every unparseable region; the runtime dispatch decides what
  to do when execution reaches it.

(b) is more uniform. (a) is less invasive. Open decision.

### What this enables

- **PDD-style tolerance** without PDD: a developer running in
  loose mode sees division-by-zero return 0, with a trace
  annotation; they review the trace, decide whether to fix the
  source. No LLM involvement required (per the AI-opt-in
  constraint).
- **Test modes**: tests run with strict dispatch. CI gets
  identical-to-today behavior.
- **Refactor-aware execution**: a refactor that changes a fn's
  type signature triggers `TypeMismatch` conflicts at each call
  site; the dispatch can park-and-ask the human to choose
  per-call ("update this call to the new signature, or revert
  the refactor").
- **Live-development workflow**: while editing, the program
  keeps running — div-by-zero substitutes, pattern-mismatch
  substitutes, missing-fn substitutes. The trace builds up the
  list of substitutions. You fix them when you're ready.

This is the original *tolerant runtime* claim from CLAIMS §4,
now mechanically achievable.

### Sequencing — when this work happens

This isn't a separate chunk in ROADMAP §"Chunks needed" — it's a
**deepening of C4 (conflict resolution dispatch)**. The work
is:

1. Land the dispatch + tables (Phase 2 — alongside identity +
   capabilities).
2. Pick the first few raise sites to migrate (probably the Ints
   ones; smallest blast radius).
3. Migrate each `raiseRTE` site to use the dispatch. ~35 + 26 =
   ~60 sites; each is a 4-line diff (emit Conflict, match
   Resolution, act).
4. Auto-rule defaults stay "FailLoudly" so behavior matches
   today by default.

This can ship incrementally — sites migrate one by one, with
auto-rule preserving current behavior until policies are
deliberately added.

### Risk — dispatch latency on the hot path

Every runtime call site that *might* error now does an
extra-cheap dispatch lookup. Today's raise is a stack-unwind; the
new path is a dictionary lookup + branch.

Probably negligible: the dispatch's auto-rule fast-path is a
single match arm returning `FailLoudly`. We're not adding
allocations or async hops. Measure on the hot loop (integer
arithmetic in tight loops); if there's a regression, gate the
dispatch behind a "tolerance mode" check and short-circuit when
strict.

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
