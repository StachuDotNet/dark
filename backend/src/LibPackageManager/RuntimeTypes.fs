module LibPackageManager.RuntimeTypes

open Prelude

open Microsoft.Data.Sqlite
open Fumble
open LibDB.Db

module RT = LibExecution.RuntimeTypes
module BinarySerialization = LibBinarySerialization.BinarySerialization


module Type =
  let get (id : uuid) : Ply<Option<RT.PackageType.PackageType>> =
    uply {
      return!
        Sql.query
          """
          SELECT rt_def
          FROM package_types
          WHERE id = @id
          """
        |> Sql.parameters [ "id", Sql.uuid id ]
        |> Sql.executeRowOptionAsync (fun read -> read.bytes "rt_def")
        |> Task.map (Option.map (BinarySerialization.RT.PackageType.deserialize id))
    }


module Value =
  let get (id : uuid) : Ply<Option<RT.PackageValue.PackageValue>> =
    uply {
      return!
        Sql.query
          """
          SELECT rt_dval
          FROM package_values
          WHERE id = @id
          """
        |> Sql.parameters [ "id", Sql.uuid id ]
        |> Sql.executeRowOptionAsync (fun read -> read.bytes "rt_dval")
        |> Task.map (Option.map (BinarySerialization.RT.PackageValue.deserialize id))
    }

  /// Find all value IDs that have the given type
  let findByTypeId (typeId : uuid) : Ply<List<uuid>> =
    uply {
      return!
        Sql.query
          """
          SELECT id
          FROM package_values
          WHERE value_type_id = @type_id
          """
        |> Sql.parameters [ "type_id", Sql.string (string typeId) ]
        |> Sql.executeAsync (fun read ->
          read.string "id" |> System.Guid.Parse)
    }


module Fn =
  let get (id : uuid) : Ply<Option<RT.PackageFn.PackageFn>> =
    uply {
      return!
        Sql.query
          """
          SELECT rt_instrs
          FROM package_functions
          WHERE id = @id
          """
        |> Sql.parameters [ "id", Sql.uuid id ]
        |> Sql.executeRowOptionAsync (fun read -> read.bytes "rt_instrs")
        |> Task.map (Option.map (BinarySerialization.RT.PackageFn.deserialize id))
    }
