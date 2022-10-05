/// API endpoints related to DBs
module ApiServer.DBs

open System.Threading.Tasks
open FSharp.Control.Tasks
open Microsoft.AspNetCore.Http

open Prelude
open Tablecloth
open Http

module PT = LibExecution.ProgramTypes
module AT = LibExecution.AnalysisTypes
module CRT = ClientTypes.Runtime

module Stats = LibBackend.Stats
module Canvas = LibBackend.Canvas
module RT = LibExecution.RuntimeTypes
module TI = LibBackend.TraceInputs
module Telemetry = LibService.Telemetry

module Unlocked =
  type T = { unlocked_dbs : tlid list }

  /// API endpoint to fetch a list of unlocked User DBs
  ///
  /// A 'locked' database cannot have its fields/types changed
  let get (ctx : HttpContext) : Task<T> =
    task {
      use t = startTimer "read-api" ctx
      let canvasInfo = loadCanvasInfo ctx

      t.next "getUnlocked"
      let! unlocked = LibBackend.UserDB.unlocked canvasInfo.owner canvasInfo.id

      return { unlocked_dbs = unlocked }
    }

module DBStatsV1 =
  type Params = { tlids : tlid list }

  type Stat = { count : int; example : Option<CRT.Dval.T * string> }

  type T = Map<string, Stat>

  /// API endpoint to get statistical data regarding User DBs
  let getStats (ctx : HttpContext) : Task<T> =
    task {
      use t = startTimer "read-api" ctx
      let canvasInfo = loadCanvasInfo ctx
      let! p = ctx.ReadVanillaJsonAsync<Params>()
      Telemetry.addTags [ "tlids", p.tlids ]

      t.next "load-canvas"
      let! c = Canvas.loadAllDBs canvasInfo

      t.next "load-db-stats"
      let! result = Stats.dbStats c p.tlids

      t.next "write-api"
      let (result : T) =
        result
        |> Map.toList
        |> List.map (fun (k, (s : Stats.DBStat)) ->
          (string k),
          { count = s.count
            example =
              Option.map (fun (dv, s) -> (CRT.Dval.fromRT dv, s)) s.example })
        |> Map

      return result
    }
