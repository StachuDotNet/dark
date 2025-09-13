/// Builtin functions for developer collaboration in CLI
module BuiltinCliHost.Libs.DevCollab

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module Errors = LibExecution.Errors

let fns : List<BuiltInFn> =
  [ { name = fn "devCollabInitDb" 0
      typeParams = []
      parameters = []
      returnType = TUnit
      description = "Initialize the collaboration database schema"
      fn =
        (function
        | _, _, _, [] ->
          uply {
            do! LibPackageManager.DevCollabDb.initSchema ()
            return DUnit
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }

    { name = fn "devCollabGetCurrentUser" 0
      typeParams = []
      parameters = []
      returnType = TOption TString
      description = "Get the currently authenticated user ID"
      fn =
        (function
        | _, _, _, [] ->
          uply {
            let! userOpt = LibPackageManager.DevCollabDb.getCurrentUser ()
            return
              match userOpt with
              | Some userId -> DOption(ValueType.Known KTString, Some(DString userId))
              | None -> DOption(ValueType.Known KTString, None)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }

    { name = fn "devCollabCreatePatch" 0
      typeParams = []
      parameters = [ Param.make "author" TString ""; Param.make "intent" TString "" ]
      returnType = TString
      description = "Create a new patch with the given author and intent"
      fn =
        (function
        | _, _, _, [ DString author; DString intent ] ->
          uply {
            let patch = LibPackageManager.DevCollab.Patch.create author intent
            do! LibPackageManager.DevCollabDb.savePatch patch
            return DString(patch.id.ToString())
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }

    { name = fn "devCollabLoadPatches" 0
      typeParams = []
      parameters = []
      returnType = TList(TString)
      description = "Load all patches from the database"
      fn =
        (function
        | _, _, _, [] ->
          uply {
            let! patches = LibPackageManager.DevCollabDb.loadPatches ()
            let patchIds = 
              patches 
              |> List.map (fun p -> DString(p.id.ToString()))
            return Dval.list KTString patchIds
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }

    { name = fn "devCollabGetPatchInfo" 0
      typeParams = []
      parameters = [ Param.make "patchId" TString "" ]
      returnType = TOption(TDict TString)
      description = "Get patch information as a dictionary"
      fn =
        (function
        | _, _, _, [ DString patchIdStr ] ->
          uply {
            match System.Guid.TryParse patchIdStr with
            | true, patchId ->
              let! patchOpt = LibPackageManager.DevCollabDb.loadPatchById patchId
              return
                match patchOpt with
                | Some patch ->
                  let patchDict = 
                    Map.ofList [
                      ("id", DString(patch.id.ToString()))
                      ("author", DString patch.author)
                      ("intent", DString patch.intent)
                      ("status", DString(match patch.status with
                                         | LibPackageManager.DevCollab.Draft -> "draft"
                                         | LibPackageManager.DevCollab.Ready -> "ready"
                                         | LibPackageManager.DevCollab.Applied -> "applied"
                                         | LibPackageManager.DevCollab.Rejected -> "rejected"))
                      ("createdAt", DString(patch.createdAt.ToString("yyyy-MM-dd HH:mm:ss")))
                      ("opsCount", DInt64(List.length patch.ops |> int64))
                    ]
                  DOption(ValueType.Known(KTDict(ValueType.Known KTString)), Some(DDict(ValueType.Known KTString, patchDict)))
                | None ->
                  DOption(ValueType.Known(KTDict(ValueType.Known KTString)), None)
            | false, _ ->
              return DOption(ValueType.Known(KTDict(ValueType.Known KTString)), None)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }

    { name = fn "devCollabCreateSession" 0
      typeParams = []
      parameters = [ Param.make "owner" TString ""; Param.make "name" TString ""; Param.make "intent" TString "" ]
      returnType = TString
      description = "Create a new session"
      fn =
        (function
        | _, _, _, [ DString owner; DString name; DString intent ] ->
          uply {
            let session = LibPackageManager.DevCollab.Session.create owner name intent
            do! LibPackageManager.DevCollabDb.saveSession session
            return DString(session.id.ToString())
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }

    { name = fn "devCollabGetCurrentSession" 0
      typeParams = []
      parameters = [ Param.make "userId" TString "" ]
      returnType = TOption(TDict TString)
      description = "Get current active session for a user"
      fn =
        (function
        | _, _, _, [ DString userId ] ->
          uply {
            let! sessionOpt = LibPackageManager.DevCollabDb.loadCurrentSession userId
            return
              match sessionOpt with
              | Some session ->
                let sessionDict = 
                  Map.ofList [
                    ("id", DString(session.id.ToString()))
                    ("name", DString session.name)
                    ("intent", DString session.intent)
                    ("owner", DString session.owner)
                    ("state", DString(match session.state with
                                      | LibPackageManager.DevCollab.Active -> "active"
                                      | LibPackageManager.DevCollab.Suspended -> "suspended"
                                      | LibPackageManager.DevCollab.Completed -> "completed"))
                    ("startedAt", DString(session.startedAt.ToString("yyyy-MM-dd HH:mm:ss")))
                  ]
                DOption(ValueType.Known(KTDict(ValueType.Known KTString)), Some(DDict(ValueType.Known KTString, sessionDict)))
              | None ->
                DOption(ValueType.Known(KTDict(ValueType.Known KTString)), None)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]

let builtins = LibExecution.Builtin.make [] fns []