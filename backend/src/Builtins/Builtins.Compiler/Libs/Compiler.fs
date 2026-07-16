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

/// A builtin's RUNTIME return type -> the marshalable compiler AST.Type it decodes
/// to, or None if it can't be recursively unmarshaled. Handles primitives and
/// Option/Result/List of marshalable types (Option/Result recognized by hash);
/// custom-enum payloads (which need the type-def env) return None.
let rec private rtToMarshalable (t : TypeReference) : AST.Type option =
  match t with
  | TString -> Some AST.TString
  | TInt64 -> Some AST.TInt64
  | TInt -> Some AST.TInt64
  | TBool -> Some AST.TBool
  | TUnit -> Some AST.TUnit
  | TList inner -> rtToMarshalable inner |> Option.map AST.TList
  | TCustomType(nr, args) ->
    match nr.resolved with
    | Ok(FQTypeName.Package(Hash h)) ->
      if h = Bridge.optionTypeHash then
        match args with
        | [ inner ] ->
          rtToMarshalable inner
          |> Option.map (fun a -> AST.TSum("Stdlib.Option.Option", [ a ]))
        | _ -> None
      elif h = Bridge.resultTypeHash then
        match args with
        | [ a; b ] ->
          match rtToMarshalable a, rtToMarshalable b with
          | Some ab, Some bb -> Some(AST.TSum("Stdlib.Result.Result", [ ab; bb ]))
          | _ -> None
        | _ -> None
      else None
    | _ -> None
  | _ -> None

/// Fetch + bridge the whole graph (fns and their non-generic custom types),
/// returning the compiler TypeDefs, FunctionDefs, and the root's compiled name.
/// Effectful builtins routable through the host RPC seam, from the live runtime:
/// name -> returns-String (true) / returns-Unit (false); included only when all
/// params are String (the wire-marshalable subset in v1).
let private buildEffectfulMap
  (exeState : ExecutionState)
  : Map<string, Bridge.WireArg list * Bridge.WireRet> =
  exeState.fns.builtIn
  |> Map.toList
  |> List.choose (fun (name, bfn) ->
    let argWires =
      bfn.parameters
      |> List.map (fun p ->
        match p.typ with
        | TString -> Some Bridge.WAString
        | TInt64 -> Some Bridge.WAInt
        | TInt -> Some Bridge.WAInt
        | TBool -> Some Bridge.WABool
        | TFloat -> Some Bridge.WAFloat
        | TUnit -> Some Bridge.WAUnit
        | TChar -> Some Bridge.WAString // a Char is a single grapheme, sent as a string
        // Host-opaque types travel as their canonical string (see bridgeType).
        // Uuid round-trips cleanly (Guid parse); DateTime args are omitted until
        // wire->DateTime ISO parsing is in (return-only for now).
        | TUuid -> Some Bridge.WAString
        | _ -> None)
    let wireRet =
      match bfn.returnType with
      | TUnit -> Some Bridge.WRUnit
      | TString -> Some Bridge.WRString
      | TInt64 -> Some Bridge.WRInt
      | TInt -> Some Bridge.WRInt
      | TBool -> Some Bridge.WRBool
      | TUuid -> Some Bridge.WRString
      | TDateTime -> Some Bridge.WRString
      | other ->
        // General container returns (Option/Result/List of marshalable types);
        // the recursive unmarshaller decodes them. Custom-enum payloads (needing
        // the type-def env) return None here and stay unrouted.
        match rtToMarshalable other with
        | Some bt -> Some(Bridge.WRTyped bt)
        | None -> None
    match wireRet with
    | Some ret when not (List.exists Option.isNone argWires) ->
      Some(name.name, (argWires |> List.map Option.get, ret))
    | _ -> None)
  |> Map.ofList

let private buildPieces
  (effectful : Map<string, Bridge.WireArg list * Bridge.WireRet>)
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
      | Ok typesRaw ->
        // Option/Result are defined natively by the compiler stdlib and bridged to
        // those native types (Bridge.isNativeType). Drop them from the fetched
        // closure so we don't ALSO emit T_<hash> defs — that would double-define
        // Some/None/Ok/Error and make every use ambiguous.
        let types = typesRaw |> List.filter (fun (h, _) -> not (Bridge.isNativeType h))
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
        // expression. A value can reference another value; the fetch order isn't
        // dependency order, so iterate to a fixpoint — each pass bridges any value
        // whose referenced values are now in the map — rather than dropping a
        // forward reference on a single ordered pass.
        let bridgeValuesOnce (m : Map<string, AST.Expr>) : Map<string, AST.Expr> =
          (m, values)
          ||> List.fold (fun m (h, pv) ->
            if Map.containsKey h m then m
            else
              let ctx : Bridge.BridgeCtx = { Params = [||]; Self = ""; Values = m; Effectful = Map.empty }
              match Bridge.bridgeExpr ctx pv.body with
              | Ok e -> Map.add h e m
              | Error _ -> m)
        let rec fixpoint (m : Map<string, AST.Expr>) : Map<string, AST.Expr> =
          let m' = bridgeValuesOnce m
          if Map.count m' = Map.count m then m' else fixpoint m'
        let valuesMap = fixpoint Map.empty
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
/// Build a type-var substitution for a generic type def instantiated with `args`:
/// each declared type param maps to its positional arg, or Int64 when the type is
/// referenced with fewer args than params (the harness only needs *a* concrete
/// monomorphization to check compilability).
let private defSubst (typeParams : string list) (args : AST.Type list) : Map<string, AST.Type> =
  typeParams
  |> List.mapi (fun i tp -> (tp, List.tryItem i args |> Option.defaultValue AST.TInt64))
  |> Map.ofList

