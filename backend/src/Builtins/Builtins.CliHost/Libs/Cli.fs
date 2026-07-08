/// Builtin functions for building the CLI
/// (as opposed to functions needed by CLI programs, which are in StdLibCli)
module Builtins.CliHost.Libs.Cli

open System.Threading.Tasks
open FSharp.Control.Tasks


open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module NR = LibExecution.RuntimeTypes.NameResolution
module VT = LibExecution.ValueType
module AT = LibExecution.AnalysisTypes
module Dval = LibExecution.Dval
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module RT2DT = LibExecution.RuntimeTypesToDarkTypes
module PT2DT = LibExecution.ProgramTypesToDarkTypes
module Exe = LibExecution.Execution
module PackageRefs = LibExecution.PackageRefs
module Json = Builtins.Pure.Libs.Json
module C2DT = LibExecution.CommonToDarkTypes
module D = LibExecution.DvalDecoder
module Utils = Builtins.CliHost.Utils
module Toplevels = LibCloud.Toplevels
module Tracing = LibDB.Tracing
module P = LibParser.Parser
module WT = LibParser.WrittenTypes
module WT2PT = LibParser.WrittenTypesToProgramTypes
module NRslv = LibParser.NameResolver
module Hashing = LibSerialization.Hashing.Hashing


/// Load all DBs from the global toplevel set.
let loadDBs () : Ply<Map<string, RT.DB.T>> =
  uply {
    let! tls = Toplevels.loadAllDBs ()
    let! program = Toplevels.toProgram tls
    return program.dbs
  }


type CliTraceSource =
  | RunScript of filename : string * code : string
  | EvalExpression of expression : string

module CliTraceSource =
  let toTraceParams (source : CliTraceSource) =
    match source with
    | RunScript(filename, code) -> ($"run {filename}", "code", RT.DString code)
    | EvalExpression expr -> ("eval", "expression", RT.DString expr)


module ParseError =
  type ParseError = Message of string

  let fqTypeName () =
    FQTypeName.fqPackage (
      PackageRefs.Type.LanguageTools.Parser.CliScript.parseError ()
    )

  let toDT (err : ParseError) : Dval =
    let typeName = fqTypeName ()
    let (caseName, fields) =
      match err with
      | Message msg -> "Message", [ DString msg ]
    DEnum(typeName, typeName, [], caseName, fields)

  let fromDT (d : Dval) : ParseError =
    match d with
    | DEnum(_, _, _, "Message", [ DString msg ]) -> Message msg
    | _ -> Exception.raiseInternal "Invalid ParseError Dval" [ "dval", d ]

/// CLI/script declaration flattening. The CLI execution path qualifies by
/// owner/scriptName (not the module path), so a `module X = …` block's
/// declarations are flattened to the top level, each paired with its module path
/// (so `module Helpers = let go …` registers under …/Helpers/go, and same-named
/// fns in different modules can't collide at one location). `declarationsToModule`
/// consumes the raw `WT.Declaration`s directly. DB/test decls don't reach here.
let rec private flattenDecls
  (path : List<string>)
  (ds : List<WT.Declaration>)
  : List<List<string> * WT.Declaration> =
  ds
  |> List.collect (fun d ->
    match d with
    | WT.DModule m ->
      let parts =
        (snd m.name).Split('.') |> Array.toList |> List.filter (fun s -> s <> "")
      flattenDecls (path @ parts) m.declarations
    | WT.DFunction _
    | WT.DType _
    | WT.DValue _
    | WT.DExpr _ -> [ (path, d) ]
    | WT.DTypeDB _
    | WT.DTest _ -> [])

/// Flatten a parsed file: declarations (each with its module path), then the
/// trailing expressions as `DExpr`s.
let private flattenSourceFile
  (sf : WT.SourceFile)
  : List<List<string> * WT.Declaration> =
  flattenDecls [] sf.declarations
  @ (sf.exprsToEval |> List.map (fun e -> ([], WT.DExpr e)))


