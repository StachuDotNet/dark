module LibPackageManager.SessionStorage

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.ProgramTypes

open Microsoft.Data.Sqlite
open Fumble
open LibDB.Db

module PT = LibExecution.ProgramTypes

// Session operations
module Session =
  let save (session: PT.Session.T) : Ply<Result<unit, string>> =
    uply {
      try
        let! _ =
          Sql.query
            """
            INSERT INTO matter_sessions_v0 (id, name, intent, owner, current_patch_id, state, workspace_state, started_at, last_active_at)
            VALUES (@id, @name, @intent, @owner, @currentPatch, @state, @workspace, @startedAt, @lastActiveAt)
            ON CONFLICT (id) DO UPDATE SET
              name = @name,
              intent = @intent,
              current_patch_id = @currentPatch,
              state = @state,
              workspace_state = @workspace,
              last_active_at = @lastActiveAt
            """
          |> Sql.parameters
            [ "id", Sql.uuid session.id
              "name", Sql.string session.name
              "intent", Sql.string session.intent
              "owner", Sql.string session.owner
              "currentPatch",
                match session.currentPatch with
                | Some id -> Sql.uuid id
                | None -> Sql.dbnull
              "state",
                Sql.string (
                  match session.state with
                  | PT.SessionState.Active -> "Active"
                  | PT.SessionState.Suspended -> "Suspended"
                  | PT.SessionState.Completed -> "Completed"
                )
              "workspace", Sql.bytes [||] // TODO: Serialize workspace
              "startedAt", Sql.string (session.startedAt.ToString("o"))
              "lastActiveAt", Sql.string (session.lastActiveAt.ToString("o")) ]
          |> Sql.executeNonQueryAsync
        return Ok ()
      with
      | e -> return Error $"Failed to save session: {e.Message}"
    }

  let get (sessionId: uuid) : Ply<Option<PT.Session.T>> =
    uply {
      return!
        Sql.query
          """
          SELECT id, name, intent, owner, current_patch_id, state, workspace_state, started_at, last_active_at
          FROM matter_sessions_v0
          WHERE id = @id
          """
        |> Sql.parameters [ "id", Sql.uuid sessionId ]
        |> Sql.executeRowOptionAsync (fun read ->
          { PT.Session.id = read.uuid "id"
            name = read.string "name"
            intent = read.string "intent"
            owner = read.string "owner"
            patches = [] // TODO: Load from join table
            currentPatch =
              try
                Some (read.uuid "current_patch_id")
              with
              | _ -> None
            startedAt = read.dateTime "started_at"
            lastActiveAt = read.dateTime "last_active_at"
            state =
              match read.string "state" with
              | "Active" -> PT.SessionState.Active
              | "Suspended" -> PT.SessionState.Suspended
              | "Completed" -> PT.SessionState.Completed
              | _ -> PT.SessionState.Active
            workspace = PT.WorkspaceState.empty }) // TODO: Deserialize
    }

// Patch operations
module Patch =
  let save (patch: PT.Patch.T) : Ply<Result<unit, string>> =
    uply {
      try
        let! _ =
          Sql.query
            """
            INSERT INTO matter_patches_v0 (id, intent, author, status, created_at, updated_at, metadata)
            VALUES (@id, @intent, @author, @status, @createdAt, @updatedAt, @metadata)
            ON CONFLICT (id) DO UPDATE SET
              intent = @intent,
              status = @status,
              updated_at = @updatedAt,
              metadata = @metadata
            """
          |> Sql.parameters
            [ "id", Sql.uuid patch.id
              "intent", Sql.string patch.intent
              "author", Sql.string patch.author
              "status",
                Sql.string (
                  match patch.status with
                  | PT.PatchStatus.Draft -> "Draft"
                  | PT.PatchStatus.Ready -> "Ready"
                  | PT.PatchStatus.Applied -> "Applied"
                  | PT.PatchStatus.Rejected -> "Rejected"
                )
              "createdAt", Sql.string (patch.createdAt.ToString("o"))
              "updatedAt", Sql.string (patch.updatedAt.ToString("o"))
              "metadata", Sql.bytes [||] ] // TODO: Serialize metadata
          |> Sql.executeNonQueryAsync
        return Ok ()
      with
      | e -> return Error $"Failed to save patch: {e.Message}"
    }

  let get (patchId: uuid) : Ply<Option<PT.Patch.T>> =
    uply {
      return!
        Sql.query
          """
          SELECT id, intent, author, status, created_at, updated_at, metadata
          FROM matter_patches_v0
          WHERE id = @id
          """
        |> Sql.parameters [ "id", Sql.uuid patchId ]
        |> Sql.executeRowOptionAsync (fun read ->
          { PT.Patch.id = read.uuid "id"
            intent = read.string "intent"
            ops = [] // TODO: Load from ops table
            parentPatches = [] // TODO: Load from dependencies table
            status =
              match read.string "status" with
              | "Draft" -> PT.PatchStatus.Draft
              | "Ready" -> PT.PatchStatus.Ready
              | "Applied" -> PT.PatchStatus.Applied
              | "Rejected" -> PT.PatchStatus.Rejected
              | _ -> PT.PatchStatus.Draft
            author = read.string "author"
            createdAt = read.dateTime "created_at"
            updatedAt = read.dateTime "updated_at"
            metadata =
              { PT.Patch.todos = []
                tags = []
                testsCovered = [] }}) // TODO: Deserialize
    }

// Op operations
module Op =
  let saveOp (patchId: uuid) (sequenceNum: int) (op: PT.Op.T) : Ply<Result<unit, string>> =
    uply {
      try
        let opType =
          match op with
          | PT.Op.AddFunctionContent _ -> "AddFunctionContent"
          | PT.Op.AddTypeContent _ -> "AddTypeContent"
          | PT.Op.AddValueContent _ -> "AddValueContent"
          | PT.Op.CreateName _ -> "CreateName"
          | PT.Op.UpdateNamePointer _ -> "UpdateNamePointer"
          | PT.Op.MoveName _ -> "MoveName"
          | PT.Op.UnassignName _ -> "UnassignName"
          | PT.Op.DeprecateContent _ -> "DeprecateContent"

        let! _ =
          Sql.query
            """
            INSERT INTO matter_ops_v0 (patch_id, sequence_num, op_type, op_data)
            VALUES (@patchId, @seqNum, @opType, @opData)
            """
          |> Sql.parameters
            [ "patchId", Sql.uuid patchId
              "seqNum", Sql.int sequenceNum
              "opType", Sql.string opType
              "opData", Sql.bytes [||] ] // TODO: Serialize op
          |> Sql.executeNonQueryAsync
        return Ok ()
      with
      | e -> return Error $"Failed to save op: {e.Message}"
    }

  let getOpsForPatch (patchId: uuid) : Ply<List<PT.Op.T>> =
    uply {
      let! ops =
        Sql.query
          """
          SELECT sequence_num, op_type, op_data
          FROM matter_ops_v0
          WHERE patch_id = @patchId
          ORDER BY sequence_num
          """
        |> Sql.parameters [ "patchId", Sql.uuid patchId ]
        |> Sql.executeAsync (fun _read ->
          // TODO: Deserialize op_data
          PT.Op.CreateName(
            PT.PackageLocation.create "" [] "",
            "",
            ""
          )) // Placeholder
      return ops
    }