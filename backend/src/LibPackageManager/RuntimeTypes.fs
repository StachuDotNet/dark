module LibPackageManager.RuntimeTypes

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

open Microsoft.Data.Sqlite
open Fumble
open LibDB.Db

module RT = LibExecution.RuntimeTypes
module BinarySerialization = LibBinarySerialization.BinarySerialization


module Type =
  let get (id : uuid) : Task<Option<RT.PackageType.PackageType>> =
    task {
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
  let get (id : uuid) : Task<Option<RT.PackageValue.PackageValue>> =
    task {
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


module Fn =
  let get (id : uuid) : Task<Option<RT.PackageFn.PackageFn>> =
    task {
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
