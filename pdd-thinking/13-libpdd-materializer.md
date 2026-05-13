# 13 — `LibPDD` Project Sketch

Concrete F# shape of the new project. Read alongside `02-libexecution-changes.md` (the LibExecution side) and `10-day-1-hacking-plan.md` (the order of operations).

## File layout

```
backend/src/LibPDD/
  LibPDD.fsproj
  paket.references
  Defaults.fs          — Dval.defaultFor (small, no deps beyond LibExecution)
  Capability.fs        — Capability enum + check fn (registered in ExecutionState)
  TraceEvents.fs       — JSONL serializers for the new event kinds
  Find.fs              — corpus search (package store + pinned hashes + name+arity)
  Generate.fs          — LLM call + parse-back into PackageFn (depends on LibParser)
  Materializer.fs      — race-driver: orchestrates Find + Generate + Human fallback
  Resolver.fs          — humanResolver default TTY impl + inbox file impl
```

7 files. Probably ~1500 lines total when done. The materializer is the meatiest (~500 lines including diagnostics).

## `LibPDD.fsproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <IsPublishable>false</IsPublishable>
    <IsTrimmable>true</IsTrimmable>
  </PropertyGroup>
  <ItemGroup>
    <None Include="paket.references" />
    <Compile Include="Defaults.fs" />
    <Compile Include="Capability.fs" />
    <Compile Include="TraceEvents.fs" />
    <Compile Include="Find.fs" />
    <Compile Include="Generate.fs" />
    <Compile Include="Resolver.fs" />
    <Compile Include="Materializer.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Prelude/Prelude.fsproj" />
    <ProjectReference Include="../LibExecution/LibExecution.fsproj" />
    <ProjectReference Include="../LibDB/LibDB.fsproj" />
    <ProjectReference Include="../LibParser/LibParser.fsproj" />
  </ItemGroup>
</Project>
```

Note: `Materializer.fs` is last because it depends on everything else.

`paket.references` will need to include the Anthropic SDK package (or whatever Feriel set up — check what's already in `LibAI` if it exists, otherwise add a fresh ref).

## `Defaults.fs`

The tolerant-runtime substitution helper. Pure, no deps beyond `LibExecution.RuntimeTypes`.

```fsharp
module LibPDD.Defaults

open LibExecution.RuntimeTypes
module VT = ValueType

let rec defaultFor (t : TypeReference) : Dval =
  match t with
  | TUnit -> DUnit
  | TBool -> DBool false
  | TInt8 -> DInt8 0y
  | TUInt8 -> DUInt8 0uy
  | TInt16 -> DInt16 0s
  | TUInt16 -> DUInt16 0us
  | TInt32 -> DInt32 0
  | TUInt32 -> DUInt32 0u
  | TInt64 -> DInt64 0L
  | TUInt64 -> DUInt64 0UL
  | TFloat -> DFloat 0.0
  | TChar -> DChar ""
  | TString -> DString ""
  | TList _ -> DList(VT.unknown, [])
  | TDict _ -> DDict(VT.unknown, Map.empty)
  | TTuple (a, b, rest) ->
      DTuple(defaultFor a, defaultFor b, rest |> List.map defaultFor)
  | TOption _ ->
      // Option.None — TODO: pull the actual FQTypeName for Option
      DEnum(FQTypeName.fqBuiltin "Option" 0, FQTypeName.fqBuiltin "Option" 0,
            [], "None", [])
  | TResult (_, _) ->
      // Result.Ok defaultOk — TODO same
      DUnit  // placeholder; tighten Day 2
  | TCustomType _ ->
      // For records: empty record (all fields defaulted) — needs PT lookup.
      // For PoC, fall through to DUnit.
      DUnit
  | TVariable _ -> DUnit
  | TFn _ -> DUnit
  | _ -> DUnit
```

The `TOption` / `TResult` cases need access to `FQTypeName`s — work out what stdlib types they map to. For Day 1, even returning `DUnit` for unknown types is fine. Tests will reveal which cases need filling in.

## `Capability.fs`

```fsharp
module LibPDD.Capability

open LibExecution.RuntimeTypes

type Capability =
  | CapPure
  | CapReadTime
  | CapReadRandom
  | CapReadEnv
  | CapReadFile
  | CapReadNet
  | CapWriteFile
  | CapWriteNet
  | CapWriteDB
  | CapExec
  | CapSendSecret
  | CapAny

