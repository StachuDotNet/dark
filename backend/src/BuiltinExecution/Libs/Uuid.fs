module BuiltinExecution.Libs.Uuid

open System.Threading.Tasks
open System.Numerics
open FSharp.Control.Tasks

open LibExecution.RuntimeTypes
open Prelude
open LibExecution.Builtin.Shortcuts
module VT = ValueType
module Dval = LibExecution.Dval

let types : List<BuiltInType> = []
let constants : List<BuiltInConstant> = []

let fn = fn [ "Uuid" ]

let fns : List<BuiltInFn> =
  [ { name = fn "generate" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TUuid
      description = "Generate a new <type Uuid> v4 according to RFC 4122"
      fn =
        (function
        | _, _, [ DUnit ] -> Ply(DUuid(System.Guid.NewGuid()))
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplemented
      // similarly to DateTime.now, it's not particularly fun for this to change
      // when live programming
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "parse" 0
      typeParams = []
      parameters = [ Param.make "uuid" TString "" ]
      returnType = TypeReference.result TUuid TString
      description =
        "Parse a <type Uuid> of form {{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}}"
      fn =
        (function
        | state, _, [ DString s ] ->
          let types = ExecutionState.availableTypes state
          let resultOk = Dval.resultOk types VT.uuid VT.string
          let resultError = Dval.resultError types VT.uuid VT.string

          match System.Guid.TryParse s with
          | true, x -> x |> DUuid |> resultOk
          | _ ->
            "`uuid` parameter was not of form XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX"
            |> DString
            |> resultError
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplemented
      previewable = Pure
      deprecated = NotDeprecated }


    { name = fn "toString" 0
      typeParams = []
      parameters = [ Param.make "uuid" TUuid "" ]
      returnType = TString
      description =
        "Stringify <param uuid> to the format XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX"
      fn =
        (function
        | _, _, [ DUuid uuid ] -> Ply(DString(string uuid))
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }

    ]

let contents = (fns, types, constants)
