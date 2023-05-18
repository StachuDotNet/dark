/// StdLib for handling JS-WASM interactions via WASM'd Darklang code
module Wasm.Libs.Editor

open System

open Prelude
open Tablecloth

open LibExecution.RuntimeTypes
open LibExecution.StdLib.Shortcuts

let types : List<BuiltInType> = []


type Editor =
  { Types : List<UserType.T>
    Functions : List<UserFunction.T>
    CurrentState : Dval }

// this is client.dark, loaded and live, along with some current state
let mutable editor : Editor = { Types = []; Functions = []; CurrentState = DUnit }


// TODO: throw these fns in the "WASM.Editor" module once parsing works
let fns : List<BuiltInFn> =
  [ { name = fn' [ "WASM"; "Editor" ] "getState" 0
      typeParams = [ "state" ]
      parameters = []
      returnType = TResult(TVariable "a", TString)
      description = "TODO"
      fn =
        (function
        | _, [ _typeParam ], [] ->
          uply {
            let state = editor.CurrentState
            // TODO: assert that the type matches the given typeParam
            return DResult(Ok state)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn' [ "WASM"; "Editor" ] "setState" 0
      typeParams = [ "a" ]
      parameters = [ Param.make "state" (TVariable "a") "" ]
      returnType = TResult(TVariable "a", TString)
      description = "TODO"
      fn =
        (function
        | _, [ _typeParam ], [ v ] ->
          uply {
            // TODO: verify that the type matches the given typeParam
            editor <- { editor with CurrentState = v }
            return DResult(Ok v)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn' [ "WASM"; "Editor" ] "callJSFunction" 0
      typeParams = []
      parameters =
        [ Param.make "functionName" TString ""
          Param.make "args" (TList TString) "" ]
      returnType = TResult(TUnit, TString)
      description = "Calls a globally-accessible JS function with the given args"
      fn =
        (function
        | _, _, [ DString functionName; DList args ] ->
          let args =
            args
            |> List.fold (Ok []) (fun agg item ->
              match agg, item with
              | (Error err, _) -> Error err
              | (Ok l, DString arg) -> Ok(arg :: l)
              | (_, notAString) ->
                // this should be a DError, not a "normal" error
                $"Expected args to be a `List<String>`, but got: {LibExecution.DvalReprDeveloper.toRepr notAString}"
                |> Error)
            |> Result.map (fun pairs -> List.rev pairs)

          match args with
          | Ok args ->
            uply {
              try
                do Wasm.WasmHelpers.callJSFunction functionName args
                return DResult(Ok DUnit)
              with
              | e ->
                return
                  $"Error calling {functionName} with provided args: {e.Message}"
                  |> DString
                  |> Error
                  |> DResult
            }
          | Error err -> Ply(DResult(Error(DString err)))
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]

let contents = (fns, types)