/// Lower a script's declarations to a PTCliScriptModule. Two passes: lower once
/// against the base package manager, graft the script's own declarations in,
/// then re-lower so intra-script references resolve. The two passes are inherent
/// (pass 1 can't resolve sibling refs — the decls aren't in the PM yet), not a
/// workaround.
///
let private declarationsToModule
  (state : RT.ExecutionState)
  (owner : string)
  (scriptName : string)
  (declarations : List<List<string> * WT.Declaration>)
  : Ply<Utils.CliScript.PTCliScriptModule> =
  uply {
    let builtins : RT.Builtins =
      { values = state.values.builtIn; fns = state.fns.builtIn }
    let baseModules = if scriptName = "" then [] else [ scriptName ]

    // Build WT package declarations + top-level expressions.
    let wtFns = ResizeArray<WT.PackageFn.PackageFn>()
    let wtTypes = ResizeArray<WT.PackageType.PackageType>()
    let wtValues = ResizeArray<WT.PackageValue.PackageValue>()
    // each trailing expr keeps its own module path so an expr inside `module M =`
    // still resolves M's declarations by short name
    let wtExprs = ResizeArray<List<string> * WT.Expr>()
    for (declPath, d) in declarations do
      let modules = baseModules @ declPath
      match d with
      | WT.DFunction fn ->
        let parameters =
          fn.parameters
          |> List.map (fun p ->
            let (name, typ) =
              match p with
              | WT.FPUnit _ -> ("_", WT.TUnit WT.synthRange)
              | WT.FPNormal(_, n, t, _, _, _) -> (n.name, t)
            ({ name = name; typ = typ; description = "" } : WT.PackageFn.Parameter))
        let parameters =
          match parameters with
          | h :: t -> NEList.ofList h t
          | [] ->
            NEList.singleton (
              { name = "_"; typ = WT.TUnit WT.synthRange; description = "" }
              : WT.PackageFn.Parameter
            )
        let fnName : WT.PackageFn.Name =
          { owner = owner; modules = modules; name = fn.name.name }
        wtFns.Add(
          { name = fnName
            body = fn.body
            typeParams = fn.typeParams |> List.map fst
            parameters = parameters
            returnType = fn.returnType
            description = fn.description }
          : WT.PackageFn.PackageFn
        )
      | WT.DType t ->
        let typeName : WT.PackageType.Name =
          { owner = owner; modules = modules; name = t.name.name }
        let decl : WT.TypeDeclaration.T =
          { typeParams = t.typeParams |> List.map fst
            definition = WT.typeDefinitionNorm t.definition }
        wtTypes.Add(
          { name = typeName; declaration = decl; description = t.description }
          : WT.PackageType.PackageType
        )
      | WT.DValue v ->
        let valueName : WT.PackageValue.Name =
          { owner = owner; modules = modules; name = v.name.name }
        wtValues.Add(
          { name = valueName; description = v.description; body = v.body }
          : WT.PackageValue.PackageValue
        )
      | WT.DExpr e -> wtExprs.Add(modules, e)
      // modules are flattened upstream; DB/test decls never reach the CLI path
      | WT.DModule _
      | WT.DTypeDB _
      | WT.DTest _ -> ()

    let onMissing = NRslv.OnMissing.Allow
    let fnList = List.ofSeq wtFns
    let typeList = List.ofSeq wtTypes
    let valueList = List.ofSeq wtValues

    let lowerFns pm =
      fnList
      |> Ply.List.mapSequentially (fun fn ->
        WT2PT.PackageFn.toPT
          builtins
          pm
          onMissing
          state.branchId
          (WT2PT.PackageFn.Name.toModules fn.name)
          fn)
    let lowerTypes pm =
      typeList
      |> Ply.List.mapSequentially (fun t ->
        WT2PT.PackageType.toPT
          pm
          onMissing
          state.branchId
          (WT2PT.PackageType.Name.toModules t.name)
          t)
    let lowerValues pm =
      valueList
      |> Ply.List.mapSequentially (fun v ->
        WT2PT.PackageValue.toPT
          builtins
          pm
          onMissing
          state.branchId
          (WT2PT.PackageValue.Name.toModules v.name)
          v)

    // WT2PT stamps every declaration with an empty `Hash ""` placeholder, and the
    // grafting below (`withExtras`) keys declarations by hash — so without real content
    // hashes ALL of a script's types (or values/fns) collapse to a single hash entry and
    // only the last-defined one survives (`type A={a} type B={b}` then `A{a=1}` resolves
    // to B → "no field named a"). These call the SHARED `Hashing.compute*Hash` (the same
    // primitive `LibDB/PackageOpPlayback` uses for `Hash ""` decls) — not a reimplementation.
    // (The SCC-aware `LibDB.HashStabilization` is a different strategy; unneeded here since
    // pass-1 bodies carry no resolved sibling refs to stabilize.)
    let hashType (t : PT.PackageType.PackageType) =
      { t with hash = Hashing.computeTypeHash Hashing.Normal t }
    let hashValue (v : PT.PackageValue.PackageValue) =
      { v with hash = Hashing.computeValueHash Hashing.Normal v }
    let hashFn (f : PT.PackageFn.PackageFn) =
      { f with hash = Hashing.computeFnHash Hashing.Normal f }

    // Pass 1: lower against the base pm (intra-script refs unresolved, allowed).
    // The resolver looks up packages on `state.branchId` (threaded through WT2PT),
    // so WIP on this branch resolves without wrapping the pm.
    let pm0 = LibDB.PackageManager.pt
    let! fns1 = lowerFns pm0
    let fns1 = fns1 |> List.map hashFn
    let! types1 = lowerTypes pm0
    let types1 = types1 |> List.map hashType
    let! values1 = lowerValues pm0
    let values1 = values1 |> List.map hashValue

    // Graft the script's own declarations into the pm, keyed by location.
    let fnLocs =
      List.zip
        fns1
        (fnList |> List.map (fun f -> WT2PT.PackageFn.Name.toLocation f.name))
    let typeLocs =
      List.zip
        types1
        (typeList |> List.map (fun t -> WT2PT.PackageType.Name.toLocation t.name))
    let valueLocs =
      List.zip
        values1
        (valueList |> List.map (fun v -> WT2PT.PackageValue.Name.toLocation v.name))
    let pm1 = pm0 |> PT.PackageManager.withExtras typeLocs valueLocs fnLocs

    // Pass 2: re-lower with the grafted pm so intra-script references resolve —
    // but KEEP each decl's pass-1 hash. Pass-2 bodies embed the pass-1 hashes pm1
    // hands out (that's all it has), so re-hashing the resolved bodies would make
    // every registered decl's hash disagree with the refs embedded in its callers
    // (any chain of depth ≥ 2 then fails with "function … couldn't be found").
    let! fns = lowerFns pm1
    let fns =
      List.map2
        (fun (f1 : PT.PackageFn.PackageFn) (f2 : PT.PackageFn.PackageFn) ->
          { f2 with hash = f1.hash })
        fns1
        fns
    let! types = lowerTypes pm1
    let types =
      List.map2
        (fun (t1 : PT.PackageType.PackageType) (t2 : PT.PackageType.PackageType) ->
          { t2 with hash = t1.hash })
        types1
        types
    let! values = lowerValues pm1
    let values =
      List.map2
        (fun (v1 : PT.PackageValue.PackageValue) (v2 : PT.PackageValue.PackageValue) ->
          { v2 with hash = v1.hash })
        values1
        values

    // Graft the pass-2 declarations (resolved bodies, pass-1 hashes) for the
    // expressions' lowering — their refs then match the returned decls exactly.
    let typeLocs2 =
      List.zip
        types
        (typeList |> List.map (fun t -> WT2PT.PackageType.Name.toLocation t.name))
    let valueLocs2 =
      List.zip
        values
        (valueList |> List.map (fun v -> WT2PT.PackageValue.Name.toLocation v.name))
    let fnLocs2 =
      List.zip
        fns
        (fnList |> List.map (fun f -> WT2PT.PackageFn.Name.toLocation f.name))
    let pm2 = pm0 |> PT.PackageManager.withExtras typeLocs2 valueLocs2 fnLocs2

    let emptyContext =
      { WT2PT.Context.currentFnName = None
        WT2PT.Context.isInFunction = false
        WT2PT.Context.argMap = Map.empty
        WT2PT.Context.localBindings = Set.empty }
    let! exprs =
      wtExprs
      |> List.ofSeq
      |> Ply.List.mapSequentially (fun (modules, e) ->
        WT2PT.Expr.toPT
          builtins
          pm2
          onMissing
          state.branchId
          (owner :: modules)
          emptyContext
          e)

    let emptyDefs : Utils.CliScript.Definitions =
      { types = []; values = []; fns = [] }
    return
      { Utils.CliScript.PTCliScriptModule.types = types
        values = values
        fns = fns
        submodules = emptyDefs
        exprs = exprs }
  }

