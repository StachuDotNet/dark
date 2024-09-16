module Tests.Interpreter

open Expecto
open Prelude
open TestUtils.TestUtils

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module VT = LibExecution.ValueType
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module RTE = RT.RuntimeError

module E = TestValues.Expressions
module PM = TestValues.PM

let tCheckVM
  name
  ptExpr
  expectedInsts
  (extraVmStateAssertions : RT.VMState -> unit)
  =
  testTask name {
    let vmState = ptExpr |> PT2RT.Expr.toRT Map.empty 0 |> RT.VMState.fromExpr

    let! exeState =
      executionStateFor TestValues.pm (System.Guid.NewGuid()) false false

    let! actual = LibExecution.Interpreter.execute exeState vmState |> Ply.toTask
    Expect.equal actual expectedInsts ""

    extraVmStateAssertions vmState
  }

let t name ptExpr expectedInsts =
  tCheckVM name ptExpr expectedInsts (ignore<RT.VMState>)



let tFail name ptExpr expectedRte =
  testTask name {
    let instructions = ptExpr |> PT2RT.Expr.toRT Map.empty 0

    let! exeState =
      executionStateFor TestValues.pm (System.Guid.NewGuid()) false false

    let! actual = LibExecution.Execution.executeExpr exeState instructions

    match actual with
    | Ok _ -> return Expect.equal 1 2 "Expected an RTE, but got a successful result"
    | Error(actualRte) -> return Expect.equal actualRte expectedRte ""
  }


module Basic =
  // CLEANUP back fill with more simple stuff

  let one = t "1" E.Basic.one (RT.DInt64 1L)

  //let onePlusTwo = t "1+2" E.Basic.onePlusTwo (RT.DInt64 3L)

  let tests =
    testList
      "Basic"
      [ one
        //onePlusTwo
        ]


module List =
  let simple =
    t
      "[true; false; true]"
      E.List.simple
      (RT.DList(VT.bool, [ RT.DBool true; RT.DBool false; RT.DBool true ]))

  let nested =
    t
      "[[true; false]; [false; true]]"
      E.List.nested
      (RT.DList(
        VT.list VT.bool,
        [ RT.DList(VT.bool, [ RT.DBool true; RT.DBool false ])
          RT.DList(VT.bool, [ RT.DBool false; RT.DBool true ]) ]
      ))

  let mixed =
    tFail
      "[1; true]"
      E.List.mixed
      //   RT.DString "Could not merge types List<Bool> and List<Int64>"
      (RTE.Lists.TriedToAddMismatchedData(VT.int64, VT.bool, RT.DBool true)
       |> RTE.List)

  let tests = testList "Lists" [ simple; nested; mixed ]


module Let =
  let simple = t "let x = true\nx" E.Let.simple (RT.DBool true)

  let tuple = t "let (x, y) = (1, 2)\nx" E.Let.tuple (RT.DInt64 1L)

  /// `let (a, b) = 1 in a`
  let tupleNotTuple =
    tFail
      "let (a, b) = 1 in a"
      E.Let.tupleNotTuple
      (RTE.Error.Let(
        RTE.Lets.Error.PatternDoesNotMatch(
          RT.DInt64 1,
          RT.LPTuple(RT.LPVariable 1, RT.LPVariable 2, [])
        )
      ))

  /// `let (a, b) = (1, 2, 3) in a`
  let tupleIncorrectLen =
    tFail
      "let (a, b) = (1, 2, 3) in a"
      E.Let.tupleIncorrectLen
      (RTE.Error.Let(
        RTE.Lets.Error.PatternDoesNotMatch(
          RT.DTuple(RT.DInt64 1, RT.DInt64 2, [ RT.DInt64 3 ]),
          RT.LPTuple(RT.LPVariable 4, RT.LPVariable 5, [])
        )
      ))

  let tupleNested =
    t "let (a, (b, c)) = (1, (2, 3))\nb" E.Let.tupleNested (RT.DInt64 2L)

  /// `a`
  let undefinedVar = tFail "a" E.Let.undefinedVar (RTE.VariableNotFound "a")

  let tests =
    testList
      "Let"
      [ simple; tuple; tupleNotTuple; tupleIncorrectLen; tupleNested; undefinedVar ]


module String =
  let simple = t "[\"hello\"]" E.String.simple (RT.DString "hello")

  let withInterpolation =
    t
      "[let x = \"world\" in $\"hello {x}\"]"
      E.String.withInterpolation
      (RT.DString "hello, world")

  let tests = testList "Strings" [ simple; withInterpolation ]