let rec private zeroValue
  (defs : Map<string, AST.TypeDef>)
  (seen : Set<string>)
  (t : AST.Type)
  : Result<AST.Expr, string> =
  match t with
  | AST.TRecord(name, args) ->
    if Set.contains name seen then Error $"recursive type {name}"
    else
      match Map.tryFind name defs with
      | Some(AST.RecordDef(_, tps, fields)) ->
        // Instantiate the generic def's type params with the reference's args, so
        // a field typed `T` becomes its concrete type before we synthesize a zero.
        let subst = defSubst tps args
        fields
        |> List.map (fun (fname, ft) ->
          zeroValue defs (Set.add name seen) (substTVars subst ft)
          |> Result.map (fun v -> (fname, v)))
        |> allOk
        |> Result.map (fun fs -> AST.RecordLiteral(name, fs))
      | _ -> Error $"no record def {name}"
  // Native Option/Result have no emitted TypeDef (the compiler stdlib defines
  // them), so synthesize their zeros directly: None for Option, Ok<zero> for
  // Result (falling back to Error<zero> if the Ok type can't be zeroed).
  | AST.TSum("Stdlib.Option.Option", _) ->
    Ok(AST.Constructor("Stdlib.Option.Option", "None", None))
  | AST.TSum("Stdlib.Result.Result", args) ->
    let okT = List.tryItem 0 args |> Option.defaultValue AST.TInt64
    let errT = List.tryItem 1 args |> Option.defaultValue AST.TString
    match zeroValue defs seen okT with
    | Ok v -> Ok(AST.Constructor("Stdlib.Result.Result", "Ok", Some v))
    | Error _ ->
      zeroValue defs seen errT
      |> Result.map (fun v -> AST.Constructor("Stdlib.Result.Result", "Error", Some v))
  | AST.TSum(name, args) ->
    if Set.contains name seen then Error $"recursive type {name}"
    else
      match Map.tryFind name defs with
      | Some(AST.SumTypeDef(_, tps, variants)) when not (List.isEmpty variants) ->
        let subst = defSubst tps args
        // Try variants in order; a recursive type (e.g. `Nil | Cons of ...`) is
        // zeroable via its non-recursive base case, so take the first that
        // terminates rather than forcing the first-declared variant.
        let zeroed =
          variants
          |> List.tryPick (fun variant ->
            match variant.Payload with
            | None -> Some(AST.Constructor(name, variant.Name, None))
            | Some pt ->
              match zeroValue defs (Set.add name seen) (substTVars subst pt) with
              | Ok pv -> Some(AST.Constructor(name, variant.Name, Some pv))
              | Error _ -> None)
        match zeroed with
        | Some c -> Ok c
        | None -> Error $"recursive type {name}"
      | _ -> Error $"no sum def {name}"
  | AST.TTuple ts ->
    ts |> List.map (zeroValue defs seen) |> allOk |> Result.map AST.TupleLiteral
  | AST.TList _ -> Ok(AST.ListLiteral [])
  // Dict.empty is nullary AND generic, so give it explicit type args (we know k/v
  // here) rather than relying on inference from a call site that has none. The Unit
  // arg is how the compiler spells a nullary call (normalizeNullaryCallArgs drops it).
  | AST.TDict(k, v) ->
    Ok(AST.TypeApp("Stdlib.Dict.empty", [ k; v ], AST.NonEmptyList.singleton AST.UnitLiteral))
  | AST.TFunction(argTypes, retType) ->
    // A function param: synthesize a dummy lambda `(a0, a1, …) => <zero of ret>`
    // that ignores its args, so a higher-order fn can be made reachable. Param
    // types are TVar so the compiler infers them at the call site.
    zeroValue defs seen retType
    |> Result.map (fun body ->
      let ps =
        argTypes
        |> List.mapi (fun i _ -> ($"__zlam{i}", AST.TVar $"__zlamt{i}"))
      match ps with
      | [] -> AST.Lambda(AST.NonEmptyList.fromList [ ("__zlam0", AST.TVar "__zlamt0") ], body)
      | _ -> AST.Lambda(AST.NonEmptyList.fromList ps, body))
  | AST.TVar _ -> zeroLit AST.TInt64 // an unmapped signature tvar: monomorphize at Int64
  | AST.TBytes ->
    // No bytes literal exists in the AST; synthesize an empty blob via the cast
    // intrinsic. Only ever used to make a fn reachable for a compile-check (never
    // run), so it just needs to typecheck to Bytes.
    Ok(AST.Call("Stdlib.__int64_to_bytes", AST.NonEmptyList.fromList [ AST.Int64Literal 0L ]))
  | prim -> zeroLit prim