/// Parse a whole CLI script with the hand-written parser, lowering `WT → PT`. One
/// parser feeds both the editor (WrittenTypes directly) and execution (via this
/// lowering).
let parseCliScript
  (state : RT.ExecutionState)
  (owner : string)
  (scriptName : string)
  (code : string)
  : Ply<Result<Utils.CliScript.PTCliScriptModule, List<P.Diagnostic>>> =
  uply {
    let result = P.parse code
    match result.parsed with
    | None -> return Error result.diagnostics
    | Some(WT.SourceFile sf) ->
      let declarations = flattenSourceFile sf
      match result.diagnostics with
      | (_ :: _) as ds -> return Error ds
      | [] ->
        let! m = declarationsToModule state owner scriptName declarations
        return Ok m
  }

/// Parse a single expression (the `eval` path) with the hand-written parser,
/// lowering `WT → PT`. Reuses the script lowering so `let x = … in x` and short
/// statement sequences also work; a bare expression is just `exprs`.
let parseCliExpr
  (state : RT.ExecutionState)
  (expression : string)
  : Ply<Result<Utils.CliScript.PTCliScriptModule, List<P.Diagnostic>>> =
  uply {
    let result = P.parse expression
    match result.parsed with
    | None -> return Error result.diagnostics
    | Some(WT.SourceFile sf) ->
      match result.diagnostics with
      | (_ :: _) as ds -> return Error ds
      | [] ->
        let! m = declarationsToModule state "" "" (flattenSourceFile sf)
        return Ok m
  }


