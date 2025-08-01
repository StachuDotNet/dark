/// <summary>
/// Supports anything relating to Datastores on a user canvas.
/// Namely, this file is responsible for managing such user data, including CRUD and type-checking.
/// </summary>
///
/// <remarks>
/// User data stores live within the `user_data` table, which is broken down by:
/// - Dark version
/// - canvas (by ID)
/// - table/store ID
/// - user version (for when the users change the type being stored)
/// </remarks>
module LibCloud.UserDB

open System.Threading.Tasks
open FSharp.Control.Tasks
open Microsoft.Data.Sqlite
open Fumble
open LibDB.Db

open Prelude

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module DvalReprInternalQueryable = LibExecution.DvalReprInternalQueryable

// Bump this if you make a breaking change to the underlying data format, and
// are migrating user data to the new version
//
// ! you should definitely notify the entire engineering team about this
let currentDarkVersion = 0

let rec dbToDval
  (types : RT.Types)
  (threadID : RT.ThreadID)
  (tst : RT.TypeSymbolTable)
  (db : RT.DB.T)
  (dbValue : string)
  : Ply<RT.Dval> =
  DvalReprInternalQueryable.parseJsonV0 types threadID tst db.typ dbValue

let dvalToDB
  (threadID : RT.ThreadID)
  (types : RT.Types)
  (dv : RT.Dval)
  : Ply<string> =
  DvalReprInternalQueryable.toJsonStringV0 types threadID dv

let rec set
  (exeState : RT.ExecutionState)
  (threadID : RT.ThreadID)
  (upsert : bool)
  (db : RT.DB.T)
  (key : string)
  (dv : RT.Dval)
  : Ply<Result<uuid, RT.RuntimeError.Error>> =
  uply {
    let id = System.Guid.NewGuid()

    let types = exeState.types
    // CLEANUP: the caller should do this type check instead, but we haven't
    // implemented nested types in the DB yet
    //let context = LibExecution.TypeChecker.DBSchemaType(db.name, db.typ)


    // TODO: should we be passing in a TST?
    match! LibExecution.TypeChecker.unify types Map.empty db.typ dv with
    | Error _errPath ->
      // TODO: include path
      return Error(RT.RuntimeError.DBSetOfWrongType(db.typ, RT.Dval.toValueType dv))

    | Ok _ ->
      let upsertQuery =
        if upsert then
          "ON CONFLICT (canvas_id, table_tlid, dark_version, user_version, key) DO UPDATE SET data=EXCLUDED.data, updated_at=datetime('now')"
        else
          ""

      let! data = dvalToDB threadID types dv

      do!
        Sql.query
          $"INSERT INTO user_data_v0
            (id, canvas_id, table_tlid, user_version, dark_version, key, data, updated_at)
          VALUES
            (@id, @canvasID, @tlid, @userVersion, @darkVersion, @key, @data, datetime('now'))
          {upsertQuery}"
        |> Sql.parameters
          [ "id", Sql.uuid id
            "canvasID", Sql.uuid exeState.program.canvasID
            "tlid", Sql.id db.tlid
            "userVersion", Sql.int db.version
            "darkVersion", Sql.int currentDarkVersion
            "key", Sql.string key
            "data", Sql.string data ]
        |> Sql.executeStatementAsync

      return Ok id
  }



and getOption
  (exeState : RT.ExecutionState)
  (threadID : RT.ThreadID)
  (db : RT.DB.T)
  (key : string)
  : Ply<Option<RT.Dval>> =
  uply {
    let types = exeState.types

    let! result =
      Sql.query
        "SELECT data
          FROM user_data_v0
        WHERE table_tlid = @tlid
          AND canvas_id = @canvasID
          AND user_version = @userVersion
          AND dark_version = @darkVersion
          AND key = @key"
      |> Sql.parameters
        [ "tlid", Sql.tlid db.tlid
          "canvasID", Sql.uuid exeState.program.canvasID
          "userVersion", Sql.int db.version
          "darkVersion", Sql.int currentDarkVersion
          "key", Sql.string key ]
      |> Sql.executeRowOptionAsync (fun read -> read.string "data")

    match result with
    | None -> return None
    | Some dval ->
      let tst = Map.empty // OK?
      return! dbToDval types threadID tst db dval |> Ply.map Some
  }