/// Compile-check one package fn by hash (bridge its call-graph, make it
/// reachable with zero-valued args, compile — don't run). Returns (ok, detail).
/// Wrapped so an unexpected crash in one fn can't abort a batch sweep.
let private checkOne (effectful : Map<string, Bridge.WireArg list * Bridge.WireRet>) (hash : string) : Ply<bool * string> =
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

/// Opaque-handle table: complex values (List/Dict/…) the compiler can't represent
/// natively on x64 (the finger-tree DEEP-node runtime bug) live here as real Dvals;
/// compiled code holds only an Int64 handle and routes every operation on them back
/// through the daemon to the REAL builtins. Daemon requests are serviced on one
/// thread, so a plain Dictionary is safe.
let private handleTable = System.Collections.Generic.Dictionary<int64, Dval>()
let mutable private handleCounter = 0L


let private storeHandle (d : Dval) : int64 =
  handleCounter <- handleCounter + 1L
  handleTable[handleCounter] <- d
  handleCounter

let private getHandle (id : int64) : Dval option =
  match handleTable.TryGetValue id with
  | true, d -> Some d
  | _ -> None

/// Escape a child's encoding so a raw newline only ever separates children
/// (matches Stdlib.hostRpcEscape on the compiled side); composes to any depth.
let private escWire (s : string) : string =
  s.Replace("\\", "\\\\").Replace("\n", "\\n")

/// Dval -> wire string. Scalars are literal; enums (Option/Result) recurse; a
/// list becomes an opaque handle id (see handleTable) since the compiler can't
/// build a native multi-element list on x64.
let rec private dvalToWire (d : Dval) : string =
  match d with
  | DEnum(_, _, _, caseName, []) -> caseName
  | DEnum(_, _, _, caseName, fields) ->
    caseName + "\n" + (fields |> List.map (dvalToWire >> escWire) |> String.concat "\n")
  // A list marshals as its escaped elements joined by "\n" (same shape as DTuple),
  // which is exactly what Bridge.unmarshalTyped's TList decoder expects: split on
  // "\n", unescape + decode each, empty string -> []. It used to store an opaque
  // HANDLE here (a leftover from the handle-model detour, which the finger-tree
  // misdiagnosis motivated and native lists made unnecessary). Since TList
  // marshalling was re-enabled, that silently corrupted EVERY effectful builtin
  // returning a List: the daemon sent the handle id and the decoder read it back
  // as a one-element list, so `Directory.list` returned ["1"] instead of the
  // actual entries. Found by the differential run-sweep, not by a compile check.
  // The __list* handle primitives call storeHandle directly, so they're unaffected.
  | DList(_, xs) -> xs |> List.map (dvalToWire >> escWire) |> String.concat "\n"
  | DTuple(a, b, rest) ->
    (a :: b :: rest) |> List.map (dvalToWire >> escWire) |> String.concat "\n"
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
  // Host-opaque types -> canonical string (see Bridge.bridgeType for soundness).
  | DUuid g -> string g
  | DDateTime dt -> LibExecution.DarkDateTime.toIsoString dt
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
  | TUuid -> DUuid(System.Guid.Parse s)
  // A list/dict arg arrives as an opaque handle id -> resolve to the real Dval.
  | TList _
  | TDict _ ->
    match System.Int64.TryParse s with
    | true, id ->
      match getHandle id with
      | Some d -> d
      | None -> DString s
    | _ -> DString s
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
    // Handle-based list primitives: build/walk a real Dval list held in the daemon,
    // so compiled code can work with multi-element lists (which the native x64
    // finger-tree can't) without ever constructing one. Element payloads are
    // String for now (the common case); typed elements come later.
    let listOf (hStr : string) : List<Dval> option =
      match System.Int64.TryParse hStr with
      | true, id ->
        match getHandle id with
        | Some(DList(_, xs)) -> Some xs
        | _ -> None
      | _ -> None
    let tryPrimitive () : string option =
      match name, wireArgs with
      | "__listEmpty", _ -> Some(string (storeHandle (DList(ValueType.Unknown, []))))
      | "__listConsStr", [ h; elem ] ->
        listOf h
        |> Option.map (fun xs ->
          string (storeHandle (DList(ValueType.Unknown, DString elem :: xs))))
      // Typed cons: prepend a wire element decoded per its type tag, so lists of
      // Int/Bool/Float (not just String) build correctly via handles.
      | "__listCons", [ h; tag; elem ] ->
        listOf h
        |> Option.map (fun xs ->
          let dv =
            match tag with
            | "i" -> DInt64(int64 elem)
            | "b" -> DBool(elem = "true")
            | "f" -> DFloat(float elem)
            | _ -> DString elem
          string (storeHandle (DList(ValueType.Unknown, dv :: xs))))
      | "__listLen", [ h ] -> listOf h |> Option.map (List.length >> string)
      | "__listIsEmpty", [ h ] ->
        listOf h |> Option.map (fun xs -> if List.isEmpty xs then "true" else "false")
      | "__listHeadStr", [ h ] ->
        listOf h
        |> Option.map (fun xs ->
          match xs with
          | DString s :: _ -> s
          | d :: _ -> dvalToWire d
          | [] -> "")
      | "__listTail", [ h ] ->
        listOf h
        |> Option.map (fun xs ->
          let t =
            match xs with
            | _ :: t -> t
            | [] -> []
          string (storeHandle (DList(ValueType.Unknown, t))))
      | _ -> None
    match tryPrimitive () with
    | Some resp -> return resp
    | None ->
    match Map.tryFind (FQFnName.builtin name 0) exeState.fns.builtIn with
    | None -> return $"ERR:no-builtin:{name}"
    | Some bfn ->
      let n = min bfn.parameters.Length wireArgs.Length
      let dvalArgs =
        List.map2
          (fun (p : BuiltInParam) (a : string) -> wireToDval p.typ a)
          (List.truncate n bfn.parameters)
          (List.truncate n wireArgs)
      // A builtin that raises (e.g. a capability check, a bad arg) must still
      // produce a response, or the compiled program polls the response file
      // forever. Return an error marker instead of letting it propagate.
      try
        let! result = bfn.fn (exeState, vm, [], dvalArgs)
        return dvalToWire result
      with e ->
        return $"ERR:exn:{e.Message}"
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

