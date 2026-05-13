module Tests.PDD

open Expecto
open Prelude

module RT = LibExecution.RuntimeTypes
module VT = LibExecution.ValueType
module Dval = LibExecution.Dval


// ---------------------------------------------------------------------------
// Phase E: Dval.defaultFor — drives the tolerant-runtime EmptyBody path
// ---------------------------------------------------------------------------

module Defaults =
  let testCases : Test list =
    [ test "defaultFor TUnit = DUnit" {
        Expect.equal (Dval.defaultFor RT.TUnit) RT.DUnit ""
      }
      test "defaultFor TBool = DBool false" {
        Expect.equal (Dval.defaultFor RT.TBool) (RT.DBool false) ""
      }
      test "defaultFor TInt64 = DInt64 0L" {
        Expect.equal (Dval.defaultFor RT.TInt64) (RT.DInt64 0L) ""
      }
      test "defaultFor TInt32 = DInt32 0" {
        Expect.equal (Dval.defaultFor RT.TInt32) (RT.DInt32 0) ""
      }
      test "defaultFor TFloat = DFloat 0.0" {
        Expect.equal (Dval.defaultFor RT.TFloat) (RT.DFloat 0.0) ""
      }
      test "defaultFor TString = DString \"\"" {
        Expect.equal (Dval.defaultFor RT.TString) (RT.DString "") ""
      }
      test "defaultFor TChar = DChar \"\"" {
        Expect.equal (Dval.defaultFor RT.TChar) (RT.DChar "") ""
      }
      test "defaultFor (TList TInt64) = empty list" {
        Expect.equal
          (Dval.defaultFor (RT.TList RT.TInt64))
          (RT.DList(VT.unknown, []))
          ""
      }
      test "defaultFor (TDict TString) = empty dict" {
        Expect.equal
          (Dval.defaultFor (RT.TDict RT.TString))
          (RT.DDict(VT.unknown, Map.empty))
          ""
      }
      test "defaultFor (TTuple (TInt64, TString, [])) = (0L, \"\")" {
        Expect.equal
          (Dval.defaultFor (RT.TTuple(RT.TInt64, RT.TString, [])))
          (RT.DTuple(RT.DInt64 0L, RT.DString "", []))
          ""
      }
      test "defaultFor (TVariable _) falls through to DUnit" {
        Expect.equal (Dval.defaultFor (RT.TVariable "'a")) RT.DUnit ""
      }
      test "defaultFor TUuid = empty Guid" {
        Expect.equal (Dval.defaultFor RT.TUuid) (RT.DUuid System.Guid.Empty) ""
      } ]

  let tests = testList "Defaults" testCases


// ---------------------------------------------------------------------------
// Phase B: FQFnName.Pending — construction + round-trip basics
// ---------------------------------------------------------------------------

module Pending =
  let constructorMakesUniqueHandles =
    test "fqPending generates fresh Guids on each call" {
      let p1 = RT.FQFnName.fqPending "foo"
      let p2 = RT.FQFnName.fqPending "foo"
      match p1, p2 with
      | RT.FQFnName.Pending a, RT.FQFnName.Pending b ->
        Expect.equal a.name b.name "names match"
        Expect.notEqual a.handle b.handle "handles differ"
      | _ -> Expect.equal 1 2 "Expected two Pending values"
    }

  let constructorRespectsName =
    test "fqPending preserves the requested name" {
      match RT.FQFnName.fqPending "myMissingFn" with
      | RT.FQFnName.Pending p -> Expect.equal p.name "myMissingFn" ""
      | _ -> Expect.equal 1 2 "Expected Pending"
    }

  let tests =
    testList "Pending" [ constructorMakesUniqueHandles; constructorRespectsName ]


// ---------------------------------------------------------------------------
// Phase C: PackageManager.materializeFn — default is no-op
// ---------------------------------------------------------------------------

module PMField =
  let emptyMaterializeReturnsNone =
    testTask "PackageManager.empty.materializeFn returns None for any pending" {
      let pm = RT.PackageManager.empty
      let p =
        match RT.FQFnName.fqPending "neverMaterializes" with
        | RT.FQFnName.Pending p -> p
        | _ -> Exception.raiseInternal "unreachable" []
      let! result = pm.materializeFn p |> Ply.toTask
      Expect.equal result None "default materializer is a no-op"
    }

  let stubMaterializerHooksIn =
    testTask "Override materializeFn on a PackageManager via record-with" {
      let mutable callCount = 0
      let stub : RT.FQFnName.Pending -> Ply<RT.PackageFn.PackageFn option> =
        fun _ ->
          callCount <- callCount + 1
          Ply None
      let pm = { RT.PackageManager.empty with materializeFn = stub }
      let p =
        match RT.FQFnName.fqPending "anyName" with
        | RT.FQFnName.Pending p -> p
        | _ -> Exception.raiseInternal "unreachable" []
      let! _ = pm.materializeFn p |> Ply.toTask
      let! _ = pm.materializeFn p |> Ply.toTask
      Expect.equal callCount 2 "stub was invoked twice"
    }

  let tests = testList "PMField" [ emptyMaterializeReturnsNone; stubMaterializerHooksIn ]


let tests = testList "PDD" [ Defaults.tests; Pending.tests; PMField.tests ]