type GrantScope =
  | ScopeOnce
  | ScopeSession
  | ScopeAlways
  | ScopeNever

type CapabilityDecision =
  | Granted
  | Denied of reason : string
  | DeniedAsk

let mkChecker (granted : Set<Capability>) (denied : Set<Capability>)
              : Set<Capability> -> FQFnName.FQFnName -> CapabilityDecision =
  fun fnCaps fnName ->
    let blocked = Set.intersect fnCaps denied
    if not (Set.isEmpty blocked) then
      Denied (sprintf "explicitly denied: %A" (Set.toList blocked))
    elif granted.Contains CapAny then
      Granted
    elif Set.isSubset fnCaps granted then
      Granted
    else
      let missing = Set.difference fnCaps granted
      Denied (sprintf "missing: %A" (Set.toList missing))

let allowAll = Set.ofList [ CapAny ]
let strict = Set.empty<Capability>
```

## `TraceEvents.fs`

```fsharp
module LibPDD.TraceEvents

open System.IO
open System.Text.Json

type EventKind =
  | MaterializeStart of handle : System.Guid * name : string
  | CandidateArrived of handle : System.Guid * source : string * elapsedMs : int * hash : string
  | CandidateRejected of handle : System.Guid * source : string * reason : string
  | MaterializeDone of handle : System.Guid * winning : string * hash : string
  | FrameParked of frameId : System.Guid * onHandle : System.Guid
  | FrameResumed of frameId : System.Guid * withHash : string
  | Recovery of loc : string * policy : string * reason : string * substituted : string
  | CapabilityCheck of fn : string * requested : string list * decision : string
  | HumanAsk of queryId : System.Guid * kind : string
  | HumanAnswer of queryId : System.Guid * response : string * latencyMs : int

type TraceWriter =
  { write : EventKind -> int -> unit  // event + t-ms-since-start
    flush : unit -> unit
    close : unit -> unit }

let nullWriter : TraceWriter =
  { write = fun _ _ -> ()
    flush = fun () -> ()
    close = fun () -> () }

let jsonlWriter (path : string) (startTime : System.DateTime) : TraceWriter =
  let stream = File.OpenWrite path
  let writer = new StreamWriter(stream)
  let writeLine (ev : EventKind) (t : int) =
    let payload =
      // Real impl: JsonSerializer.Serialize(...) with custom converters
      // For PoC, hand-roll. Each variant maps to {"t": t, "ev": "...", ...}
      sprintf """{"t":%d,"ev":%A}""" t ev
    writer.WriteLine payload
  { write = writeLine
    flush = fun () -> writer.Flush()
    close = fun () -> writer.Flush(); writer.Close() }
```

The JSON serialization is hand-rolled for now. System.Text.Json with custom converters comes Day 2. The point Day 1 is: events land on disk, you can `grep` them.

## `Find.fs`

```fsharp
module LibPDD.Find

open FSharp.Control.Tasks
open LibExecution.RuntimeTypes

/// In priority order:
/// 1. Pinned hashes (manually promoted)
/// 2. Package store name-exact match
/// 3. Package store name+arity loose match (substring)
let tryFind
  (pm : PackageManager)
  (pinned : Map<string, FQFnName.Package>)
  (pending : FQFnName.Pending)
  : Ply<Option<PackageFn.PackageFn>> =
  uply {
    // 1. Pinned
    match Map.tryFind pending.name pinned with
    | Some hash ->
        match! pm.getFn hash with
        | Some fn -> return Some fn
        | None -> ()  // pinned hash missing — anomaly, log later
    | None -> ()

    // 2. Exact-name match (TBD: needs a name → hashes index from LibPackageManager)
    // ... query the index here ...

    // 3. Loose match — for the PoC, return None and let `generate` win
    return None
  }
```

The name→hash index doesn't exist yet — check `LibPackageManager` for an analog. If not, expose a new query method on `PackageManager`:

```fsharp
// Add to PackageManager:
findFnsByName : nameLike : string -> arity : int -> Ply<List<FQFnName.Package>>
```

## `Generate.fs`

```fsharp
module LibPDD.Generate

open FSharp.Control.Tasks
open LibExecution.RuntimeTypes

type LLMProvider =
  { call : prompt : string -> systemPrompt : string -> Task<Result<string, string>> }

/// Build a prompt for a pending fn given the available context.
let buildPrompt
  (pending : FQFnName.Pending)
  (visibleBuiltins : List<FQFnName.Builtin>)
  (visibleTypes : List<FQTypeName.FQTypeName>) : string =
  // ... format the prompt per docs/03-find-vs-generate.md ...
  ""