/// Compile a fn by hash, make it reachable with zero args, RUN it (daemon up, so
/// effectful builtins are serviced) with a timeout, and classify the outcome:
/// "cf|<blocker>" compile-fail, "hang" (timed out — e.g. multi-element list),
/// "crash|<code>", or "ran|<stdout>". This is the runnable-vs-compilable measure.
let private runOne
  (exeState : ExecutionState)
  (vm : VMState)
  (effectful : Map<string, Bridge.WireArg list * Bridge.WireRet>)
  (timeoutMs : int)
  (hash : string)
  : Ply<string> =
  uply {
    try
      let! pieces = buildPieces effectful hash
      match pieces with
      | Error e -> return "cf|" + e
      | Ok(typeDefs, fds, rootName) ->
        match fds |> List.tryFind (fun fd -> fd.Name = rootName) with
        | None -> return "cf|root not bridged"
        | Some rootFd ->
          let defsByName =
            typeDefs
            |> List.choose (fun td ->
              match td with
              | AST.RecordDef(n, _, _) -> Some(n, td)
              | AST.SumTypeDef(n, _, _) -> Some(n, td)
              | AST.TypeAlias(n, _, _) -> Some(n, td))
            |> Map.ofList
          let dummy = rootParamTypes rootFd |> List.map (zeroValue defsByName Set.empty)
          match List.tryPick (function Error e -> Some e | Ok _ -> None) dummy with
          | Some e -> return "cf|arg-synth: " + e
          | None ->
            let args = dummy |> List.choose (function Ok x -> Some x | Error _ -> None)
            match compileClosure typeDefs fds (mainCall rootFd args) with
            | Error e -> return "cf|" + e
            | Ok binary ->
              let (daemon, shutdown) = startDaemon exeState vm
              let out =
                try CompilerLibrary.execute 0 timeoutMs binary
                with e -> { ExitCode = -1; Stdout = ""; Stderr = e.Message; RuntimeTime = System.TimeSpan.Zero }
              shutdown ()
              let _ = (try daemon.Wait() with _ -> ())
              if out.ExitCode = -999 then return "hang"
              elif out.ExitCode <> 0 then return $"crash|{out.ExitCode}"
              else
                let compiledOut = out.Stdout.Trim()
                // Differential check: run the SAME fn in the interpreter with the
                // same zero args and compare. Restricted to non-generic fns whose
                // params are scalars (so the Dval zeros exactly match the compiled
                // zeroValue and the printed formats align) — otherwise just "ran".
                let! ptFnOpt = LibDB.PackageManager.pt.getFn (PT.FQFnName.package hash)
                match ptFnOpt with
                | Some ptFn when List.isEmpty ptFn.typeParams ->
                  let paramList = NEList.toList ptFn.parameters
                  // Fetch the param types' closure so record/enum params can be
                  // zeroed the same way the compiled zeroValue does.
                  let typeSeeds =
                    paramList
                    |> List.collect (fun (p : PT.PackageFn.Parameter) -> Bridge.typeRefsInType p.typ)
                    |> List.distinct
                  let! typeClosure = fetchTypeClosure Set.empty [] typeSeeds
                  let declMap =
                    match typeClosure with
                    | Ok ts -> ts |> List.map (fun (h, pt) -> (h, pt.declaration)) |> Map.ofList
                    | Error _ -> Map.empty
                  // Interpreter-side zero, mirroring zeroValue. Conservative: None
                  // (skip the diff) for generics/aliases/functions/dicts, so a
                  // synthesis mismatch can never masquerade as a miscompile.
                  let rec zeroDvalPT (seen : Set<string>) (t : PT.TypeReference) : Dval option =
                    match t with
                    | PT.TInt64 | PT.TInt -> Some(DInt64 0L)
                    | PT.TString -> Some(DString "")
                    | PT.TBool -> Some(DBool false)
                    | PT.TChar -> Some(DChar "a")
                    | PT.TUnit -> Some DUnit
                    | PT.TFloat -> Some(DFloat 0.0)
                    | PT.TList _ -> Some(DList(ValueType.Unknown, []))
                    | PT.TTuple(a, b, rest) ->
                      let zs = (a :: b :: rest) |> List.map (zeroDvalPT seen)
                      if List.exists Option.isNone zs then None
                      else
                        match zs |> List.choose (fun x -> x) with
                        | x1 :: x2 :: r -> Some(DTuple(x1, x2, r))
                        | _ -> None
                    | PT.TCustomType(nr, _) ->
                      match nr.resolved with
                      | Ok resolved ->
                        match resolved.name with
                        | PT.FQTypeName.Package(PT.Hash h) ->
                          let tn = FQTypeName.fqPackage h
                          if h = Bridge.optionTypeHash then
                            Some(DEnum(tn, tn, [ ValueType.Unknown ], "None", []))
                          elif Set.contains h seen then None
                          else
                            match Map.tryFind h declMap with
                            | Some(decl : PT.TypeDeclaration.T) when List.isEmpty decl.typeParams ->
                              match decl.definition with
                              | PT.TypeDeclaration.Record fields ->
                                let fzs =
                                  NEList.toList fields
                                  |> List.map (fun (f : PT.TypeDeclaration.RecordField) ->
                                    (f.name, zeroDvalPT (Set.add h seen) f.typ))
                                if fzs |> List.exists (snd >> Option.isNone) then None
                                else
                                  Some(
                                    DRecord(
                                      tn, tn, [],
                                      fzs |> List.map (fun (n, v) -> (n, Option.get v)) |> Map.ofList))
                              | PT.TypeDeclaration.Enum cases ->
                                NEList.toList cases
                                |> List.tryPick (fun (c : PT.TypeDeclaration.EnumCase) ->
                                  let pzs =
                                    c.fields
                                    |> List.map (fun (ef : PT.TypeDeclaration.EnumField) ->
                                      zeroDvalPT (Set.add h seen) ef.typ)
                                  if pzs |> List.exists Option.isNone then None
                                  else Some(DEnum(tn, tn, [], c.name, pzs |> List.choose (fun x -> x))))
                              | PT.TypeDeclaration.Alias _ -> None
                            | _ -> None
                      | Error _ -> None
                    | _ -> None
                  let zeros : List<Dval option> =
                    paramList |> List.map (fun (p : PT.PackageFn.Parameter) -> zeroDvalPT Set.empty p.typ)
                  if List.exists Option.isNone zeros then
                    return "ran|" + compiledOut.Replace("\n", "\\n")
                  else
                    let dvalArgs : List<Dval> = zeros |> List.choose (fun x -> x)
                    let argsNE =
                      match dvalArgs with
                      | [] -> NEList.singleton DUnit
                      | args -> NEList.ofListUnsafe "runOne diff args" [] args
                    let runInterp () =
                      try
                        match (LibExecution.Execution.executeFunction
                                 exeState (FQFnName.fqPackage hash) [] argsNE).Result with
                        | Ok v -> Some v
                        | Error _ -> None
                      with _ -> None
                    // Repr matching the compiler's own print format so outputs can be
                    // compared: strings raw at top level but quoted inside a list,
                    // list as [a, b], Unit as "". None => not comparable (skip).
                    let rec reprNested (d : Dval) : string option =
                      match d with
                      | DString s -> Some("\"" + s + "\"")
                      | _ -> scalarRepr d
                    and scalarRepr (d : Dval) : string option =
                      match d with
                      | DInt64 n -> Some(string n)
                      | DInt n -> Some(string (DarkInt.toBigInt n))
                      | DString s -> Some s
                      | DBool b -> Some(if b then "true" else "false")
                      | DFloat f -> Some(string f)
                      | DChar c -> Some c
                      | DUnit -> Some ""
                      | DList(_, xs) ->
                        let parts = xs |> List.map reprNested
                        if List.exists Option.isNone parts then None
                        else
                          let strs = parts |> List.choose (fun (x : string option) -> x)
                          Some("[" + String.concat ", " strs + "]")
                      | _ -> None
                    let iv1, iv2 = runInterp (), runInterp ()
                    match iv1, iv2 with
                    | Some a, Some b when (scalarRepr a) <> (scalarRepr b) ->
                      // interpreter itself gave two answers -> effectful/non-deterministic; don't compare
                      return "nondet|" + compiledOut.Replace("\n", "\\n")
                    | None, _ | _, None -> return "ierr|" + compiledOut.Replace("\n", "\\n")
                    | Some iv, _ ->
                      match scalarRepr iv with
                      | None -> return "ran|" + compiledOut.Replace("\n", "\\n")
                      | Some istr ->
                        let norm (s : string) =
                          let t = (s.Trim().Trim('"')).Replace("\n", "\\n")
                          if t = "()" || t = "[]" then "" else t
                        let ca, ia = norm compiledOut, norm istr
                        // numeric equality handles the compiler's "0.0" vs the interp's "0"
                        let numEq =
                          match System.Double.TryParse ca, System.Double.TryParse ia with
                          | (true, x), (true, y) -> x = y
                          | _ -> false
                        if ca = ia || numEq then return "match|" + ia
                        else return $"diff|c={ca}|i={ia}"
                | _ -> return "ran|" + compiledOut.Replace("\n", "\\n")
    with e -> return "cf|exn: " + e.Message
  }