and getMany
  (exeState : RT.ExecutionState)
  (threadID : RT.ThreadID)
  (tst : RT.TypeSymbolTable)
  (db : RT.DB.T)
  (keys : string list)
  : Ply<List<RT.Dval>> =
  uply {
    let types = exeState.types

    // If no keys, return empty list early
    if List.isEmpty keys then
      return []
    else
      // Create parameters for each key
      let keyParams = keys |> List.mapi (fun i key -> ($"key{i}", Sql.string key))

      // Create placeholders for the SQL query
      let keyPlaceholders =
        keys |> List.mapi (fun i _ -> $"@key{i}") |> String.concat ", "

      // Base parameters that are always needed
      let baseParams =
        [ "tlid", Sql.tlid db.tlid
          "canvasID", Sql.uuid exeState.program.canvasID
          "userVersion", Sql.int db.version
          "darkVersion", Sql.int currentDarkVersion ]

      let! result =
        Sql.query
          $"SELECT data
          FROM user_data_v0
          WHERE table_tlid = @tlid
            AND canvas_id = @canvasID
            AND user_version = @userVersion
            AND dark_version = @darkVersion
            AND key IN ({keyPlaceholders})"
        |> Sql.parameters (baseParams @ keyParams)
        |> Sql.executeAsync (fun read -> read.string "data")

      return! result |> List.map (dbToDval types threadID tst db) |> Ply.List.flatten
  }



and getManyWithKeys
  (exeState : RT.ExecutionState)
  (threadID : RT.ThreadID)
  (tst : RT.TypeSymbolTable)
  (db : RT.DB.T)
  (keys : string list)
  : Ply<List<string * RT.Dval>> =
  uply {
    let types = exeState.types

    // If no keys, return empty list early
    if List.isEmpty keys then
      return []
    else
      // Create parameters for each key
      let keyParams = keys |> List.mapi (fun i key -> ($"key{i}", Sql.string key))

      // Create placeholders for the SQL query
      let keyPlaceholders =
        keys |> List.mapi (fun i _ -> $"@key{i}") |> String.concat ", "

      // Base parameters that are always needed
      let baseParams =
        [ "tlid", Sql.tlid db.tlid
          "canvasID", Sql.uuid exeState.program.canvasID
          "userVersion", Sql.int db.version
          "darkVersion", Sql.int currentDarkVersion ]

      let! result =
        Sql.query
          $"SELECT key, data
          FROM user_data_v0
          WHERE table_tlid = @tlid
            AND canvas_id = @canvasID
            AND user_version = @userVersion
            AND dark_version = @darkVersion
            AND key IN ({keyPlaceholders})"
        |> Sql.parameters (baseParams @ keyParams)
        |> Sql.executeAsync (fun read -> (read.string "key", read.string "data"))

      return!
        result
        |> List.map (fun (key, data) ->
          dbToDval types threadID tst db data |> Ply.map (fun dval -> (key, dval)))
        |> Ply.List.flatten
  }



let getAll
  (exeState : RT.ExecutionState)
  (threadID : RT.ThreadID)
  (tst : RT.TypeSymbolTable)
  (db : RT.DB.T)
  : Ply<List<string * RT.Dval>> =
  uply {
    let! result =
      Sql.query
        "SELECT key, data
        FROM user_data_v0
        WHERE table_tlid = @tlid
          AND canvas_id = @canvasID
          AND user_version = @userVersion
          AND dark_version = @darkVersion"
      |> Sql.parameters
        [ "tlid", Sql.tlid db.tlid
          "canvasID", Sql.uuid exeState.program.canvasID
          "userVersion", Sql.int db.version
          "darkVersion", Sql.int currentDarkVersion ]
      |> Sql.executeAsync (fun read -> (read.string "key", read.string "data"))

    return!
      result
      |> List.map (fun (key, data) ->
        dbToDval exeState.types threadID tst db data
        |> Ply.map (fun dval -> (key, dval)))
      |> Ply.List.flatten
  }

// // Reusable function that provides the template for the SqlCompiler query functions
// let doQuery
//   (exeState : RT.ExecutionState)
//   (db : RT.DB.T)
//   (b : RT.LambdaImpl)
//   (queryFor : string)
//   : Ply<Result<Sql.SqlProps, RT.RuntimeError>> =
//   uply {
//     let paramName =
//       match b.parameters with
//       | { head = RT.LPVariable(_, name); tail = [] } -> name
//       | _ -> Exception.raiseInternal "wrong number of args" [ "args", b.parameters ]

//     let exeState =
//       { exeState with symbolTable = b.symtable; typeSymbolTable = b.typeSymbolTable }