module Dict =
  let empty = t "Dict {}" E.Dict.empty (RT.DDict(VT.unknown, Map.empty))

  let simple =
    t
      "Dict { t: true}"
      E.Dict.simple
      (RT.DDict(VT.bool, Map [ "key", RT.DBool true ]))

  let multEntries =
    t
      "Dict {t: true; f: false}"
      E.Dict.multEntries
      (RT.DDict(VT.bool, Map [ "t", RT.DBool true; "f", RT.DBool false ]))

  let dupeKey =
    tFail
      "Dict {t: true; f: false; t: false}"
      E.Dict.dupeKey
      (RTE.Dict(RTE.Dicts.TriedToAddKeyAfterAlreadyPresent "t"))

  let tests = testList "Dict" [ empty; simple; multEntries; dupeKey ]


module If =
  let gotoThenBranch = t "if true then 1 else 2" E.If.gotoThenBranch (RT.DInt64 1L)
  let gotoElseBranch = t "if false then 1 else 2" E.If.gotoElseBranch (RT.DInt64 2L)
  let elseMissing = t "if false then 1" E.If.elseMissing RT.DUnit

  let tests = testList "If" [ gotoThenBranch; gotoElseBranch; elseMissing ]


module Tuples =
  let two =
    t "(false, true)" E.Tuples.two (RT.DTuple(RT.DBool false, RT.DBool true, []))

  let three =
    t
      "(false, true, false)"
      E.Tuples.three
      (RT.DTuple(RT.DBool false, RT.DBool true, [ RT.DBool false ]))

  let nested =
    t
      "((false, true), true, (true, false)))"
      E.Tuples.nested
      (RT.DTuple(
        RT.DTuple(RT.DBool false, RT.DBool true, []),
        RT.DBool true,
        [ RT.DTuple(RT.DBool true, RT.DBool false, []) ]
      ))

  let tests = testList "Tuples" [ two; three; nested ]


module Match =
  let simple =
    t
      "match true with\n| false -> \"first branch\"\n| true -> \"second branch\""
      E.Match.simple
      (RT.DString "second branch")

  let notMatched =
    tFail
      "match true with\n| false -> \"first branch\""
      E.Match.notMatched
      RTE.MatchUnmatched

  let withVar = t "match true with\n| x -> x" E.Match.withVar (RT.DBool true)

  // let withVarAndWhenCondition =
  //   t
  //     "match 4 with\n| 1 -> \"first branch\"\n| x when x % 2 == 0 -> \"second branch\""
  //     E.Match.withVarAndWhenCondition
  //     (RT.DString "second branch")

  let list =
    t
      "match [1, 2] with\n| [1, 2] -> \"first branch\""
      E.Match.list
      (RT.DString "first branch")

  let listCons =
    t
      "match [1, 2] with\n| 1 :: tail -> tail"
      E.Match.listCons
      (RT.DList(VT.int64, [ RT.DInt64 2L ]))

  let tuple =
    t
      "match (1, 2) with\n| (1, 2) -> \"first branch\""
      E.Match.tuple
      (RT.DString "first branch")

  let tests =
    testList
      "Match"
      [ simple
        notMatched
        withVar
        //withVarAndWhenCondition
        list
        listCons
        tuple ]


module Records =
  let simple =
    let typeName = RT.FQTypeName.fqPackage PM.Types.Records.singleField
    t
      "Test.Test { key = true }"
      E.Records.simple
      (RT.DRecord(typeName, typeName, [], Map [ "key", RT.DBool true ]))

  let nested =
    let outerTypeName = RT.FQTypeName.fqPackage PM.Types.Records.nested
    let innerTypeName = RT.FQTypeName.fqPackage PM.Types.Records.singleField
    t
      "Test.Test2 { outer = (Test.Test { key = true }) }"
      E.Records.nested
      (RT.DRecord(
        outerTypeName,
        outerTypeName,
        [],
        Map
          [ "outer",
            RT.DRecord(
              innerTypeName,
              innerTypeName,
              [],
              Map [ "key", RT.DBool true ]
            ) ]
      ))


  let tests = testList "Records" [ simple; nested ]