/// Benchmark one Int64->Int64 package fn at arg n: compile it (bridge -> native
/// binary), run compiled vs interpreted (in-process), and return a pretty report.
let private perfBench (exeState : ExecutionState) (hash : string) (n : int64) : Ply<string> =
  uply {
    let effectful = buildEffectfulMap exeState
    let! pieces = buildPieces effectful hash
    match pieces with
    | Error e -> return "  ✗ does not compile: " + e
    | Ok(typeDefs, fds, rootName) ->
      let program : AST.Program =
        AST.Program(
          (typeDefs |> List.map AST.TypeDef)
          @ (fds |> List.map AST.FunctionDef)
          @ [ AST.Expression(AST.Call(rootName, AST.NonEmptyList.singleton (AST.Int64Literal n))) ])
      match compileAst program with
      | Error e -> return "  ✗ compile error: " + e
      | Ok binary ->
        let _warm = CompilerLibrary.execute 0 120000 binary
        let swC = System.Diagnostics.Stopwatch.StartNew()
        let outC = CompilerLibrary.execute 0 120000 binary
        swC.Stop()
        let args = NEList.singleton (DInt64 n)
        let swI = System.Diagnostics.Stopwatch.StartNew()
        let interp =
          try
            match (LibExecution.Execution.executeFunction exeState (FQFnName.fqPackage hash) [] args).Result with
            | Ok v -> dvalToWire v
            | Error _ -> "<interp error>"
          with e -> "<interp exn: " + e.Message + ">"
        swI.Stop()
        let cOut = outC.Stdout.Trim()
        let cMs = swC.Elapsed.TotalMilliseconds
        let iMs = swI.Elapsed.TotalMilliseconds
        let agree = if cOut = interp then "✓ match" else "✗ DIFFER"
        let speedup = if cMs > 0.0 then System.Math.Round(iMs / cMs, 0) else 0.0
        let bytes = binary.Length
        return
          "  arg           n = " + string n + "\n"
          + "  result        " + cOut + "   (compiled vs interpreted: " + agree + ")\n"
          + "  native size   " + string bytes + " bytes\n"
          + "  interpreted   " + string (System.Math.Round(iMs, 2)) + " ms\n"
          + "  compiled      " + string (System.Math.Round(cMs, 2)) + " ms  (incl. process launch)\n"
          + "  speedup       " + string speedup + "x  faster (compiled)"
  }

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
            let out = CompilerLibrary.execute 0 10000 binary
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
              let out = CompilerLibrary.execute 0 10000 binary
              daemon.Wait()
              return DString $"binary stdout={out.Stdout.Trim()} (expected 5)"
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compilerMarshalSelfTest" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TString
      description =
        "Proves the general container marshaller round-trips at runtime: compiles a program that calls the REAL environmentGet builtin through the daemon (returns Option<String>), decodes the wire response via Bridge.unmarshalTyped over Option<String>, and Option.withDefault-extracts it. Returns the binary stdout (expect the value of $HOME)."
      fn =
        (function
        | exeState, vm, _, [ DUnit ] ->
          uply {
            let daemon = serveOneRequest exeState vm
            // Option<String>: environmentGet "HOME" through the daemon -> decode -> withDefault
            let optStrTy = AST.TSum("Stdlib.Option.Option", [ AST.TString ])
            let rpcCall =
              AST.Call(
                "Stdlib.hostRpc",
                AST.NonEmptyList.singleton (AST.StringLiteral "environmentGet\nHOME"))
            let decoded = Bridge.unmarshalTyped 0 optStrTy rpcCall
            let program : AST.Program =
              AST.Program
                [ AST.Expression(
                    AST.TypeApp(
                      "Stdlib.Option.withDefault",
                      [ AST.TString ],
                      AST.NonEmptyList.fromList [ decoded; AST.StringLiteral "NONE" ])) ]
            match compileAst program with
            | Error e ->
              daemon.Wait()
              return DString $"compile-error: {e}"
            | Ok binary ->
              let out = CompilerLibrary.execute 0 10000 binary
              daemon.Wait()
              return DString(out.Stdout.Trim())
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
              let out = CompilerLibrary.execute 0 10000 binary
              if out.ExitCode = 0 then outcome true out.Stdout |> Ply
              else outcome false $"exit {out.ExitCode}: {out.Stderr}" |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compilerHandleSelfTest" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TString
      description =
        "Proves the opaque-handle model end to end: compiles a program that (1) calls the REAL stringSplit builtin through the daemon (returns a List -> stored as a handle id), then (2) passes that handle to the REAL listLength builtin. The compiled code only ever touches strings/ints (no native finger-tree), yet a list flows between two builtins via the daemon. Returns stdout (expect 3)."
      fn =
        (function
        | exeState, vm, _, [ DUnit ] ->
          uply {
            let (daemon, shutdown) = startDaemon exeState vm
            // let h = hostRpc("stringSplit\na,b,c\n,") in hostRpc("listLength\n" ++ h)
            let rpc req = AST.Call("Stdlib.hostRpc", AST.NonEmptyList.singleton req)
            let program : AST.Program =
              AST.Program
                [ AST.Expression(
                    AST.Let(
                      "h",
                      rpc (AST.StringLiteral "stringSplit\na,b,c\n,"),
                      rpc (AST.BinOp(AST.StringConcat, AST.StringLiteral "listLength\n", AST.Var "h")))) ]
            match compileAst program with
            | Error e ->
              shutdown ()
              daemon.Wait()
              return DString $"compile-error: {e}"
            | Ok binary ->
              let out = CompilerLibrary.execute 0 10000 binary
              shutdown ()
              daemon.Wait()
              return DString(out.Stdout.Trim())
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compilerListHandleSelfTest" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TString
      description =
        "Proves the compiled side can BUILD and query a multi-element list via handles (the native x64 finger-tree cannot): conses [a,b,c] through the daemon (__listEmpty/__listConsStr) then __listLen -> 3. Every value the compiled code holds is a String/Int handle."
      fn =
        (function
        | exeState, vm, _, [ DUnit ] ->
          uply {
            let (daemon, shutdown) = startDaemon exeState vm
            let rpc req = AST.Call("Stdlib.hostRpc", AST.NonEmptyList.singleton req)
            let cat a b = AST.BinOp(AST.StringConcat, a, b)
            let consReq hVar elem =
              cat (cat (AST.StringLiteral "__listConsStr\n") (AST.Var hVar)) (AST.StringLiteral ("\n" + elem))
            let program : AST.Program =
              AST.Program
                [ AST.Expression(
                    AST.Let("h0", rpc (AST.StringLiteral "__listEmpty"),
                      AST.Let("h1", rpc (consReq "h0" "c"),
                        AST.Let("h2", rpc (consReq "h1" "b"),
                          AST.Let("h3", rpc (consReq "h2" "a"),
                            rpc (cat (AST.StringLiteral "__listLen\n") (AST.Var "h3"))))))) ]
            match compileAst program with
            | Error e ->
              shutdown ()
              daemon.Wait()
              return DString $"compile-error: {e}"
            | Ok binary ->
              let out = CompilerLibrary.execute 0 10000 binary
              shutdown ()
              daemon.Wait()
              return DString(out.Stdout.Trim())
          }
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
                  let out = CompilerLibrary.execute 0 10000 binary
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
      deprecated = NotDeprecated }

    { name = fn "compilerNativeListTest" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TString
      description = "Debug: does a native multi-element list [7,8,9] match head work at runtime (no daemon)? Returns 7, or hangs/crashes if the finger-tree is broken."
      fn =
        (function
        | _, _, _, [ DUnit ] ->
          uply {
            // Decode a wire list "alpha\\nbeta\\ngamma" via unmarshalTyped(List<String>), no daemon -> length 3
            let decoded = Bridge.unmarshalTyped 0 (AST.TList AST.TString) (AST.StringLiteral "alpha\nbeta\ngamma")
            let len = AST.TypeApp("Stdlib.List.length", [ AST.TString ], AST.NonEmptyList.singleton decoded)
            let program = AST.Program [ AST.Expression(AST.Call("Stdlib.Int64.toString", AST.NonEmptyList.singleton len)) ]
            match compileAst program with
            | Error e -> return DString ("compile: " + e)
            | Ok binary ->
              let out = CompilerLibrary.execute 0 5000 binary
              return DString (if out.ExitCode = -999 then "HANG" elif out.ExitCode <> 0 then $"crash {out.ExitCode}" else out.Stdout.Trim())
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compilerShowFn" 0
      typeParams = []
      parameters = [ Param.make "hash" TString "package fn content hash" ]
      returnType = TString
      description = "Debug: fetch a package fn by hash and return its name + F# structural repr of params and body."
      fn =
        (function
        | _, _, _, [ DString hash ] ->
          uply {
            let! fnOpt = LibDB.PackageManager.pt.getFn (PT.FQFnName.package hash)
            match fnOpt with
            | None -> return DString "not found"
            | Some(f : PT.PackageFn.PackageFn) ->
              let ps =
                NEList.toList f.parameters
                |> List.map (fun (p : PT.PackageFn.Parameter) -> $"{p.name}: {p.typ}")
                |> String.concat ", "
              return DString $"ret={f.returnType}\ntypeParams={f.typeParams}\nparams=({ps})\nbody={f.body}"
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compilerPerfCompare" 0
      typeParams = []
      parameters =
        [ Param.make "hash" TString "package fn hash (Int64 -> Int64)"
          Param.make "n" TInt64 "the Int64 argument" ]
      returnType = TString
      description = "Benchmarks one Int64->Int64 fn at arg n: runs it compiled (native binary) and interpreted (executeFunction), times each with a Stopwatch, and reports both wall times + the result. For a fair compute comparison use a heavy fn (e.g. naive fib)."
      fn =
        (function
        | exeState, _, _, [ DString hash; DInt64 n ] ->
          uply {
            let! report = perfBench exeState hash n
            return DString report
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "perfCompareEval" 0
      typeParams = []
      parameters =
        [ Param.makeWithArgs "fn" (TFn(NEList.singleton TInt64, TInt64)) "an Int64 -> Int64 fn" [ "n" ]
          Param.make "n" TInt64 "the Int64 argument" ]
      returnType = TString
      description = "The user-facing perf comparison: pass a fn value (e.g. Stdlib.PerfDemo.fib) and an arg; compiles it, runs compiled-native vs interpreted in-process, and returns a pretty report with both times, the speedup, and whether the results match."
      fn =
        (function
        | exeState, _, _, [ DApplicable(AppNamedFn nf); DInt64 n ] ->
          uply {
            match nf.name with
            | FQFnName.Package(Hash h) ->
              let! report = perfBench exeState h n
              return DString ("\n" + report + "\n")
            | _ -> return DString "perfCompareEval: not a package fn"
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "compilerRunSweep" 0
      typeParams = []
      parameters =
        [ Param.make "hashes" (TList TString) "package fn content hashes to compile AND run" ]
      returnType = TString
      description =
        "Runnable-vs-compilable sweep: for each fn hash, compiles it, makes it reachable with zero args, and RUNS the binary (daemon up) with a 2s timeout. One line per hash: cf|<blocker> (didn't compile), hang (timed out — e.g. the multi-element list bug), crash|<code>, or ran|<stdout>. Turns the compile-coverage number into an actually-executes number and surfaces runtime hangs as failures."
      fn =
        (function
        | exeState, vm, _, [ DList(_, hashes) ] ->
          uply {
            let effectful = buildEffectfulMap exeState
            let mutable lines : List<string> = []
            for hd in hashes do
              match hd with
              | DString hash ->
                let! r = runOne exeState vm effectful 2000 hash
                lines <- lines @ [ hash + "\t" + r ]
              | _ -> lines <- lines @ [ "?\tcf|non-string-hash" ]
            return DString(String.concat "\n" lines)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated } ]

let builtins () = LibExecution.Builtin.make [] (fns ())
