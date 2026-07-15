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

/// Return a (Bool ok, String detail) tuple as a Dark value.
let private outcome (ok : bool) (detail : string) : Dval =
  DTuple(DBool ok, DString detail, [])

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
          match Bridge.bridgeFn "bridgedFn" ptFn with
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
            let! fnOpt = LibDB.PackageManager.pt.getFn (PT.FQFnName.package hash)
            match fnOpt with
            | None -> return outcome false $"no package fn with hash {hash}"
            | Some ptFn ->
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
                match Bridge.bridgeFn "bridgedFn" ptFn with
                | Error e -> return outcome false $"bridge: {e}"
                | Ok fd ->
                  let program : AST.Program =
                    AST.Program
                      [ AST.FunctionDef fd
                        AST.Expression(
                          AST.Call("bridgedFn", AST.NonEmptyList.fromList argLits)) ]
                  match compileAst program with
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
            let! fnOpt = LibDB.PackageManager.pt.getFn (PT.FQFnName.package hash)
            match fnOpt with
            | None -> return outcome false $"no package fn with hash {hash}"
            | Some ptFn ->
              match Bridge.bridgeFn "bridgedFn" ptFn with
              | Error e -> return outcome false e
              | Ok fd ->
                let dummyArgs = fd.Params |> AST.NonEmptyList.toList |> List.map (snd >> zeroLit)
                match List.tryPick (function Error e -> Some e | Ok _ -> None) dummyArgs with
                | Some e -> return outcome false $"arg-synthesis: {e}"
                | None ->
                  let args = dummyArgs |> List.choose (function Ok x -> Some x | Error _ -> None)
                  let program : AST.Program =
                    AST.Program
                      [ AST.FunctionDef fd
                        AST.Expression(
                          AST.Call("bridgedFn", AST.NonEmptyList.fromList args)) ]
                  match compileAst program with
                  | Ok binary -> return outcome true $"{binary.Length} bytes"
                  | Error e -> return outcome false e
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated } ]

let builtins () = LibExecution.Builtin.make [] (fns ())