/// Returns Some PackageFn on success, None on parser/format failure.
let tryGenerate
  (provider : LLMProvider)
  (parser : string -> Result<PackageFn.PackageFn, string>)
  (pending : FQFnName.Pending)
  (visibleBuiltins : List<FQFnName.Builtin>)
  (visibleTypes : List<FQTypeName.FQTypeName>)
  : Task<Option<PackageFn.PackageFn>> =
  task {
    let prompt = buildPrompt pending visibleBuiltins visibleTypes
    let systemPrompt =
      "You are generating a Darklang function body. Return JSON shaped {sig: ..., body: ...}"
    match! provider.call prompt systemPrompt with
    | Error _ -> return None
    | Ok llmOutput ->
        match parser llmOutput with
        | Ok fn -> return Some fn
        | Error _ -> return None
  }
```

Note: parser strategy per `09-carving-the-codebase.md` "Parser open question" → start with P3 (LLM emits structured JSON; we deserialize into PT directly; we skip LibParser for the spike).

## `Materializer.fs` — the orchestrator

```fsharp
module LibPDD.Materializer

open FSharp.Control.Tasks
open System.Threading
open LibExecution.RuntimeTypes
open LibPDD

type MaterializeOptions =
  { findBudgetMs : int
    generateBudgetMs : int
    allowEmptyBody : bool
    preferPath : Path }

and Path = PreferFind | PreferGenerate | RaceBoth

let defaultOpts =
  { findBudgetMs = 1000
    generateBudgetMs = 1000
    allowEmptyBody = true
    preferPath = RaceBoth }

let deepOpts =
  { findBudgetMs = 5000
    generateBudgetMs = 60000
    allowEmptyBody = false
    preferPath = PreferGenerate }

type MaterializeSource = FromFind | FromGenerate | FromHuman

type MaterializeResult =
  | Materialized of PackageFn.PackageFn * MaterializeSource
  | EmptyBody of TypeReference option
  | Failed of string

/// Race find + generate; first non-failure wins.
let race
  (find : unit -> Task<Option<PackageFn.PackageFn>>)
  (generate : unit -> Task<Option<PackageFn.PackageFn>>)
  (opts : MaterializeOptions)
  (trace : TraceEvents.TraceWriter)
  (pending : FQFnName.Pending)
  : Task<MaterializeResult> =
  task {
    let findCts = new CancellationTokenSource(opts.findBudgetMs)
    let genCts = new CancellationTokenSource(opts.generateBudgetMs)

    let findTask = Task.Run<Option<PackageFn.PackageFn>>(find, findCts.Token)
    let genTask = Task.Run<Option<PackageFn.PackageFn>>(generate, genCts.Token)

    let firstWinner = Task.WhenAny [| findTask :> Task; genTask :> Task |] |> Async.AwaitTask
    let! winner = firstWinner

    let extract (t : Task<Option<PackageFn.PackageFn>>) source =
      if t.IsCompletedSuccessfully && Option.isSome t.Result then
        Some (Materialized(Option.get t.Result, source))
      else None

    match extract findTask FromFind with
    | Some r ->
        genCts.Cancel()
        return r
    | None ->
        match extract genTask FromGenerate with
        | Some r ->
            findCts.Cancel()
            return r
        | None ->
            // First-completed-was-empty. Wait briefly for the other.
            try
              let! _ = Task.WhenAll [| findTask :> Task; genTask :> Task |]
              ()
            with _ -> ()
            match extract findTask FromFind with
            | Some r -> return r
            | None ->
                match extract genTask FromGenerate with
                | Some r -> return r
                | None ->
                    if opts.allowEmptyBody then
                      return EmptyBody None
                    else
                      return Failed "both paths empty within budget"
  }

/// Entry point — installed into PackageManager.materializeFn.
let materializeFn
  (provider : Generate.LLMProvider)
  (pm : PackageManager)
  (parser : string -> Result<PackageFn.PackageFn, string>)
  (pinned : Map<string, FQFnName.Package>)
  (trace : TraceEvents.TraceWriter)
  (pending : FQFnName.Pending)
  : Ply<MaterializeResult> =
  uply {
    let opts = defaultOpts
    let find () = (Find.tryFind pm pinned pending) |> Ply.toTask
    let generate () = Generate.tryGenerate provider parser pending [] []
    let! result = race find generate opts trace pending |> Ply.ofTask
    return result
  }
