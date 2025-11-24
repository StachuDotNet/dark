module internal LibPackageManager.PT.SQL.Values

open System.Threading.Tasks
open FSharp.Control.Tasks
open System.Collections.Concurrent

open Prelude

open Microsoft.Data.Sqlite
open Fumble
open LibDB.Db

module PT = LibExecution.ProgramTypes
module BS = LibSerialization.Binary.Serialization



let find
  ((branchID, location) : Option<PT.BranchID> * PT.PackageLocation)
  : Ply<Option<PT.FQValueName.Package>> =
  uply {
    let modulesStr = String.concat "." location.modules

    return!
      Sql.query
        """
        SELECT item_id
        FROM locations
        WHERE owner = @owner
          AND modules = @modules
          AND name = @name
          AND item_type = 'value'
          AND deprecated_at IS NULL
          AND (branch_id IS NULL OR branch_id = @branch_id)
        ORDER BY created_at DESC
        LIMIT 1
        """
      |> Sql.parameters
        [ "owner", Sql.string location.owner
          "modules", Sql.string modulesStr
          "name", Sql.string location.name
          "branch_id",
          (match branchID with
           | Some id -> Sql.uuid id
           | None -> Sql.dbnull) ]
      |> Sql.executeRowOptionAsync (fun read ->
        let guid = read.uuid "item_id"
        Hash.ofBytes (guid.ToByteArray()))
  }

let get (id : PT.FQValueName.Package) : Ply<Option<PT.PackageValue.PackageValue>> =
  uply {
    return!
      Sql.query
        """
        SELECT pt_def
        FROM package_values
        WHERE id = @id
        """
      |> Sql.parameters [ "id", Sql.uuid (Hash.toGuid id) ]
      |> Sql.executeRowOptionAsync (fun read -> read.bytes "pt_def")
      |> Task.map (Option.map BS.PT.PackageValue.deserialize)
  }

let getLocation
  ((branchID, id) : Option<PT.BranchID> * PT.FQValueName.Package)
  : Ply<Option<PT.PackageLocation>> =
  uply {
    return!
      Sql.query
        """
        SELECT owner, modules, name
        FROM locations
        WHERE item_id = @item_id
          AND item_type = 'value'
          AND deprecated_at IS NULL
          AND (branch_id IS NULL OR branch_id = @branch_id)
        ORDER BY created_at DESC
        LIMIT 1
        """
      |> Sql.parameters
        [ "item_id", Sql.uuid (Hash.toGuid id)
          "branch_id",
          (match branchID with
           | Some id -> Sql.uuid id
           | None -> Sql.dbnull) ]
      |> Sql.executeRowOptionAsync (fun read ->
        let modulesStr = read.string "modules"
        { owner = read.string "owner"
          modules = modulesStr.Split('.') |> Array.toList
          name = read.string "name" })
  }