module RecordFieldAccess =
  let simple =
    t "(Test.Test { key = true }).key" E.RecordFieldAccess.simple (RT.DBool true)
  let notRecord =
    tFail
      "1.key"
      E.RecordFieldAccess.notRecord
      (RTE.Record(RTE.Records.FieldAccessNotRecord VT.int64))

  let missingField =
    tFail
      "(Test.Test { key = true }).missing"
      E.RecordFieldAccess.missingField
      (RTE.Record(RTE.Records.FieldAccessFieldNotFound "missing"))

  let nested =
    t
      "(Test.Test2 { outer = (Test.Test { key = true }) }).outer.key"
      E.RecordFieldAccess.nested
      (RT.DBool true)

  let tests =
    testList "RecordFieldAccess" [ simple; notRecord; missingField; nested ]


module Lambdas =
  module Identity =
    let unapplied =
      tCheckVM
        "fn x -> x"
        E.Lambdas.Identity.unapplied
        (RT.DApplicable(
          RT.AppLambda
            { exprId = E.Lambdas.Identity.id; closedRegisters = []; argsSoFar = [] }
        ))
        (fun vm -> Expect.isFalse (Map.isEmpty vm.lambdas) "no lambdas in VMState")

    let applied = t "(fn x -> x) 1" E.Lambdas.Identity.applied (RT.DInt64 1L)

    let tests = testList "Identity" [ unapplied; applied ]

  let tests = testList "Lambdas" [ Identity.tests ]


module Fns =
  module Builtin =
    let unapplied =
      t
        "Builtin.int64Add"
        E.Fns.Builtin.unapplied
        (RT.DApplicable(
          RT.AppNamedFn { name = RT.FQFnName.fqBuiltin "int64Add" 0; argsSoFar = [] }
        ))

    let partiallyApplied =
      t
        "Builtin.int64Add 1"
        E.Fns.Builtin.partiallyApplied
        (RT.DApplicable(
          RT.AppNamedFn
            { name = RT.FQFnName.fqBuiltin "int64Add" 0
              argsSoFar = [ RT.DInt64 1 ] }
        ))

    let fullyApplied =
      t "Builtin.int64Add 1 2" E.Fns.Builtin.fullyApplied (RT.DInt64 3L)

    let twoStepApplied =
      t "(Builtin.int64Add 1) 2" E.Fns.Builtin.twoStepApplication (RT.DInt64 3L)

    let tests =
      testList
        "Builtin"
        [ unapplied; partiallyApplied; fullyApplied; twoStepApplied ]


  module Package =
    module MyAdd =

      let unapplied =
        t
          "Test.myAdd"
          E.Fns.Package.MyAdd.unapplied
          (RT.DApplicable(
            RT.AppNamedFn
              { name = RT.FQFnName.fqPackage E.Fns.Package.MyAdd.id; argsSoFar = [] }
          ))

      let partiallyApplied =
        t
          "Test.myAdd 1"
          E.Fns.Package.MyAdd.partiallyApplied
          (RT.DApplicable(
            RT.AppNamedFn
              { name = RT.FQFnName.fqPackage E.Fns.Package.MyAdd.id
                argsSoFar = [ RT.DInt64 1 ] }
          ))

      let fullyApplied =
        t "Test.myAdd 1 2" E.Fns.Package.MyAdd.fullyApplied (RT.DInt64 3L)


      let tests = testList "Myadd" [ unapplied; partiallyApplied; fullyApplied ]


    module Fact =
      let unapplied =
        t
          "Test.fact"
          E.Fns.Package.Fact.unapplied
          (RT.DApplicable(
            RT.AppNamedFn
              { name = RT.FQFnName.fqPackage E.Fns.Package.Fact.id; argsSoFar = [] }
          ))

      let appliedWith2 =
        t "Test.fact 2" E.Fns.Package.Fact.appliedWith2 (RT.DInt64 2L)

      let appliedWith20 =
        t
          "Test.fact 20"
          E.Fns.Package.Fact.appliedWith20
          (RT.DInt64 2432902008176640000L)

      let tests = testList "Fact" [ unapplied; appliedWith2; appliedWith20 ]

    let tests = testList "Package" [ MyAdd.tests; Fact.tests ]

  let tests = testList "Fns" [ Builtin.tests; Package.tests ]


(*
((PACKAGE.Darklang.Stdlib.List.repeat_v0 348L 1L)
 |> Builtin.unwrap
 |> PACKAGE.Darklang.Stdlib.List.map (fun f -> f)) = []
 *)

let tests =
  testList
    "Interpreter"
    [ Basic.tests
      List.tests
      Let.tests
      String.tests
      Dict.tests
      If.tests
      Tuples.tests
      Match.tests
      Records.tests
      RecordFieldAccess.tests
      Lambdas.tests
      Fns.tests ]