module ExecutionError =
  let fqTypeName () = FQTypeName.fqPackage (PackageRefs.Type.Cli.executionError ())
  let typeRef () = TCustomType(NR.ok (fqTypeName ()), [])

  let unhandledTypeName () = FQTypeName.fqPackage (PackageRefs.Type.Cli.unhandled ())

  type Unhandled = { message : string; metadata : List<string * string> }

  type ExecutionError =
    | Parse of ParseError.ParseError
    | Runtime of RT.RuntimeError.Error
    | Unhandled of Unhandled

  /// Capture an exception's message and metadata for the `Unhandled` case.
  /// `Exception.toMetadata` only ever produces string-stringified values,
  /// so `List<string * string>` faithfully represents what we have without
  /// the Dval-wrapping machinery.
  let unhandledFromExn (e : exn) : Unhandled =
    { message = Exception.getMessages e |> String.concat "\n"
      metadata = Exception.toMetadata e |> List.map (fun (k, v) -> (k, string v)) }

  let private unhandledToDT (u : Unhandled) : Dval =
    let typeName = unhandledTypeName ()
    let pairKT = KTTuple(VT.string, VT.string, [])
    let metadataDval =
      u.metadata
      |> List.map (fun (k, v) -> DTuple(DString k, DString v, []))
      |> fun items -> DList(VT.known pairKT, items)
    let fields = [ "message", DString u.message; "metadata", metadataDval ]
    DRecord(typeName, typeName, [], Map fields)

  let toDT (err : ExecutionError) : Dval =
    let typeName = fqTypeName ()
    let (caseName, fields) =
      match err with
      | Parse pe -> "Parse", [ ParseError.toDT pe ]
      | Runtime rte -> "Runtime", [ RT2DT.RuntimeError.toDT rte ]
      | Unhandled u -> "Unhandled", [ unhandledToDT u ]
    DEnum(typeName, typeName, [], caseName, fields)


let pmRT = LibDB.PackageManager.rt

// The `cliEvaluateExpression` and `cliParseAndExecuteScript` builtins
// build child execution states (a different branch, a fresh PM with
// the script's own fns/types grafted in, a tracer, etc.). Rather than
// re-`createState` from scratch — which would force us to know the
// full builtin set here and would cycle into `fns ()` below — we
// derive the child state from `parentState`. The parent already
// includes our own builtins (it was constructed by Cli/Cli.fs) so
// nested `eval` / `run` dispatches automatically.

