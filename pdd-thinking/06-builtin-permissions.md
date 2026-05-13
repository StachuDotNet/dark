# 06 — Builtin Permissions / Capability Model

> Stachu's directive: "we may need to figure out 'builtin restrictions' sooner than later — lots of wild code generation, don't want to get into issues. CLI installers should be able to have users choose what the agents are allowed to do."

## Why this can't wait

PDD will generate code that calls builtins we didn't pre-audit. Without a capability gate, a hallucinated body might:
- `File.delete "*"` because the prompt mentioned cleanup
- `HttpClient.post "https://random.url" secret` because the LLM thought "send" was right
- `Db.delete *` because the model name-matched something

Capabilities are part of the *threat model*, not optional polish. Day 1 might skip them (in the trusted dev loop). Day N must have them.

## What Darklang already has

Reading `RuntimeTypes.fs:1340-1351`, every `BuiltInFn` already has a `Previewable` tag:

```fsharp
type Previewable =
  | Pure
  | ImpurePreviewable    // safe to preview, e.g. DateTime.now
  | Impure               // server-only, e.g. DB.update
```

And there's a `Deprecation.Harmful` marker checked via `PackageManager.isHarmful`, with an `ExecutionState.allowHarmful` escape hatch. This is *one-bit capability*: harmful yes/no.

The PDD design extends this from "one harmful bit" to "a small set of capability tags."

## The capability tags

Start with a small, fixed set. Resist the temptation to make this open-ended at first — fewer tags is easier to reason about.

```fsharp
type Capability =
  // No effects beyond CPU/memory.
  | CapPure

  // Reads things that depend on outside state but don't modify it.
  | CapReadTime         // DateTime.now, time-based ops
  | CapReadRandom       // Int.random, etc.
  | CapReadEnv          // env vars, system info
  | CapReadFile         // local filesystem reads
  | CapReadNet          // outbound HTTP GET, DNS, etc.

  // Writes / makes external changes.
  | CapWriteFile        // local filesystem writes/deletes
  | CapWriteNet         // HTTP POST/PUT/PATCH/DELETE, sockets
  | CapWriteDB          // user DB writes
  | CapExec             // sub-process spawn, eval-of-string-as-code
  | CapSendSecret       // explicit "this fn handles secrets"

  // Wildcards used for grants, not checks.
  | CapAny              // grant everything (dev mode)
```

Each `BuiltInFn` gets a `capabilities : Set<Capability>` field. Most are `{ CapPure }`.

### Examples

| Builtin | Capabilities |
|---|---|
| `Int64.add` | `{ CapPure }` |
| `DateTime.now` | `{ CapReadTime }` |
| `Int.random` | `{ CapReadRandom }` |
| `File.read` | `{ CapReadFile }` |
| `File.delete` | `{ CapWriteFile }` |
| `HttpClient.get` | `{ CapReadNet }` |
| `HttpClient.post` | `{ CapWriteNet }` |
| `Db.update` | `{ CapWriteDB }` |
| `Process.spawn` | `{ CapExec }` |
| `Eval.string` | `{ CapExec; CapAny }` |
| `Secrets.get "X"` | `{ CapSendSecret }` |

## Where it's enforced

### At the call site (the only place that matters)

In `Interpreter.fs`, when applying a `Builtin` fn:

```fsharp
| Apply (..., thingToApply = Reg r, ...) ->
    match registers[r] with
    | DApplicable (AppNamedFn { name = FQFnName.Builtin b }) ->
        let fn = exeState.fns.builtIn[b]
        match exeState.capabilityCheck fn.capabilities fn.name with
        | Granted ->
            // existing path
            uply { let! result = fn.fn (exeState, vm, ..., args) ... }
        | Denied reason ->
            // depending on RecoveryPolicy: substitute default, ask user, or fail
            handleDenied fn reason
```

**Key invariant:** the check happens at the call site, not when the fn is constructed. Same fn, different sessions, can be allowed or denied.

### The `capabilityCheck` field

Added to `ExecutionState`:

```fsharp
type CapabilityDecision =
  | Granted
  | Denied of reason : string
  | DeniedAsk     // "ask the human"

capabilityCheck : Set<Capability> -> FQFnName.FQFnName -> CapabilityDecision
```

The default impl (set by the CLI flag layer) compares the fn's capabilities against the session's grant set:

```fsharp
let mkCapabilityCheck (granted : Set<Capability>) : Set<Capability> -> _ -> _ =
  fun fnCaps fnName ->
    if granted.Contains CapAny then Granted
    elif Set.isSubset fnCaps granted then Granted
    else
      let missing = Set.difference fnCaps granted
      Denied (sprintf "missing: %s" (missing |> Set.map string |> String.concat ", "))
```

### What's NOT enforced

