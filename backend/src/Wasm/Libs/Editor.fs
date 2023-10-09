/// Builtin for handling JS-WASM interactions via WASM'd Darklang code
module Wasm.Libs.Editor

open System

open Prelude

open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts
open Wasm.EvalHelpers

module VT = ValueType
module TypeChecker = LibExecution.TypeChecker
module Dval = LibExecution.Dval

let types : List<BuiltInType> = []
let constants : List<BuiltInConstant> = []


type Editor =
  { Types : List<UserType.T>
    Functions : List<UserFunction.T>
    Constants : List<UserConstant.T>
    CurrentState : Dval }


/// A "user program" that can be executed by the interpreter
/// (probably generated by AI).
type UserProgramSource =
  { types : List<UserType.T>
    fns : List<UserFunction.T>
    constants : List<UserConstant.T>
    exprs : List<Expr> }

// this is client.dark, loaded and live, along with some current state
let mutable editor : Editor =
  { Types = []; Functions = []; Constants = []; CurrentState = DUnit }

let fn = fn [ "WASM"; "Editor" ]

let fns : List<BuiltInFn> =
  [ { name = fn "getState" 0
      typeParams = [ "state" ]
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TypeReference.result (TVariable "a") TString
      description =
        "Get the editor's global current state (maintained in the WASM runtime)"
      fn =
        let okType = VT.unknownTODO
        let resultOk = Dval.checkedResultOk okType VT.string
        let resultError = Dval.checkedResultOk okType VT.string
        (function
        | _, [ _typeParam ], [ DUnit ] ->
          try
            let state = editor.CurrentState
            // TODO: assert that the type matches the given typeParam
            resultOk state |> Ply
          with e ->
            $"Error getting state: {e.Message}" |> DString |> resultError |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "setState" 0
      typeParams = [ "a" ]
      parameters = [ Param.make "state" (TVariable "a") "" ]
      returnType = TypeReference.result (TVariable "a") TString
      description =
        "Set the editor's global current state (maintained in the WASM runtime)"
      fn =
        (function
        | _, [ _typeParam ], [ v ] ->
          // TODO: verify that the type matches the given typeParam
          editor <- { editor with CurrentState = v }
          Dval.checkedResultOk VT.unknownTODO VT.string v |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "callJSFunction" 0
      typeParams = []
      parameters =
        [ Param.make "functionName" TString ""
          Param.make "args" (TList TString) "" ]
      returnType = TypeReference.result TUnit TString
      description =
        "Calls a JS function with the given args.
        Note: this will throw an exception if the function doesn't exist in the webworker that hosts the Dark runtime"
      fn =
        let resultOk = Dval.resultOk KTUnit KTString
        let resultError = Dval.resultError KTUnit KTString
        (function
        | _, _, [ DString functionName; DList(_, args) ] ->
          let args =
            args
            |> List.fold
              (fun agg item ->
                match agg, item with
                | (Error err, _) -> Error err
                | (Ok l, DString arg) -> Ok(arg :: l)
                | (_, notAString) ->
                  // CLEANUP this should be an RTE, not a "normal" error
                  $"Expected args to be a `List<String>`, but got: {LibExecution.DvalReprDeveloper.toRepr notAString}"
                  |> Error)
              (Ok [])
            |> Result.map (fun pairs -> List.rev pairs)

          match args with
          | Ok args ->
            try
              do Wasm.WasmHelpers.callJSFunction functionName args
              resultOk DUnit |> Ply
            with e ->
              $"Error calling {functionName} with provided args: {e.Message}"
              |> DString
              |> resultError
              |> Ply
          | Error err -> resultError (DString err) |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "evalUserProgram" 0
      typeParams = []
      parameters = [ Param.make "program" TString "" ]
      returnType = TypeReference.result TString TString
      description = "Eval a user program (probably generated by AI)"
      fn =
        let resultOk = Dval.resultOk KTString KTString
        let resultError = Dval.resultError KTString KTString
        (function
        | _, _, [ DString sourceJson ] ->
          uply {
            let source = Json.Vanilla.deserialize<UserProgramSource> sourceJson
            let tlid = 77777723978322UL

            let httpConfig : BuiltinExecution.Libs.HttpClient.Configuration =
              { BuiltinExecution.Libs.HttpClient.defaultConfig with
                  telemetryAddException =
                    (fun metadata e ->
                      Wasm.WasmHelpers.callJSFunction
                        "console.warn"
                        [ string metadata; string e ]) }

            let builtin =
              LibExecution.Builtin.combine
                [ BuiltinExecution.Builtin.contents httpConfig ]
                []
                []

            let! result =
              let expr = exprsCollapsedIntoOne source.exprs
              let state =
                getStateForEval builtin source.types source.fns source.constants
              let inputVars = Map.empty
              LibExecution.Execution.executeExpr state tlid inputVars expr

            match result with
            | Error(_source, rte) ->
              // TODO probably need to call `toString` on the RTE, or raise it
              return resultError (DString(string rte))
            | Ok result ->
              return
                LibExecution.DvalReprDeveloper.toRepr result |> DString |> resultOk
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }

    ]

let contents = (fns, types, constants)