let childState
  (parentState : RT.ExecutionState)
  (pm : RT.PackageManager)
  (tracing : RT.Tracing.Tracing)
  (branchId : System.Guid)
  (program : Program)
  : RT.ExecutionState =
  { parentState with
      tracing = tracing
      branchId = branchId
      program = program
      types = { package = pm.getType }
      values = { parentState.values with package = pm.getValue }
      fns =
        { parentState.fns with
            package = pm.getFn
            isHarmful = fun pkg -> pm.isHarmful branchId pkg }
      blobs = { get = pm.getBlob; persist = pm.persistBlob } }


let execute
  (parentState : RT.ExecutionState)
  (branchId : System.Guid)
  (mod' : Utils.CliScript.PTCliScriptModule)
  (_args : List<Dval>) // CLEANUP update to List<String>, and extract in builtin
  (dbs : Map<string, RT.DB.T>)
  (traceSource : CliTraceSource)
  : Ply<RT.ExecutionResult> =
  uply {
    let (program : Program) = { dbs = dbs }

    let types =
      List.concat
        [ mod'.types |> List.map PT2RT.PackageType.toRT
          mod'.submodules.types |> List.map PT2RT.PackageType.toRT ]

    let values =
      List.concat
        [ mod'.values
          |> List.map (PT2RT.PackageValue.toRT parentState.values.builtIn)
          mod'.submodules.values
          |> List.map (PT2RT.PackageValue.toRT parentState.values.builtIn) ]

    let fns =
      List.concat
        [ mod'.fns |> List.map PT2RT.PackageFn.toRT
          mod'.submodules.fns |> List.map PT2RT.PackageFn.toRT ]

    // TODO we should probably use LibPM's in-memory grafting thing instead of this
    // (no need for RT.PM.withExtras to exist, I think)
    let pm = pmRT |> PackageManager.withExtras types values fns

    let (traceDesc, inputName, inputValue) = CliTraceSource.toTraceParams traceSource
    let traceID = AT.TraceID.create ()
    let tracer = Tracing.createCliTracer traceID traceDesc inputName inputValue

    let state = childState parentState pm tracer.executionTracing branchId program

    match mod'.exprs with
    | [] ->
      return
        RuntimeError.CLIs.NoExpressionsToExecute
        |> RuntimeError.CLI
        |> raiseUntargetedRTE
    | exprs ->
      let exprInsrts = exprs |> List.map (PT2RT.Expr.toRT Map.empty 0 None)
      let results = exprInsrts |> List.map (Exe.executeExpr state)
      match List.tryLast results with
      | Some lastResult ->
        let! result = lastResult
        do! tracer.storeTraceResults state
        return result
      | None ->
        return
          Exception.raiseInternal
            "No results from executing expressions (which should be impossible..)"
            []
  }

/// Create a branch-specific execution state for parsing.
///
/// `allowHarmful` is passed in rather than inherited from `parentState` so
/// callers can turn on the escape hatch per-invocation (e.g. when Dark-side
/// `run --allow-harmful` reaches `cliParseAndExecuteScript`).
let createBranchState
  (parentState : RT.ExecutionState)
  (branchId : System.Guid)
  (allowHarmful : bool)
  =
  let program : Program = { dbs = Map.empty }
  let state = childState parentState pmRT Exe.noTracing branchId program
  { state with allowHarmful = allowHarmful }


let fns () : List<BuiltInFn> =
  [ { name = fn "cliParseAndExecuteScript" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchId" TUuid ""
          Param.make "filename" TString ""
          Param.make "code" TString ""
          Param.make "args" (TList TString) ""
          Param.make
            "allowHarmful"
            TBool
            "Opt out of Harmful-deprecation halting (see docs/deprecation)"
          Param.make
            "sandbox"
            TBool
            "Run the script body with NO capabilities (a deny-all sandbox for untrusted scripts), instead of the host's configured grant" ]
      returnType = TypeReference.result TInt (ExecutionError.typeRef ())
      description =
        "Parses Dark code as a script, and and executes it, returning an exit code"
      fn =
        let errType = KTCustomType(ExecutionError.fqTypeName (), [])
        let resultOk = Dval.resultOk KTInt errType
        let resultError = Dval.resultError KTInt errType
        (function
        | exeState,
          _,
          [],
          [ accountIDDval
            DUuid branchId
            DString filename
            DString code
            DList(_vtTODO, scriptArgs)
            DBool allowHarmful
            DBool sandbox ] ->
          uply {
            // Attribute the run to the calling account so the trace
            // insert can stamp `traces.account_id`. None passes through
            // (anonymous runs, tests).
            let accountID = C2DT.Option.fromDT D.uuid accountIDDval
            let exeState = { exeState with accountID = accountID }
            // Use branch-specific state for parsing so name resolution uses the right branch.
            // Parsing keeps the host's caps — name resolution / package loading needs
            // cli-host effects to boot (the noCaps-breaks-bootstrap case). Only the script
            // *body* is sandboxed below (`runState`).
            let branchState = createBranchState exeState branchId allowHarmful

            try
              // A parse failure surfaces a precise diagnostic as a `ParseError`
              let! parseResult = parseCliScript branchState "CliScript" filename code
              let! parsedScript =
                match parseResult with
                | Ok m -> Ply(Ok m)
                | Error diags ->
                  let pe =
                    match diags with
                    | d :: _ -> ParseError.Message(P.renderDiagnostic code d)
                    | [] -> ParseError.Message "Parse error"
                  Ply(Error pe)

              let! dbs = loadDBs ()

              match parsedScript with
              | Ok mod' ->
                // `dark run` RESPECTS the host's configured grant by default (`hostCaps`: allCaps until
                // an instance grant is configured, then that grant) — the same posture as `eval`, so the
                // grant you set is the grant scripts obey. `--sandbox` drops to NO capabilities for
                // running untrusted scripts (any effectful builtin then raises).
                // TODO product decision, revisit: this favors "run my own script" over "run an untrusted
                // script" (sandbox is opt-IN). If `dark run <url>` / piping untrusted code becomes common,
                // a deny-all default + `--trust`/`--apply-host-caps` opt-in may be safer. See also the
                // trust-boundary TODO in `LanguageTools.Capabilities.all`.
                let runCaps =
                  if sandbox then
                    LibExecution.Capabilities.noCaps
                  else
                    LibDB.CapabilityGrants.hostCaps ()
                let exeState = { exeState with grantedCaps = runCaps }
                match!
                  execute
                    exeState
                    branchId
                    mod'
                    scriptArgs
                    dbs
                    (RunScript(filename, code))
                with
                | Ok(DInt i) -> return resultOk (DInt i)
                | Ok(DInt64 i) -> return resultOk (Dval.int (bigint i))
                | Ok DUnit -> return resultOk (Dval.int (bigint 0))
                | Ok result ->
                  let rte =
                    RuntimeError.CLIs.NonIntReturned result |> RuntimeError.CLI
                  return
                    resultError (ExecutionError.toDT (ExecutionError.Runtime rte))
                | Error(e, callStack) ->
                  let! csString = Exe.callStackString exeState callStack
                  print $"Error when executing Script. Call-stack:\n{csString}\n"
                  return resultError (ExecutionError.toDT (ExecutionError.Runtime e))
              | Error pe ->
                return resultError (ExecutionError.toDT (ExecutionError.Parse pe))
            // Runtime errors raised via `raiseUntargetedRTE` (e.g.
            // `NoExpressionsToExecute`) escape as `RuntimeErrorException`
            // rather than returning through the normal `Error(rte, _)`
            // channel. Catch them explicitly so they're classified as
            // `Runtime`, not `Unhandled`.
            with
            | RuntimeErrorException(_, rte) ->
              return resultError (ExecutionError.toDT (ExecutionError.Runtime rte))
            | e ->
              return
                resultError (
                  ExecutionError.toDT (
                    ExecutionError.Unhandled(ExecutionError.unhandledFromExn e)
                  )
                )
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    { name = fn "cliEvaluateExpression" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchId" TUuid ""
          Param.make "expression" TString ""
          Param.make
            "allowHarmful"
            TBool
            "Opt out of Harmful-deprecation halting (see docs/deprecation)" ]
      returnType =
        TypeReference.result
          (TypeReference.option TString)
          (ExecutionError.typeRef ())
      description =
        "Evaluates a Dark expression. Returns Some(reprString) for a value, "
        + "or None when the result is Unit (so callers can suppress empty echo)."
      fn =
        let errType = KTCustomType(ExecutionError.fqTypeName (), [])
        let okKT = KTCustomType(Dval.optionType (), [ VT.known KTString ])
        let resultOk = Dval.resultOk okKT errType
        let resultError = Dval.resultError okKT errType
        let okSome (s : string) = resultOk (Dval.optionSome KTString (DString s))
        let okNone () = resultOk (Dval.optionNone KTString)
        (function
        | exeState,
          _,
          [],
          [ accountIDDval; DUuid branchId; DString expression; DBool allowHarmful ] ->
          uply {
            // Attribute the run to the calling account so the trace
            // insert can stamp `traces.account_id`.
            let accountID = C2DT.Option.fromDT D.uuid accountIDDval
            // `eval` runs the expression under the HOST's capabilities — `allCaps` until an instance
            // grant is configured, then whatever that grant allows (the gate denies uncovered builtins).
            let exeState =
              { exeState with
                  accountID = accountID
                  grantedCaps = LibDB.CapabilityGrants.hostCaps () }
            // Use branch-specific state for parsing so name resolution uses the right branch
            let branchState = createBranchState exeState branchId allowHarmful

            try
              // Parsing can raise (e.g. deep VM failures); keep it inside the try
              // so its exceptions hit the Unhandled net. `eval` is single-expression
              // only; parse failures surface a precise diagnostic (no fallback).
              let! parseResult = parseCliExpr branchState expression
              let! parsedScript =
                match parseResult with
                | Ok m -> Ply(Ok m)
                | Error diags ->
                  let pe =
                    match diags with
                    | d :: _ -> ParseError.Message(P.renderDiagnostic expression d)
                    | [] -> ParseError.Message "Parse error"
                  Ply(Error pe)

              let! dbs = loadDBs ()

              match parsedScript with
              | Ok mod' ->
                match!
                  execute exeState branchId mod' [] dbs (EvalExpression expression)
                with
                | Ok result ->
                  match result with
                  | DUnit -> return okNone ()
                  | DString s -> return okSome s
                  | _ ->
                    let! asString = Exe.dvalToRepr exeState result
                    return okSome asString
                | Error(e, callStack) ->
                  let! csString = Exe.callStackString exeState callStack
                  print $"Error when executing expression. Call-stack:\n{csString}\n"
                  return resultError (ExecutionError.toDT (ExecutionError.Runtime e))
              | Error pe ->
                return resultError (ExecutionError.toDT (ExecutionError.Parse pe))
            with
            | RuntimeErrorException(_, rte) ->
              return resultError (ExecutionError.toDT (ExecutionError.Runtime rte))
            | e ->
              return
                resultError (
                  ExecutionError.toDT (
                    ExecutionError.Unhandled(ExecutionError.unhandledFromExn e)
                  )
                )
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    ]


/// All builtins the outer CLI execution state needs: this module's own
/// fns (so nested `eval`/`run` dispatches recursively) plus every
/// `Builtins.*` library the CLI surface depends on.
///
/// `defaultConfig` has SSRF guards on (loopback / RFC1918 / metadata
/// blocked, scheme restricted). For local-dev cases that need to hit
/// private targets, swap in `Builtins.Http.Client.Libs.HttpClient.looseConfig`.
let builtinsToUse () : RT.Builtins =
  let ptPM = LibDB.PackageManager.pt
  LibExecution.Builtin.combine
    [ Builtins.Pure.Builtin.builtins ()
      Builtins.Http.Client.Builtin.builtins
        Builtins.Http.Client.Libs.HttpClient.defaultConfig
      Builtins.Language.Builtin.builtins ()
      Builtins.Cli.Builtin.builtins ()
      Builtins.Time.Builtin.builtins ()
      Builtins.Random.Builtin.builtins ()
      Builtins.Matter.Builtin.builtins ptPM
      Builtins.Http.Server.Builtin.builtins ()
      LibExecution.Builtin.make [] (fns ()) ]
    []


let builtins () = LibExecution.Builtin.make [] (fns ())