//     let! compiled = SqlCompiler.compileLambda exeState paramName db.typ b.body

//     match compiled with
//     | Error err -> return Error err
//     | Ok compiled ->
//       return
//         Sql.query
//           $"SELECT {queryFor}
//           FROM user_data_v0
//           WHERE table_tlid = @tlid
//             AND canvas_id = @canvasID
//             AND user_version = @userVersion
//             AND dark_version = @darkVersion
//             AND {compiled.sql}"
//         |> Sql.parameters (
//           compiled.vars
//           @ [ "tlid", Sql.tlid db.tlid
//               "canvasID", Sql.uuid exeState.program.canvasID
//               "userVersion", Sql.int db.version
//               "darkVersion", Sql.int currentDarkVersion ]
//         )
//         |> Ok
//   }

// let query
//   (exeState : RT.ExecutionState)
//   (db : RT.DB.T)
//   (b : RT.LambdaImpl)
//   : Ply<Result<List<string * RT.Dval>, RT.RuntimeError>> =
//   uply {
//     let types = RT.ExecutionState.types exeState
//     let! query = doQuery exeState db b "key, data"

//     match query with
//     | Error err -> return (Error err)
//     | Ok query ->

//       let! results =
//         query
//         |> Sql.executeAsync (fun read -> (read.string "key", read.string "data"))

//       return!
//         results
//         |> List.map (fun (key, data) ->
//           uply {
//             let! dval = dbToDval exeState.tracing.callStack types db data
//             return (key, dval)
//           })
//         |> Ply.List.flatten
//         |> Ply.map Ok
//   }

// let queryValues
//   (exeState : RT.ExecutionState)
//   (db : RT.DB.T)
//   (b : RT.LambdaImpl)
//   : Ply<Result<List<RT.Dval>, RT.RuntimeError>> =
//   uply {
//     let types = RT.ExecutionState.types exeState
//     let! query = doQuery exeState db b "data"

//     match query with
//     | Error err -> return Error err
//     | Ok query ->
//       let! results = query |> Sql.executeAsync (fun read -> read.string "data")

//       return!
//         results
//         |> List.map (dbToDval exeState.tracing.callStack types db)
//         |> Ply.List.flatten
//         |> Ply.map Ok
//   }

// let queryCount
//   (exeState : RT.ExecutionState)
//   (db : RT.DB.T)
//   (b : RT.LambdaImpl)
//   : Ply<Result<int, RT.RuntimeError>> =
//   uply {
//     let! query = doQuery exeState db b "COUNT(*)"

//     match query with
//     | Error err -> return Error err
//     | Ok query ->
//       return!
//         query |> Sql.executeRowAsync (fun read -> read.int "count") |> Task.map Ok
//   }

let getAllKeys (exeState : RT.ExecutionState) (db : RT.DB.T) : Task<List<string>> =
  Sql.query
    "SELECT key
    FROM user_data_v0
    WHERE table_tlid = @tlid
      AND canvas_id = @canvasID
      AND user_version = @userVersion
      AND dark_version = @darkVersion"
  |> Sql.parameters
    [ "tlid", Sql.tlid db.tlid
      "canvasID", Sql.uuid exeState.program.canvasID
      "userVersion", Sql.int db.version
      "darkVersion", Sql.int currentDarkVersion ]
  |> Sql.executeAsync (fun read -> read.string "key")

let count (exeState : RT.ExecutionState) (db : RT.DB.T) : Task<int> =
  Sql.query
    "SELECT COUNT(*) as count
    FROM user_data_v0
    WHERE table_tlid = @tlid
      AND canvas_id = @canvasID
      AND user_version = @userVersion
      AND dark_version = @darkVersion"
  |> Sql.parameters
    [ "tlid", Sql.tlid db.tlid
      "canvasID", Sql.uuid exeState.program.canvasID
      "userVersion", Sql.int db.version
      "darkVersion", Sql.int currentDarkVersion ]
  |> Sql.executeRowAsync (fun read -> read.int "count")

let delete
  (exeState : RT.ExecutionState)
  (db : RT.DB.T)
  (key : string)
  : Task<unit> =
  Sql.query
    "DELETE
    FROM user_data_v0
    WHERE key = @key
      AND table_tlid = @tlid
      AND canvas_id = @canvasID
      AND user_version = @userVersion
      AND dark_version = @darkVersion"
  |> Sql.parameters
    [ "key", Sql.string key
      "tlid", Sql.tlid db.tlid
      "canvasID", Sql.uuid exeState.program.canvasID
      "userVersion", Sql.int db.version
      "darkVersion", Sql.int currentDarkVersion ]
  |> Sql.executeStatementAsync

