module Builtins.Compiler.Libs.Compiler

// The compiler extension's builtin surface. These call the airlifted
// CompilerLibrary in-process (no shelling out to a `dark` binary). This is the
// interim, text-fed path (plan §5): callers hand over compiler-syntax source;
// the real PT->compiler-AST bridge (plan §6) lands next and replaces the text
// hop for entities pulled from the package tree.

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module VT = LibExecution.ValueType
module PT = LibExecution.ProgramTypes
module Bridge = Builtins.Compiler.Bridge

/// buildStdlib parses + compiles the ~30 bundled .dark stdlib files; it's heavy,
/// so build it once and reuse. (Retires with the bundled stdlib once the bridge
/// consumes the main-repo tree.)
let private stdlibResult : Lazy<Result<CompilerLibrary.StdlibResult, string>> =
  lazy (CompilerLibrary.buildStdlib ())

/// Compile compiler-syntax source to a native binary, in-process.
let private compileSource (source : string) : Result<byte array, string> =
  match stdlibResult.Value with
  | Error e -> Error $"buildStdlib: {e}"
  | Ok stdlib ->
    let request : CompilerLibrary.CompileRequest =
      { Context = CompilerLibrary.StdlibOnly stdlib
        Mode = CompilerLibrary.CompileMode.FullProgram
        SourceSyntax = CompilerLibrary.InterpreterSyntax
        Source = source
        SourceFile = ""
        AllowInternal = false
        Verbosity = 0
        Options = CompilerLibrary.defaultOptions
        PassTimingRecorder = None }
    (CompilerLibrary.compile request).Result

/// Compile a pre-built compiler AST program (the PT->AST bridge path, §6),
/// skipping the text parser entirely.
let private compileAst (program : AST.Program) : Result<byte array, string> =
  match stdlibResult.Value with
  | Error e -> Error $"buildStdlib: {e}"
  | Ok stdlib ->
    let request : CompilerLibrary.CompileRequest =
      { Context = CompilerLibrary.StdlibOnly stdlib
        Mode = CompilerLibrary.CompileMode.FullProgram
        SourceSyntax = CompilerLibrary.InterpreterSyntax
        Source = ""
        SourceFile = ""
        AllowInternal = false
        Verbosity = 0
        Options = CompilerLibrary.defaultOptions
        PassTimingRecorder = None }
    (CompilerLibrary.compileAstProgram request program).Result

