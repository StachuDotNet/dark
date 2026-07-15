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
      deprecated = NotDeprecated } ]

let builtins () = LibExecution.Builtin.make [] (fns ())
