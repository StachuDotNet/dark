/// StdLib functions for building Dark functionality via Dark canvases
module StdLibDarkInternal.Libs.Canvases

open System.Threading.Tasks

open Npgsql.FSharp
open LibBackend.Db

open Prelude

open LibExecution.RuntimeTypes

module PT = LibExecution.ProgramTypes
module DvalReprDeveloper = LibExecution.DvalReprDeveloper
module Errors = LibExecution.Errors
module Telemetry = LibService.Telemetry

open LibBackend
module SchedulingRules = LibBackend.QueueSchedulingRules

let fn = FQFnName.stdlibFnName
let typ = FQTypeName.stdlibTypeName

let incorrectArgs = LibExecution.Errors.incorrectArgs

let varA = TVariable "a"
let varB = TVariable "b"


let types : List<BuiltInType> =
  [ { name = typ "DarkInternal" "F404" 0
      typeParams = []
      definition =
        CustomType.Record(
          { id = 1UL; name = "space"; typ = TString },
          [ { id = 1UL; name = "path"; typ = TString }
            { id = 1UL; name = "modifier"; typ = TString }
            { id = 1UL; name = "timestamp"; typ = TDateTime }
            { id = 1UL; name = "traceID"; typ = TUuid } ]
        )
      description = "404 record" } ]


let fns : List<BuiltInFn> =
  [ { name = fn "DarkInternal" "delete404" 0
      typeParams = []
      parameters =
        [ Param.make "canvasID" TUuid ""
          Param.make "space" TString ""
          Param.make "path" TString ""
          Param.make "modifier" TString "" ]
      returnType = TUnit
      description = "Deletes a specific 404 for a canvas"
      fn =
        (function
        | _, _, [ DUuid canvasID; DString space; DString path; DString modifier ] ->
          uply {
            Telemetry.addTags [ "space", space; "path", path; "modifier", modifier ]
            do! TraceInputs.delete404s canvasID space path modifier
            return DUnit
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "getRecent404s" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid "" ]
      returnType =
        TList(TCustomType(FQTypeName.Stdlib(typ "DarkInternal" "F404" 0), []))
      description = "Fetch a list of recent 404s"
      fn =
        (function
        | _, _, [ DUuid canvasID ] ->
          uply {
            let! f404s = TraceInputs.getRecent404s canvasID
            return
              f404s
              |> List.map (fun (space, path, modifier, instant, traceID) ->
                [ "space", DString space
                  "path", DString path
                  "modifier", DString modifier
                  "timestamp", DDateTime(DarkDateTime.fromInstant instant)
                  "traceID", DUuid(LibExecution.AnalysisTypes.TraceID.toUUID traceID) ]
                |> Map
                |> DRecord)
              |> DList
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]
