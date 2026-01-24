/// Builtins for type-based value discovery
module BuiltinCli.Libs.Discovery

open System
open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.RuntimeTypes
module VT = LibExecution.ValueType
module Dval = LibExecution.Dval
module Builtin = LibExecution.Builtin
module Execution = LibExecution.Execution
module PackageIDs = LibExecution.PackageIDs
module PT = LibExecution.ProgramTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module PM = LibPackageManager.RuntimeTypes
open Builtin.Shortcuts


/// Get the type name from a Dval (for DRecord or DEnum)
let getTypeName (dval : Dval) : Option<FQTypeName.FQTypeName> =
  match dval with
  | DRecord(typeName, _, _, _) -> Some typeName
  | DEnum(typeName, _, _, _, _) -> Some typeName
  | _ -> None


let fns : List<BuiltInFn> =
  [ // Evaluate a package value by its UUID
    { name = fn "cliEvaluatePackageValue" 0
      typeParams = []
      parameters = [ Param.make "valueId" TUuid "UUID of the package value to evaluate" ]
      returnType = TypeReference.option (TVariable "a")
      description =
        "Evaluates a package value by its UUID and returns the result. " +
        "Returns None if the value doesn't exist or fails to evaluate."
      fn =
        (function
        | exeState, _, _, [ DUuid valueId ] ->
          uply {
            // Use LoadValue instruction to evaluate the package value
            let valueName = FQValueName.Package valueId
            let instrs : Instructions =
              { registerCount = 1
                instructions = [ LoadValue(0, valueName) ]
                resultIn = 0 }

            let! result = Execution.executeExpr exeState instrs
            match result with
            | Ok dval ->
              // Get the type from the actual value
              match Dval.toValueType dval with
              | ValueType.Known kt -> return Dval.optionSome kt dval
              | ValueType.Unknown -> return Dval.optionSome KTUnit dval // Fallback
            | Error _ -> return Dval.optionNone KTUnit // Fallback type for None
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // Check if a Dval's type matches a target type UUID
    { name = fn "cliValueMatchesType" 0
      typeParams = []
      parameters =
        [ Param.make "value" (TVariable "a") "The value to check"
          Param.make "typeId" TUuid "UUID of the type to match against" ]
      returnType = TBool
      description =
        "Returns true if the value's type matches the given type UUID. " +
        "Works with records and enums."
      fn =
        (function
        | _, _, _, [ dval; DUuid typeId ] ->
          let targetTypeName = FQTypeName.Package typeId
          let matches =
            match getTypeName dval with
            | Some typeName -> typeName = targetTypeName
            | None -> false
          Ply(DBool matches)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }


    // Get the type ID of a value (if it has one)
    { name = fn "cliGetValueTypeId" 0
      typeParams = []
      parameters = [ Param.make "value" (TVariable "a") "The value to get the type ID from" ]
      returnType = TypeReference.option TUuid
      description =
        "Returns the type UUID of a value if it's a record or enum, None otherwise."
      fn =
        (function
        | _, _, _, [ dval ] ->
          let result =
            match getTypeName dval with
            | Some(FQTypeName.Package typeId) -> Dval.optionSome KTUuid (DUuid typeId)
            | None -> Dval.optionNone KTUuid
          Ply(result)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }


    // Find all value IDs that have a specific type
    { name = fn "cliFindValueIdsByType" 0
      typeParams = []
      parameters = [ Param.make "typeId" TUuid "UUID of the type to search for" ]
      returnType = TList TUuid
      description =
        "Returns a list of value UUIDs that have the given type. " +
        "Uses the indexed value_type_id column for efficient lookup."
      fn =
        (function
        | _, _, _, [ DUuid typeId ] ->
          uply {
            let! valueIds = PM.Value.findByTypeId typeId
            return DList(VT.uuid, valueIds |> List.map DUuid)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // Find and evaluate all values of a specific type
    { name = fn "cliFindValuesByType" 0
      typeParams = []
      parameters = [ Param.make "typeId" TUuid "UUID of the type to search for" ]
      returnType = TList (TVariable "a")
      description =
        "Finds all values with the given type and evaluates them. " +
        "Returns a list of the evaluated values."
      fn =
        (function
        | exeState, _, _, [ DUuid typeId ] ->
          uply {
            // First find all value IDs with this type
            let! valueIds = PM.Value.findByTypeId typeId

            // Then evaluate each one
            let! results =
              valueIds
              |> List.map (fun valueId ->
                uply {
                  let valueName = FQValueName.Package valueId
                  let instrs : Instructions =
                    { registerCount = 1
                      instructions = [ LoadValue(0, valueName) ]
                      resultIn = 0 }
                  return! Execution.executeExpr exeState instrs
                })
              |> Ply.List.flatten

            // Filter to only successful evaluations
            let successfulValues =
              results
              |> List.choose (function
                | Ok dval -> Some dval
                | Error _ -> None)

            // Determine the value type from the first result, or use Unknown
            let vt =
              match successfulValues with
              | first :: _ -> Dval.toValueType first
              | [] -> VT.unknown

            return DList(vt, successfulValues)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]


let builtins : Builtins = Builtin.make [] fns