let private allOk (rs : List<Result<'a, string>>) : Result<List<'a>, string> =
  (Ok [], rs)
  ||> List.fold (fun acc r ->
    match acc, r with
    | Error e, _ -> Error e
    | Ok _, Error e -> Error e
    | Ok xs, Ok x -> Ok(xs @ [ x ]))

/// Fetch a package fn's transitive call graph (PT fns only — no bridging yet,
/// since bridging needs the type env which we build afterwards).
let rec private fetchFnClosure
  (visited : Set<string>)
  (acc : List<string * PT.PackageFn.PackageFn>)
  (worklist : List<string>)
  : Ply<Result<List<string * PT.PackageFn.PackageFn>, string>> =
  uply {
    match worklist with
    | [] -> return Ok acc
    | h :: rest ->
      if Set.contains h visited then
        return! fetchFnClosure visited acc rest
      else
        let! fnOpt = LibDB.PackageManager.pt.getFn (PT.FQFnName.package h)
        match fnOpt with
        | None -> return Error $"missing dependency fn {h}"
        | Some ptFn ->
          let deps = Bridge.referencedPackageFns ptFn.body
          return! fetchFnClosure (Set.add h visited) (acc @ [ (h, ptFn) ]) (rest @ deps)
  }

/// Fetch the transitive closure of custom types referenced by the seeds.
let rec private fetchTypeClosure
  (visited : Set<string>)
  (acc : List<string * PT.PackageType.PackageType>)
  (worklist : List<string>)
  : Ply<Result<List<string * PT.PackageType.PackageType>, string>> =
  uply {
    match worklist with
    | [] -> return Ok acc
    | h :: rest ->
      if Set.contains h visited then
        return! fetchTypeClosure visited acc rest
      else
        let! tOpt = LibDB.PackageManager.pt.getType (PT.FQTypeName.package h)
        match tOpt with
        // A hash that doesn't resolve to a type (e.g. an over-collected fn hash,
        // or a genuinely absent type) is skipped rather than failing the whole
        // fn. If it was a real type the fn needs, bridgeType hard-fails on it
        // cleanly (TCustomType not in env); if it was noise, no harm.
        | None -> return! fetchTypeClosure (Set.add h visited) acc rest
        | Some pt ->
          let deps = Bridge.typeRefsInTypeDef pt
          return! fetchTypeClosure (Set.add h visited) (acc @ [ (h, pt) ]) (rest @ deps)
  }

/// Fetch the transitive closure of package constants referenced by the seeds.
let rec private fetchValueClosure
  (visited : Set<string>)
  (acc : List<string * PT.PackageValue.PackageValue>)
  (worklist : List<string>)
  : Ply<Result<List<string * PT.PackageValue.PackageValue>, string>> =
  uply {
    match worklist with
    | [] -> return Ok acc
    | h :: rest ->
      if Set.contains h visited then
        return! fetchValueClosure visited acc rest
      else
        let! vOpt = LibDB.PackageManager.pt.getValue (PT.FQValueName.package h)
        match vOpt with
        | None -> return! fetchValueClosure (Set.add h visited) acc rest
        | Some pv ->
          let deps = Bridge.valueRefsInExpr pv.body
          return! fetchValueClosure (Set.add h visited) (acc @ [ (h, pv) ]) (rest @ deps)
  }

/// Fetch + bridge the whole graph (fns and their non-generic custom types),
/// returning the compiler TypeDefs, FunctionDefs, and the root's compiled name.
/// Effectful builtins routable through the host RPC seam, from the live runtime:
/// name -> returns-String (true) / returns-Unit (false); included only when all
/// params are String (the wire-marshalable subset in v1).
let private buildEffectfulMap (exeState : ExecutionState) : Map<string, bool> =
  exeState.fns.builtIn
  |> Map.toList
  |> List.choose (fun (name, bfn) ->
    let allStringArgs = bfn.parameters |> List.forall (fun p -> p.typ = TString)
    if not allStringArgs then
      None
    else
      match bfn.returnType with
      | TUnit -> Some(name.name, false)
      | TString -> Some(name.name, true)
      | _ -> None)
  |> Map.ofList

let private buildPieces
  (effectful : Map<string, bool>)
  (rootHash : string)
  : Ply<Result<List<AST.TypeDef> * List<AST.FunctionDef> * string, string>> =
  uply {
    let! fnClosure = fetchFnClosure Set.empty [] [ rootHash ]
    match fnClosure with
    | Error e -> return Error e
    | Ok fns ->
      let valueSeeds =
        fns |> List.collect (fun (_, fn) -> Bridge.valueRefsInExpr fn.body) |> List.distinct
      let! valueClosure = fetchValueClosure Set.empty [] valueSeeds
      let values =
        match valueClosure with
        | Ok vs -> vs
        | Error _ -> []
      let typeSeeds =
        ((fns |> List.collect (fun (_, fn) -> Bridge.typeRefsInFn fn))
         @ (values |> List.collect (fun (_, pv) -> Bridge.typeRefsInExpr pv.body)))
        |> List.distinct
      let! typeClosure = fetchTypeClosure Set.empty [] typeSeeds
      match typeClosure with
      | Error e -> return Error e
      | Ok types ->
        // The type env: non-generic record (false) / enum (true) types only.
        // Generic + alias types are omitted, so any fn using one hard-fails.
        // Records/enums emit a TypeDef and lower to a named TRecord/TSum.
        let baseEnv =
          types
          |> List.choose (fun (h, pt) ->
            match pt.declaration.definition with
            | PT.TypeDeclaration.Record _ -> Some(h, Bridge.TERecord)
            | PT.TypeDeclaration.Enum _ -> Some(h, Bridge.TESum)
            | PT.TypeDeclaration.Alias _ -> None)
          |> Map.ofList
        // Non-generic aliases inline to their (bridged) target. Resolved against
        // baseEnv, so an alias of a record/enum works; alias-of-alias is skipped
        // (its users then hard-fail cleanly on TCustomType).
        let typeEnv =
          (baseEnv, types)
          ||> List.fold (fun env (h, pt) ->
            match pt.declaration.definition with
            | PT.TypeDeclaration.Alias t when List.isEmpty pt.declaration.typeParams ->
              match Bridge.bridgeType baseEnv t with
              | Ok bt -> Map.add h (Bridge.TEAlias bt) env
              | Error _ -> env
            | _ -> env)
        let typeDefs =
          types
          |> List.filter (fun (h, _) -> Map.containsKey h baseEnv)
          |> List.map (fun (h, pt) -> Bridge.bridgeTypeDef typeEnv (Bridge.nameForType h) pt)
          |> allOk
        // Bridge each package constant's body (no params/self) into an inline
        // expression, accumulating so a value can reference an earlier one.
        let valuesMap =
          (Map.empty, values)
          ||> List.fold (fun m (h, pv) ->
            let ctx : Bridge.BridgeCtx = { Params = [||]; Self = ""; Values = m; Effectful = Map.empty }
            match Bridge.bridgeExpr ctx pv.body with
            | Ok e -> Map.add h e m
            | Error _ -> m)
        let fnDefs =
          fns
          |> List.map (fun (h, fn) -> Bridge.bridgeFn typeEnv valuesMap effectful (Bridge.nameFor h) fn)
          |> allOk
        match typeDefs, fnDefs with
        | Error e, _ -> return Error e
        | _, Error e -> return Error e
        | Ok tds, Ok fds -> return Ok(tds, fds, Bridge.nameFor rootHash)
  }

/// Substitute type variables (used to instantiate a generic root fn's type
/// params to a concrete type so it has a monomorphic entry point).
let rec private substTVars (m : Map<string, AST.Type>) (t : AST.Type) : AST.Type =
  let s = substTVars m
  match t with
  | AST.TVar v -> Map.tryFind v m |> Option.defaultValue t
  | AST.TRecord(n, args) -> AST.TRecord(n, List.map s args)
  | AST.TSum(n, args) -> AST.TSum(n, List.map s args)
  | AST.TList inner -> AST.TList(s inner)
  | AST.TTuple ts -> AST.TTuple(List.map s ts)
  | AST.TDict(k, v) -> AST.TDict(s k, s v)
  | AST.TFunction(args, ret) -> AST.TFunction(List.map s args, s ret)
  | other -> other

/// The entry call to the root fn. A generic root is instantiated at Int64 (so
/// it monomorphizes to a concrete entry point) via TypeApp.
let private mainCall (rootFd : AST.FunctionDef) (args : List<AST.Expr>) : AST.Expr =
  let nParams = List.length rootFd.TypeParams
  if nParams = 0 then
    AST.Call(rootFd.Name, AST.NonEmptyList.fromList args)
  else
    AST.TypeApp(
      rootFd.Name,
      List.replicate nParams AST.TInt64,
      AST.NonEmptyList.fromList args)

/// Concrete param types of the root, with its generic params instantiated at
/// Int64 (matching mainCall) — so zero args can be synthesized.
let private rootParamTypes (rootFd : AST.FunctionDef) : List<AST.Type> =
  let subst = rootFd.TypeParams |> List.map (fun tp -> (tp, AST.TInt64)) |> Map.ofList
  rootFd.Params |> AST.NonEmptyList.toList |> List.map (snd >> substTVars subst)

/// Build the program (type defs + all bridged fns + the entry call) and compile.
let private compileClosure
  (typeDefs : List<AST.TypeDef>)
  (fds : List<AST.FunctionDef>)
  (mainExpr : AST.Expr)
  : Result<byte array, string> =
  let program : AST.Program =
    AST.Program(
      (typeDefs |> List.map AST.TypeDef)
      @ (fds |> List.map AST.FunctionDef)
      @ [ AST.Expression mainExpr ])
  compileAst program

/// A zero/default literal for a compiler type, so a function can be made
/// reachable (called) to check that it actually compiles, without real args.
let rec private zeroLit (t : AST.Type) : Result<AST.Expr, string> =
  match t with
  | AST.TInt64 -> Ok(AST.Int64Literal 0L)
  | AST.TInt8 -> Ok(AST.Int8Literal 0y)
  | AST.TInt16 -> Ok(AST.Int16Literal 0s)
  | AST.TInt32 -> Ok(AST.Int32Literal 0l)
  | AST.TInt128 -> Ok(AST.Int128Literal System.Int128.Zero)
  | AST.TUInt8 -> Ok(AST.UInt8Literal 0uy)
  | AST.TUInt16 -> Ok(AST.UInt16Literal 0us)
  | AST.TUInt32 -> Ok(AST.UInt32Literal 0ul)
  | AST.TUInt64 -> Ok(AST.UInt64Literal 0UL)
  | AST.TUInt128 -> Ok(AST.UInt128Literal System.UInt128.Zero)
  | AST.TBool -> Ok(AST.BoolLiteral false)
  | AST.TString -> Ok(AST.StringLiteral "")
  | AST.TFloat64 -> Ok(AST.FloatLiteral 0.0)
  | AST.TChar -> Ok(AST.CharLiteral "a")
  | AST.TUnit -> Ok AST.UnitLiteral
  | other -> Error $"no zero literal for {other}"

/// A zero value for any bridged type — including records/enums, built from the
/// bridged type defs — so compileFnCheck can make a fn taking custom-type params
/// reachable. `seen` guards against infinite recursion on recursive types.
let rec private zeroValue
  (defs : Map<string, AST.TypeDef>)
  (seen : Set<string>)
  (t : AST.Type)
  : Result<AST.Expr, string> =
  match t with
  | AST.TRecord(name, _) ->
    if Set.contains name seen then Error $"recursive type {name}"
    else
      match Map.tryFind name defs with
      | Some(AST.RecordDef(_, _, fields)) ->
        fields
        |> List.map (fun (fname, ft) ->
          zeroValue defs (Set.add name seen) ft |> Result.map (fun v -> (fname, v)))
        |> allOk
        |> Result.map (fun fs -> AST.RecordLiteral(name, fs))
      | _ -> Error $"no record def {name}"
  | AST.TSum(name, _) ->
    if Set.contains name seen then Error $"recursive type {name}"
    else
      match Map.tryFind name defs with
      | Some(AST.SumTypeDef(_, _, variant :: _)) ->
        match variant.Payload with
        | None -> Ok(AST.Constructor(name, variant.Name, None))
        | Some pt ->
          zeroValue defs (Set.add name seen) pt
          |> Result.map (fun pv -> AST.Constructor(name, variant.Name, Some pv))
      | _ -> Error $"no sum def {name}"
  | AST.TTuple ts ->
    ts |> List.map (zeroValue defs seen) |> allOk |> Result.map AST.TupleLiteral
  | AST.TList _ -> Ok(AST.ListLiteral [])
  | prim -> zeroLit prim

/// Compile-check one package fn by hash (bridge its call-graph, make it
/// reachable with zero-valued args, compile — don't run). Returns (ok, detail).
/// Wrapped so an unexpected crash in one fn can't abort a batch sweep.
let private checkOne (effectful : Map<string, bool>) (hash : string) : Ply<bool * string> =
  uply {
    try
      let! pieces = buildPieces effectful hash
      match pieces with
      | Error e -> return (false, e)
      | Ok(typeDefs, fds, rootName) ->
        match fds |> List.tryFind (fun fd -> fd.Name = rootName) with
        | None -> return (false, "root fn not bridged")
        | Some rootFd ->
          let defsByName =
            typeDefs
            |> List.choose (fun td ->
              match td with
              | AST.RecordDef(n, _, _) -> Some(n, td)
              | AST.SumTypeDef(n, _, _) -> Some(n, td)
              | AST.TypeAlias(n, _, _) -> Some(n, td))
            |> Map.ofList
          let dummy =
            rootParamTypes rootFd |> List.map (zeroValue defsByName Set.empty)
          match List.tryPick (function Error e -> Some e | Ok _ -> None) dummy with
          | Some e -> return (false, $"arg-synthesis: {e}")
          | None ->
            let args = dummy |> List.choose (function Ok x -> Some x | Error _ -> None)
            match compileClosure typeDefs fds (mainCall rootFd args) with
            | Ok binary -> return (true, $"{binary.Length} bytes")
            | Error e -> return (false, e)
    with e ->
      return (false, $"exn: {e.Message}")
  }

/// Return a (Bool ok, String detail) tuple as a Dark value.
let private outcome (ok : bool) (detail : string) : Dval =
  DTuple(DBool ok, DString detail, [])

// ---------------------------------------------------------------------------
// Runtime seam: an in-process F# daemon that services builtin-call requests
// from compiled native code and dispatches them to the REAL builtins in the
// live ExecutionState. Channel = the FIFO/file prototype (Stdlib.hostRpc).
// v1 marshals scalars only; container types are TODO. (BUILTINS-ARCHITECTURE.md)
// ---------------------------------------------------------------------------

/// Scalar Dval -> wire string.
let private dvalToWire (d : Dval) : string =
  match d with
  | DString s -> s
  | DInt n -> string (DarkInt.toBigInt n)
  | DInt8 n -> string n
  | DInt16 n -> string n
  | DInt32 n -> string n
  | DInt64 n -> string n
  | DInt128 n -> string n
  | DUInt8 n -> string n
  | DUInt16 n -> string n
  | DUInt32 n -> string n
  | DUInt64 n -> string n
  | DUInt128 n -> string n
  | DBool b -> if b then "true" else "false"
  | DUnit -> "unit"
  | DFloat f -> string f
  | DChar c -> c
  | other -> $"?unmarshalable:{other.GetType().Name}"

/// wire string -> scalar Dval, per the builtin param's declared type.
let private wireToDval (t : TypeReference) (s : string) : Dval =
  match t with
  | TString -> DString s
  | TInt64 -> DInt64(int64 s)
  | TBool -> DBool(s = "true")
  | TUnit -> DUnit
  | TFloat -> DFloat(float s)
  | TChar -> DChar s
  | _ -> DString s

/// Dispatch one request ("name\narg1\narg2…") to the real builtin, return the
/// wire response.
let private dispatchBuiltin
  (exeState : ExecutionState)
  (vm : VMState)
  (request : string)
  : Ply<string> =
  uply {
    let parts = request.Split('\n')
    let name = parts[0]
    let wireArgs = parts[1..] |> Array.toList
    match Map.tryFind (FQFnName.builtin name 0) exeState.fns.builtIn with
    | None -> return $"ERR:no-builtin:{name}"
    | Some bfn ->
      let n = min bfn.parameters.Length wireArgs.Length
      let dvalArgs =
        List.map2
          (fun (p : BuiltInParam) (a : string) -> wireToDval p.typ a)
          (List.truncate n bfn.parameters)
          (List.truncate n wireArgs)
      let! result = bfn.fn (exeState, vm, [], dvalArgs)
      return dvalToWire result
  }

let private rpcReqFifo = "/tmp/dark-rpc-req"
let private rpcRespFile = "/tmp/dark-rpc-resp"
let private rpcShutdown = "__DARK_RPC_SHUTDOWN__"

/// Set up the channel and service ONE builtin request in a background task
/// (used by the daemon self-test).
let private serveOneRequest (exeState : ExecutionState) (vm : VMState) : System.Threading.Tasks.Task =
  for f in [ rpcRespFile; rpcReqFifo ] do
    if System.IO.File.Exists f then System.IO.File.Delete f
  let psi = System.Diagnostics.ProcessStartInfo("mkfifo", rpcReqFifo)
  psi.UseShellExecute <- false
  (System.Diagnostics.Process.Start psi).WaitForExit()
  System.Threading.Tasks.Task.Run(fun () ->
    let req = System.IO.File.ReadAllText rpcReqFifo
    let resp = (dispatchBuiltin exeState vm req |> Ply.toTask).Result
    System.IO.File.WriteAllText(rpcRespFile + ".tmp", resp)
    System.IO.File.Move(rpcRespFile + ".tmp", rpcRespFile, true))

/// Start a looping daemon that services builtin requests until shutdown. Each
/// request: read from the FIFO (blocks), dispatch, write the response file (the
/// compiled hostRpc deletes it after reading, so the next poll waits for a fresh
/// one). Returns the task + a shutdown fn (unblocks the FIFO read with a sentinel).
let private startDaemon
  (exeState : ExecutionState)
  (vm : VMState)
  : System.Threading.Tasks.Task * (unit -> unit) =
  for f in [ rpcRespFile; rpcReqFifo ] do
    if System.IO.File.Exists f then System.IO.File.Delete f
  let psi = System.Diagnostics.ProcessStartInfo("mkfifo", rpcReqFifo)
  psi.UseShellExecute <- false
  (System.Diagnostics.Process.Start psi).WaitForExit()
  let task =
    System.Threading.Tasks.Task.Run(fun () ->
      let mutable running = true
      while running do
        let req = System.IO.File.ReadAllText rpcReqFifo // blocks until a writer
        if req = rpcShutdown || req = "" then
          running <- false
        else
          let resp = (dispatchBuiltin exeState vm req |> Ply.toTask).Result
          System.IO.File.WriteAllText(rpcRespFile + ".tmp", resp)
          System.IO.File.Move(rpcRespFile + ".tmp", rpcRespFile, true))
  let shutdown () =
    try
      System.IO.File.WriteAllText(rpcReqFifo, rpcShutdown)
    with _ -> ()
  (task, shutdown)

let fns () : List<BuiltInFn> =
  [ { name = fn "compilerInfo" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TString
      description =
        "Reports that the native compiler extension is linked and available."
      fn =
        (function
        | _, _, _, [ DUnit ] ->
          let opts = CompilerLibrary.defaultOptions
          DString
            $"native compiler linked (DisableFreeList={opts.DisableFreeList}, DisableTCO={opts.DisableTCO})"
          |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compilerCompile" 0
      typeParams = []
      parameters = [ Param.make "source" TString "compiler-syntax (interpreter-dialect) Dark source" ]
      returnType = TTuple(TBool, TString, [])
      description =
        "Compiles <source> to a native binary in-process via the airlifted compiler. Returns (true, \"<n> bytes\") on success or (false, <error>) on failure. Does not execute the binary."
      fn =
        (function
        | _, _, _, [ DString source ] ->
          match compileSource source with
          | Ok binary -> outcome true $"{binary.Length} bytes" |> Ply
          | Error e -> outcome false e |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compilerCompileAndRun" 0
      typeParams = []
      parameters = [ Param.make "source" TString "compiler-syntax (interpreter-dialect) Dark source" ]
      returnType = TTuple(TBool, TString, [])
      description =
        "Compiles <source> to a native binary and executes it in-process. Returns (true, <stdout>) on success or (false, <error>) on failure. NOTE: runs native code; x86-64 refcounting is disabled, so long/allocation-heavy programs may leak (fine for short pure-core leaves)."
      fn =
        (function
        | _, _, _, [ DString source ] ->
          match compileSource source with
          | Error e -> outcome false $"compile: {e}" |> Ply
          | Ok binary ->
            let out = CompilerLibrary.execute 0 binary
            if out.ExitCode = 0 then outcome true out.Stdout |> Ply
            else outcome false $"exit {out.ExitCode}: {out.Stderr}" |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compilerDaemonSelfTest" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TString
      description =
        "Proves the runtime seam dispatches to a REAL builtin: compiles a program calling Stdlib.hostRpc(\"stringLength\\nhello\"); an in-process F# daemon reads the request, invokes the real stringLength builtin (DString hello -> DInt64 5), writes the response; the native binary reads it back. Returns the binary's stdout (expect 5)."
      fn =
        (function
        | exeState, vm, _, [ DUnit ] ->
          uply {
            let daemon = serveOneRequest exeState vm
            let program : AST.Program =
              AST.Program
                [ AST.Expression(
                    AST.Call(
                      "Stdlib.hostRpc",
                      AST.NonEmptyList.singleton (AST.StringLiteral "stringLength\nhello"))) ]
            match compileAst program with
            | Error e ->
              daemon.Wait()
              return DString $"compile-error: {e}"
            | Ok binary ->
              let out = CompilerLibrary.execute 0 binary
              daemon.Wait()
              return DString $"binary stdout={out.Stdout.Trim()} (expected 5)"
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compilerBridgeSelfTest" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TTuple(TBool, TString, [])
      description =
        "Proves the ProgramTypes->compiler-AST bridge (§6) end to end: builds a PT function `(a,b) => a + b` in F#, lowers it via Bridge.bridgeFn to compiler AST, synthesizes `bridgedFn(20, 22)`, compiles that AST directly (no text/parser), runs it, and returns (true, \"42\") on success. This is the durable path the text builtins will be replaced by."
      fn =
        (function
        | _, _, _, [ DUnit ] ->
          // A pure-Int leaf, constructed as ProgramTypes: (a: Int64) (b: Int64): Int64 = a + b
          let ptFn : PT.PackageFn.PackageFn =
            { hash = PT.FQFnName.package "compiler-merge-selftest-add"
              body =
                PT.EInfix(
                  gid (),
                  PT.InfixFnCall PT.ArithmeticPlus,
                  PT.EArg(gid (), 0),
                  PT.EArg(gid (), 1))
              typeParams = []
              parameters =
                NEList.ofList
                  ({ name = "a"; typ = PT.TInt64; description = "" } : PT.PackageFn.Parameter)
                  [ { name = "b"; typ = PT.TInt64; description = "" } ]
              returnType = PT.TInt64
              description = "" }
          match Bridge.bridgeFn Map.empty Map.empty Map.empty "bridgedFn" ptFn with
          | Error e -> outcome false $"bridge: {e}" |> Ply
          | Ok fd ->
            let program : AST.Program =
              AST.Program
                [ AST.FunctionDef fd
                  AST.Expression(
                    AST.Call(
                      "bridgedFn",
                      AST.NonEmptyList.fromList [ AST.Int64Literal 20L; AST.Int64Literal 22L ])) ]
            match compileAst program with
            | Error e -> outcome false $"compile: {e}" |> Ply
            | Ok binary ->
              let out = CompilerLibrary.execute 0 binary
              if out.ExitCode = 0 then outcome true out.Stdout |> Ply
              else outcome false $"exit {out.ExitCode}: {out.Stderr}" |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compileFnByHash" 0
      typeParams = []
      parameters =
        [ Param.make "hash" TString "content hash of a package function"
          Param.make "args" (TList TInt64) "Int64 arguments to call the function with" ]
      returnType = TTuple(TBool, TString, [])
      description =
        "Fetches a package function's ProgramTypes by hash, lowers it to compiler AST via the §6 bridge, compiles a call to it with <args>, runs the native binary, and returns (true, <stdout>) or (false, <reason>). Pure-core leaves only; anything the bridge can't lower hard-fails with an `unsupported-*` reason. (Dark resolves a name to its hash; this takes the hash.)"
      fn =
        (function
        | exeState, vm, _, [ DString hash; DList(_, argDvals) ] ->
          uply {
            let effectful = buildEffectfulMap exeState
            let argLits =
              argDvals
              |> List.choose (fun d ->
                match d with
                | DInt64 n -> Some(AST.Int64Literal n)
                | _ -> None)
            if List.length argLits <> List.length argDvals then
              return outcome false "only Int64 args are supported"
            elif List.isEmpty argLits then
              return outcome false "need >= 1 arg (nullary calls not yet supported)"
            else
              let! pieces = buildPieces effectful hash
              match pieces with
              | Error e -> return outcome false e
              | Ok(typeDefs, fds, rootName) ->
                match fds |> List.tryFind (fun fd -> fd.Name = rootName) with
                | None -> return outcome false "root fn not bridged"
                | Some rootFd ->
                match compileClosure typeDefs fds (mainCall rootFd argLits) with
                | Error e -> return outcome false $"compile: {e}"
                | Ok binary ->
                  // Start the host-RPC daemon so any effectful builtins the fn
                  // calls are serviced by the real runtime, then run the binary.
                  let (daemon, shutdown) = startDaemon exeState vm
                  let out = CompilerLibrary.execute 0 binary
                  shutdown ()
                  daemon.Wait()
                  return
                    (if out.ExitCode = 0 then outcome true out.Stdout
                     else outcome false $"exit {out.ExitCode}: {out.Stderr}")
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compileFnCheck" 0
      typeParams = []
      parameters = [ Param.make "hash" TString "content hash of a package function" ]
      returnType = TTuple(TBool, TString, [])
      description =
        "Checks whether a package function compiles via the §6 bridge, WITHOUT running it: fetches its ProgramTypes by hash, lowers to compiler AST, makes it reachable with zero-valued args, and compiles. Returns (true, \"<n> bytes\") if it compiles or (false, \"<unsupported-*>\") with the first blocker. This is the `dark <fn> compile` semantic (compilability, not execution)."
      fn =
        (function
        | exeState, _, _, [ DString hash ] ->
          uply {
            let! (ok, detail) = checkOne (buildEffectfulMap exeState) hash
            return outcome ok detail
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compilerCoverageSweep" 0
      typeParams = []
      parameters =
        [ Param.make "hashes" (TList TString) "package fn content hashes to compile-check" ]
      returnType = TString
      description =
        "The §4 bring-up loop, in ONE process (stdlib built once): compile-checks every fn hash and returns a newline-joined report, one `<ok>|<detail>` line per input hash in order (ok = true/false). Feed it every fn hash to get a whole-tree coverage number + ranked blockers without the per-process stdlib rebuild."
      fn =
        (function
        | exeState, _, _, [ DList(_, hashes) ] ->
          uply {
            let effectful = buildEffectfulMap exeState
            let mutable lines : List<string> = []
            for hd in hashes do
              match hd with
              | DString hash ->
                let! (ok, detail) = checkOne effectful hash
                lines <- lines @ [ $"{ok}|{detail}" ]
              | _ -> lines <- lines @ [ "false|non-string-hash" ]
            return DString(String.concat "\n" lines)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated } ]

let builtins () = LibExecution.Builtin.make [] (fns ())