- **Package functions don't carry capabilities directly.** They inherit from their transitive builtin calls. If `foo` calls `HttpClient.get`, then `foo` effectively requires `CapReadNet`. We *could* compute this transitively at PT2RT time, but for PoC we just check at the leaf (the builtin call). Cheap. Sound.
- **Pure user code isn't gated.** No `Int64.add` checks.

## CLI surface

### Install-time defaults

`./scripts/dark-cli install` (hypothetical) asks:

```
Allow this Darklang to:
  [ ] Read files outside the project dir         (CapReadFile)
  [ ] Write files outside the project dir        (CapWriteFile)
  [ ] Make outbound HTTP requests                (CapReadNet)
  [ ] Make outbound state-changing HTTP requests (CapWriteNet)
  [ ] Spawn subprocesses                         (CapExec)
  [ ] Access secrets                             (CapSendSecret)

Write to ~/.config/darklang/capabilities.json
```

### Per-invocation overrides

```
dark run myScript.dark --allow http,fileread
dark run myScript.dark --deny exec
dark run myScript.dark --ask          # prompt on first use of each cap
```

### Per-call escalation (interactive)

When a denied call happens in `--ask` mode:

```
[pdd] foo(...) wants to call HttpClient.get "https://example.com"
      This needs capability CapReadNet, which isn't granted.
      Allow this call? [y]es / [a]lways / [n]o / [N]ever
```

`Always` adds `CapReadNet` to the session grants. `Never` adds it to denies. `Yes`/`No` are one-shot.

## What the LLM sees

The materialize prompt for `generate` includes the available builtins. **If a capability isn't granted, the corresponding builtins are *hidden* from the prompt.**

```fsharp
let visibleBuiltins (granted : Set<Capability>) (builtins : Builtins) : Builtins =
  let visible (fn : BuiltInFn) =
    Set.isSubset fn.capabilities granted
  { fns = builtins.fns |> Map.filter (fun _ fn -> visible fn)
    values = builtins.values }
```

The LLM literally won't know `HttpClient.post` exists in a session that denies `CapWriteNet`. It'll generate code that doesn't need it.

(Caveat: it might generate something that does, and trip the runtime check. That's fine — the check is the source of truth.)

## Recovery policy interaction

A denied capability is a recoverable error in tolerant mode:

```fsharp
let tolerantPolicy (rte : RTE.Error) : RecoveryPolicy =
  match rte with
  ...
  | RTE.CapabilityDenied (fn, cap) -> EmptyBody  // substitute default
  | RTE.CapabilityDeniedAsk (fn, cap) -> AskUser
  ...
```

So a denied `File.read` returns `""` (DString default) and the trace records it. The program keeps moving.

In strict mode, it crashes. Tests run strict.

## Trace events

```
capability_check  fn=HttpClient.get  requested={CapReadNet}  granted={CapReadFile,CapReadTime}  decision=Denied  reason="missing: CapReadNet"
capability_grant  cap=CapReadNet  scope=once  source=interactive_ask
```

The trace is auditable. After the run, the user sees every effect attempted and what was denied.

## Implementation order

1. **Add `Capability` enum** to RuntimeTypes.fs (5 lines).
2. **Add `capabilities : Set<Capability>` to `BuiltInFn`** (one field, then fix all builtin definitions — there are dozens).
   - Default everything to `{ CapPure }`. Then go through each builtin and bump the ones that need more. The compiler tells you when fields are missing.
3. **Add `capabilityCheck` to `ExecutionState`**.
4. **Hook it into Apply in Interpreter.fs**.
5. **Add `RTE.CapabilityDenied`** to RTE union.
6. **CLI flag parsing** — `--allow X,Y,Z`.

For PDD spike: maybe just hard-code `CapAny` granted in tests/dev, and only seriously implement the gate when starting to run materialized code in earnest. The infrastructure can be there, dormant, until needed.

## Open questions

- **Should `Pending` calls be capability-checked?** A pending fn doesn't have a body yet. We could refuse to materialize fns that *would* need ungranted caps — but we don't know what caps they need until they're materialized. Recursion: materialize, check, reject, materialize again with hidden builtins... probably just rely on the post-materialization check (the body's builtin calls are checked at runtime).
- **Capability inference at PT2RT**: walk the body, compute the transitive cap set, and refuse to call this fn if any are denied? Would be a nice belt+suspenders. Skip for PoC.
- **Time-bound capabilities**: "allow HTTP for 5 minutes" — useful but post-PoC.
- **Domain-restricted capabilities**: "allow HTTP to anthropic.com only" — important for production. Encode as a more-specific capability variant?

## Connection to other docs

- `02-libexecution-changes.md` — adds `capabilityCheck` to `ExecutionState`.
- `05-tolerant-runtime.md` — `RecoveryPolicy` for `CapabilityDenied`.
- `07-human-in-loop.md` — capability-ask is a primary human-trigger.
- `08-tracing-as-artifact.md` — capability events are trace events.