let deleteAll (exeState : RT.ExecutionState) (db : RT.DB.T) : Task<unit> =
  //   covered by idx_user_data_current_data_for_tlid
  Sql.query
    "DELETE FROM user_data_v0
    WHERE canvas_id = @canvasID
      AND table_tlid = @tlid
      AND user_version = @userVersion
      AND dark_version = @darkVersion"
  |> Sql.parameters
    [ "tlid", Sql.tlid db.tlid
      "canvasID", Sql.uuid exeState.program.canvasID
      "userVersion", Sql.int db.version
      "darkVersion", Sql.int currentDarkVersion ]
  |> Sql.executeStatementAsync

// -------------------------
// stats/locked/unlocked (not _locking_)
// -------------------------
// let statsPluck
//   (canvasID : CanvasID)
//   (db : RT.DB.T)
//   : Ply<Option<RT.Dval * string>> =
//   uply {
//     let! result =
//       Sql.query
//         "SELECT data, key
//         FROM user_data_v0
//         WHERE table_tlid = @tlid
//           AND canvas_id = @canvasID
//           AND user_version = @userVersion
//           AND dark_version = @darkVersion
//         ORDER BY created_at DESC
//         LIMIT 1"
//       |> Sql.parameters [ "tlid", Sql.tlid db.tlid
//                           "canvasID", Sql.uuid canvasID
//                           "userVersion", Sql.int db.version
//                           "darkVersion", Sql.int currentDarkVersion ]
//       |> Sql.executeRowOptionAsync (fun read ->
//         (read.string "data", read.string "key"))
//     return result |> Option.map (fun (data, key) -> (dbToDval exeState db data, key))
//   }

let statsCount (canvasID : CanvasID) (db : RT.DB.T) : Task<int> =
  Sql.query
    "SELECT COUNT(*) as count
    FROM user_data_v0
    WHERE table_tlid = @tlid
      AND canvas_id = @canvasID
      AND user_version = @userVersion
      AND dark_version = @darkVersion"
  |> Sql.parameters
    [ "tlid", Sql.tlid db.tlid
      "canvasID", Sql.uuid canvasID
      "userVersion", Sql.int db.version
      "darkVersion", Sql.int currentDarkVersion ]
  |> Sql.executeRowAsync (fun read -> read.int "count")

// Given a [canvasID], return tlids for all unlocked databases -
// a database is unlocked if it has no records, and thus its schema can be
// changed without a migration.

let all (canvasID : CanvasID) : Task<List<tlid>> =
  Sql.query
    "SELECT tlid
    FROM toplevels_v0
    WHERE canvas_id = @canvasID
      AND tipe = 'db'"
  |> Sql.parameters [ "canvasID", Sql.uuid canvasID ]
  |> Sql.executeAsync (fun read -> read.tlid "tlid")


let unlocked (canvasID : CanvasID) : Task<List<tlid>> =
  // this will need to be fixed when we allow migrations
  // Note: tl.module IS NULL means it's a db; anything else will be
  // HTTP/REPL/CRON/WORKER
  // NOTE: the line `AND tl.account_id = ud.account_id` seems redunant, but
  // it's required to hit the index

  // CLEANUP: do we need table_tlid IS NULL since we're using NOT NULL in the schema?
  Sql.query
    "SELECT tl.tlid
    FROM toplevels_v0 as tl
    LEFT JOIN user_data_v0 as ud
      ON tl.tlid = ud.table_tlid
        AND tl.canvas_id = ud.canvas_id
    WHERE tl.canvas_id = @canvasID
      AND tl.module IS NULL
      AND tl.deleted = 0
      AND ud.table_tlid IS NULL
    GROUP BY tl.tlid"
  |> Sql.parameters [ "canvasID", Sql.uuid canvasID ]
  |> Sql.executeAsync (fun read -> read.tlid "tlid")



// -------------------------
// DB schema
// -------------------------

let create (tlid : tlid) (name : string) (typ : PT.TypeReference) : PT.DB.T =
  { tlid = tlid; name = name; typ = typ; version = 0 }

let renameDB (n : string) (db : PT.DB.T) : PT.DB.T = { db with name = n }
