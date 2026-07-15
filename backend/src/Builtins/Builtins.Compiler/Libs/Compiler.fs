module Builtins.Compiler.Libs.Compiler

// The compiler extension's builtin surface. For now this is a single
// linkage-proving placeholder; the real `compile` builtins and the
// ProgramTypes -> compiler-AST bridge (plan §5/§6) land here next.

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

/// `Builtin.compilerInfo ()` — reports that the native compiler extension is
/// linked and reachable. It deliberately reads a value out of the airlifted
/// `CompilerLibrary` so the linker genuinely pulls in LibCompiler (proving the
/// airlift links in-process, not merely that the project reference resolves).
let fns () : List<BuiltInFn> =
  [ { name = fn "compilerInfo" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TString
      description =
        "Reports that the native compiler extension is linked and available. Placeholder for the airlift spike; superseded by the real `compile` surface."
      fn =
        (function
        | _, _, _, [ DUnit ] ->
          // Touch CompilerLibrary so LibCompiler is actually linked.
          let opts = CompilerLibrary.defaultOptions
          let msg =
            $"native compiler linked (DisableFreeList={opts.DisableFreeList}, DisableTCO={opts.DisableTCO})"
          DString msg |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated } ]

let builtins () = LibExecution.Builtin.make [] (fns ())
