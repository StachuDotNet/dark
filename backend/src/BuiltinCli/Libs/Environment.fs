/// Standard libraries for Environment Variables
module BuiltinCli.Libs.Environment

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.RuntimeTypes

module VT = ValueType
module Dval = LibExecution.Dval
module Builtin = LibExecution.Builtin
open Builtin.Shortcuts

let types : List<BuiltInType> = []
let constants : List<BuiltInConstant> = []

let fn = fn [ "Environment" ]

let fns : List<BuiltInFn> =
  [ { name = fn "get" 0
      typeParams = []
      parameters = [ Param.make "varName" TString "" ]
      returnType = TypeReference.option TString
      description =
        "Gets the value of the environment variable with the given <param varName> if it exists."
      fn =
        (function
        | state, _, [ DString varName ] ->
          let types = ExecutionState.availableTypes state
          let optionNone = Dval.optionNone types VT.string
          let optionSome = Dval.optionSome types VT.string

          let envValue = System.Environment.GetEnvironmentVariable(varName)

          if isNull envValue then optionNone else optionSome (DString envValue)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "getAll" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TDict TString
      description =
        "Returns a list of tuples containing all the environment variables and their values."
      fn =
        (function
        | _, _, [ DUnit ] ->
          let envVars = System.Environment.GetEnvironmentVariables()

          let envMap =
            envVars
            |> Seq.cast<System.Collections.DictionaryEntry>
            |> Seq.map (fun kv -> (string kv.Key, DString(string kv.Value)))
            |> Seq.toList
            |> Dval.dict VT.unknownTODO

          Ply(envMap)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]


let contents : Builtin.Contents = (fns, types, constants)
