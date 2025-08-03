module LibPackageManager.Purge

open System.Threading.Tasks
open FSharp.Control.Tasks
open System.Collections.Concurrent

open Prelude

open Microsoft.Data.Sqlite
open Fumble
open LibDB.Db

let purge () : Task<unit> =
  task {
    [ "DELETE FROM package_types_v0"
      "DELETE FROM package_constants_v0"
      "DELETE FROM package_functions_v0" ]
    |> List.map (fun sql -> (sql, [ [] ]))
    |> Sql.executeTransactionSync
    |> ignore<List<int>>
    
    // Force WAL checkpoint to flush changes and clean up WAL files
    // If this fails, it's not critical - the main deletes already succeeded
    try
      Sql.query "PRAGMA wal_checkpoint(TRUNCATE)"
      |> Sql.executeNonQuery
      |> Result.unwrap
      |> ignore<int>
    with
    | _ -> () // Ignore WAL checkpoint errors
  }
