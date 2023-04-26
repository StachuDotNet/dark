/// Standard libraries for Files, Directories, and other OS/file system stuff
module Wasm.LibWASM

open System
open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Tablecloth

open LibExecution.RuntimeTypes

open LibExecution.StdLib.Shortcuts

module PT = LibExecution.ProgramTypes
module Exe = LibExecution.Execution

let types : List<BuiltInType> = []

let fns : List<BuiltInFn> =
  [ { name = fn "WASM" "callJSFunction" 0
      typeParams = []
      parameters =
        [ Param.make "functionName" TString ""
          Param.make "serializedArgs" TString "" ]
      returnType = TResult(TUnit, TString)
      description = "TODO"
      fn =
        (function
        | _, _, [ DString functionName; DString args ] ->
          // TODO: I'm not really sure what to do about `args`. Maybe it should be an object to serialize?
          // or have an additiol function like callJSFunctionTyped<T>(functionName: string, args: T)
          uply {
            try
              WasmHelpers.postMessage functionName args
              return DResult(Ok DUnit)
            with
            | e -> return DResult(Error(DString($"Error: {e.Message}")))
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]