```

(Plenty of TODOs above — `Ply.toTask`/`Ply.ofTask` may not exist by those names. Check Prelude/Ply helpers when wiring up.)

## `Resolver.fs` — human-in-loop default

```fsharp
module LibPDD.Resolver

open System
open FSharp.Control.Tasks

type HumanQuery =
  | AskMaterialize of name : string * candidates : List<string>
  | AskCapability of fn : string * cap : string
  | AskAnnotation of fn : string * body : string

type HumanResponse =
  | RespAccept
  | RespReject of reason : string
  | RespEdit of newBody : string
  | RespGrant of scope : Capability.GrantScope

let ttyResolver : HumanQuery -> Ply<HumanResponse> =
  fun q ->
    uply {
      match q with
      | AskCapability(fn, cap) ->
          printfn "[pdd] %s wants capability %s. Allow? [y/a/n/N]: " fn cap
          match Console.ReadLine() with
          | "y" -> return RespGrant Capability.ScopeOnce
          | "a" -> return RespGrant Capability.ScopeAlways
          | "N" -> return RespGrant Capability.ScopeNever
          | _ -> return RespReject "denied by user"
      // ... other kinds ...
      | _ -> return RespReject "unimplemented"
    }
```

## How LibPDD wires into ExecutionState

In `Cli/Cli.fs` or `Execution.createState`, you build a `PackageManager` with `materializeFn` from `LibPDD.Materializer.materializeFn` partially applied with the provider, the parser, the pinned-hashes table, and a TraceWriter.

```fsharp
let pm = ... // existing PackageManager
let pmPDD =
  { pm with
      materializeFn =
        LibPDD.Materializer.materializeFn
          provider
          pm
          parser
          pinned
          traceWriter }
let exeState =
  Execution.createState builtins pmPDD ...
  |> fun s ->
       { s with
           recoveryPolicy = LibPDD.Recovery.tolerant
           capabilityCheck = LibPDD.Capability.mkChecker granted denied
           humanResolver = LibPDD.Resolver.ttyResolver
           tracing = ... }
```

## Test surface

`backend/tests/Tests/PDD.Tests.fs`:

- `defaultFor TUnit = DUnit`
- `defaultFor TInt64 = DInt64 0L`
- `mkChecker {CapAny} _ _ = Granted`
- `mkChecker {} _ {CapReadNet} = Denied _`
- `tryFind` against a populated pinned table returns the pinned hash
- `race` with one path succeeding fast and the other slow returns the fast result
- `race` with both timing out returns `EmptyBody`
- `materializeFn` with a stub provider returns the stub's body

These are unit-level and don't require the interpreter end-to-end.

## What's NOT in LibPDD

- The `FQFnName.Pending` variant — that lives in `LibExecution.RuntimeTypes` (it's part of the runtime's type system).
- The interpreter's parked-frame scheduler — `LibExecution.Interpreter`.
- The `recoveryPolicy` / `capabilityCheck` / `humanResolver` fields on `ExecutionState` — `LibExecution.RuntimeTypes`.
- LLM bindings themselves — probably `LibAI` or wherever Feriel put them. LibPDD imports them as an interface.

LibPDD is the *policy + behavior* layer. LibExecution is the *mechanism* layer. Keep them apart.

## Build order

1. LibExecution changes (FQFnName.Pending, materializeFn field, etc.) — must compile first.
2. LibPDD/Defaults + Capability — pure, no PDD logic, compiles immediately.
3. LibPDD/TraceEvents — pure.
4. LibPDD/Find — depends on PackageManager.
5. LibPDD/Generate — depends on LLM provider (stub it first).
6. LibPDD/Resolver — depends on Capability (for scope types).
7. LibPDD/Materializer — depends on everything else.
8. CLI wiring — last.

## Estimate

If LibExecution side is done (Day 1 per `10-day-1-hacking-plan.md`), LibPDD's `Defaults` + `Capability` + `TraceEvents` are a half-day. `Find` is another half-day if the package store has the right indices, longer if not. `Generate` is a half-day to a day depending on the LLM provider state. `Materializer` is the trickiest — racing tasks + cancellation in F# is a footgun, plan a day.

Realistic plan: **LibPDD shippable v0 by end of Day 3** of focused hacking. Day 1 was LibExecution, Days 2-3 are LibPDD.
