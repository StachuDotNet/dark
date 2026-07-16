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
        | None -> return Error $"missing type {h}"
        | Some pt ->
          let deps = Bridge.typeRefsInTypeDef pt
          return! fetchTypeClosure (Set.add h visited) (acc @ [ (h, pt) ]) (rest @ deps)
  }

/// Fetch + bridge the whole graph (fns and their non-generic custom types),
/// returning the compiler TypeDefs, FunctionDefs, and the root's compiled name.
let private buildPieces
  (rootHash : string)
  : Ply<Result<List<AST.TypeDef> * List<AST.FunctionDef> * string, string>> =
  uply {
    let! fnClosure = fetchFnClosure Set.empty [] [ rootHash ]
    match fnClosure with
    | Error e -> return Error e
    | Ok fns ->
      let typeSeeds =
        fns |> List.collect (fun (_, fn) -> Bridge.typeRefsInFn fn) |> List.distinct
      let! typeClosure = fetchTypeClosure Set.empty [] typeSeeds
      match typeClosure with
      | Error e -> return Error e
      | Ok types ->
        // The type env: non-generic record (false) / enum (true) types only.
        // Generic + alias types are omitted, so any fn using one hard-fails.
        let isSum =
          types
          |> List.choose (fun (h, pt) ->
            if List.isEmpty pt.declaration.typeParams then
              match pt.declaration.definition with
              | PT.TypeDeclaration.Record _ -> Some(h, false)
              | PT.TypeDeclaration.Enum _ -> Some(h, true)
              | PT.TypeDeclaration.Alias _ -> None
            else
              None)
          |> Map.ofList
        let typeDefs =
          types
          |> List.filter (fun (h, _) -> Map.containsKey h isSum)
          |> List.map (fun (h, pt) -> Bridge.bridgeTypeDef isSum (Bridge.nameForType h) pt)
          |> allOk
        let fnDefs =
          fns |> List.map (fun (h, fn) -> Bridge.bridgeFn isSum (Bridge.nameFor h) fn) |> allOk
        match typeDefs, fnDefs with
        | Error e, _ -> return Error e
        | _, Error e -> return Error e
        | Ok tds, Ok fds -> return Ok(tds, fds, Bridge.nameFor rootHash)
  }

/// Build the program (type defs + all bridged fns + a call to the root) and
/// compile it.
let private compileClosure
  (typeDefs : List<AST.TypeDef>)
  (fds : List<AST.FunctionDef>)
  (rootName : string)
  (args : List<AST.Expr>)
  : Result<byte array, string> =
  let program : AST.Program =
    AST.Program(
      (typeDefs |> List.map AST.TypeDef)
      @ (fds |> List.map AST.FunctionDef)
      @ [ AST.Expression(AST.Call(rootName, AST.NonEmptyList.fromList args)) ])
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
  | prim -> zeroLit prim

/// Compile-check one package fn by hash (bridge its call-graph, make it
/// reachable with zero-valued args, compile — don't run). Returns (ok, detail).
/// Wrapped so an unexpected crash in one fn can't abort a batch sweep.
let private checkOne (hash : string) : Ply<bool * string> =
  uply {
    try
      let! pieces = buildPieces hash
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
            rootFd.Params
            |> AST.NonEmptyList.toList
            |> List.map (snd >> zeroValue defsByName Set.empty)
          match List.tryPick (function Error e -> Some e | Ok _ -> None) dummy with
          | Some e -> return (false, $"arg-synthesis: {e}")
          | None ->
            let args = dummy |> List.choose (function Ok x -> Some x | Error _ -> None)
            match compileClosure typeDefs fds rootName args with
            | Ok binary -> return (true, $"{binary.Length} bytes")
            | Error e -> return (false, e)
    with e ->
      return (false, $"exn: {e.Message}")
  }

/// Return a (Bool ok, String detail) tuple as a Dark value.
let private outcome (ok : bool) (detail : string) : Dval =
  DTuple(DBool ok, DString detail, [])

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
          match Bridge.bridgeFn Map.empty "bridgedFn" ptFn with
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
        | _, _, _, [ DString hash; DList(_, argDvals) ] ->
          uply {
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
              let! pieces = buildPieces hash
              match pieces with
              | Error e -> return outcome false e
              | Ok(typeDefs, fds, rootName) ->
                match compileClosure typeDefs fds rootName argLits with
                | Error e -> return outcome false $"compile: {e}"
                | Ok binary ->
                  let out = CompilerLibrary.execute 0 binary
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
        | _, _, _, [ DString hash ] ->
          uply {
            let! (ok, detail) = checkOne hash
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
        | _, _, _, [ DList(_, hashes) ] ->
          uply {
            let mutable lines : List<string> = []
            for hd in hashes do
              match hd with
              | DString hash ->
                let! (ok, detail) = checkOne hash
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
