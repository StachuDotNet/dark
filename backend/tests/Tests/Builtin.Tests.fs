module Tests.Builtin

// Misc tests of Builtin (both LibCloud and LibExecution) that could not be
// tested via LibExecution.tests

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

module RT = LibExecution.RuntimeTypes
module PT = LibExecution.ProgramTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module PTParser = LibExecution.ProgramTypesParser
module Exe = LibExecution.Execution

open TestUtils.TestUtils


let oldFunctionsAreDeprecated =
  let builtinToString (name : RT.FQFnName.Builtin) = $"{name.name}_v{name.version}"

  testTask "old functions are deprecated" {
    let mutable counts = Map.empty

    let fns = (localBuiltIns PT.PackageManager.empty).fns |> Map.values

    fns
    |> List.iter (fun fn ->
      let key = builtinToString fn.name

      if fn.deprecated = RT.NotDeprecated then
        counts <-
          Map.update
            key
            (fun count -> count |> Option.defaultValue 0 |> (+) 1 |> Some)
            counts

      ())

    Map.iter
      (fun name count ->
        Expect.equal count 1 $"{name} has more than one undeprecated function")
      counts
  }

let usageRestrictionTests =
  testList
    "usage restrictions"
    [ test "AllowAny allows any caller" {
        // Test that a builtin with AllowAny can be called from any context
        let builtinFns = (localBuiltIns PT.PackageManager.empty).fns

        // Pick a builtin that should have AllowAny (most do by default)
        let testBuiltinKey : RT.FQFnName.Builtin = { name = "int8Add"; version = 0 }

        match Map.tryFind testBuiltinKey builtinFns with
        | Some builtinFn ->
          Expect.equal
            builtinFn.usageRestriction
            RT.AllowAny
            "int8Add should have AllowAny restriction"
        | None -> Exception.raiseInternal "int8Add builtin not found" []
      }

      testTask "AllowOne blocks unauthorized callers" {
        // Create a test builtin with AllowOne restriction
        let allowedId = System.Guid.Parse("00000000-0000-0000-0000-000000000001")
        let unauthorizedId = System.Guid.Parse("00000000-0000-0000-0000-000000000002")

        let testBuiltin : RT.BuiltInFn =
          { name = { name = "testRestricted"; version = 0 }
            typeParams = []
            parameters = []
            returnType = RT.TInt64
            description = "Test builtin with restriction"
            fn = (function
              | _, _, _, _ -> Ply(RT.DInt64 42L))
            deprecated = RT.NotDeprecated
            sqlSpec = RT.NotQueryable
            previewable = RT.Pure
            usageRestriction = RT.AllowOne allowedId }

        let builtinFns = Map.add testBuiltin.name testBuiltin Map.empty

        // Try to resolve with unauthorized caller
        let result =
          LibParser.NameResolver.resolveFnName
            None
            None
            builtinFns
            PT.PackageManager.empty
            LibParser.NameResolver.OnMissing.Allow
            []
            (Some unauthorizedId)
            (LibParser.WrittenTypes.Name.KnownBuiltin("testRestricted", 0))
          |> Ply.toTask

        let! resultValue = result

        match resultValue with
        | Error _ -> () // Expected: should fail
        | Ok _ ->
          Exception.raiseInternal
            "Expected restriction error for unauthorized caller"
            []
      }

      testTask "AllowOne allows the authorized caller" {
        // Create a test builtin with AllowOne restriction
        let allowedId = System.Guid.Parse("00000000-0000-0000-0000-000000000001")

        let testBuiltin : RT.BuiltInFn =
          { name = { name = "testRestricted"; version = 0 }
            typeParams = []
            parameters = []
            returnType = RT.TInt64
            description = "Test builtin with restriction"
            fn = (function
              | _, _, _, _ -> Ply(RT.DInt64 42L))
            deprecated = RT.NotDeprecated
            sqlSpec = RT.NotQueryable
            previewable = RT.Pure
            usageRestriction = RT.AllowOne allowedId }

        let builtinFns = Map.add testBuiltin.name testBuiltin Map.empty

        // Try to resolve with authorized caller
        let result =
          LibParser.NameResolver.resolveFnName
            None
            None
            builtinFns
            PT.PackageManager.empty
            LibParser.NameResolver.OnMissing.Allow
            []
            (Some allowedId)
            (LibParser.WrittenTypes.Name.KnownBuiltin("testRestricted", 0))
          |> Ply.toTask

        let! resultValue = result

        match resultValue with
        | Ok _ -> () // Expected: should succeed
        | Error e ->
          Exception.raiseInternal
            $"Expected success for authorized caller, got error: {e}"
            []
      } ]


let tests =
  testList "builtin" [ oldFunctionsAreDeprecated; usageRestrictionTests ]
