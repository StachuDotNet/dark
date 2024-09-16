module Tests.TestValues

open Prelude
open TestUtils.TestUtils

module PT = LibExecution.ProgramTypes
module PackageIDs = LibExecution.PackageIDs
module RT = LibExecution.RuntimeTypes

open TestUtils.PTShortcuts

// TODO: consider adding an Expect.equalInstructions,
// which better points out the diffs in the lists

module PM =
  module Types =
    let make id name definition : PT.PackageType.PackageType =
      { id = id
        name = name
        declaration = { typeParams = []; definition = definition }
        description = "TODO"
        deprecated = PT.NotDeprecated }

    module Records =
      let make id name fields =
        make id name (PT.TypeDeclaration.Record(NEList.ofListUnsafe "" [] fields))

      let singleField = System.Guid.NewGuid()
      let nested = System.Guid.NewGuid()

      let all : List<PT.PackageType.PackageType> =
        [ make
            singleField
            (PT.PackageType.name "Test" [] "Test")
            [ { name = "key"; typ = PT.TBool; description = "TODO" } ]

          make
            nested
            (PT.PackageType.name "Test" [] "Test2")
            [ { name = "outer"
                typ = PT.TCustomType(Ok(PT.FQTypeName.fqPackage singleField), [])
                description = "TODO" } ] ]

    module Enums =
      let all = []

    let all = Records.all @ Enums.all

  module Constants =
    let all = []

  module Functions =
    let all = []



module Expressions =
  module Basic =
    let one = eInt64 1

  // let onePlusTwo =
  //   eApply
  //     (PT.EFnName(gid (), Ok(PT.FQFnName.fqBuiltIn "int64Add" 0)))
  //     []
  //     [ eInt64 1; eInt64 2 ]


  module Let =
    // TODO: try to use undefined variable
    // TODO: lpunit
    let simple = eLet (lpVar "x") (eBool true) (eVar "x")

    let tuple =
      eLet
        (lpTuple (lpVar "x") (lpVar "y") [])
        (eTuple (eInt64 1) (eInt64 2) [])
        (eVar "x")

    /// `let (a, b) = 1 in a`
    let tupleNotTuple =
      eLet (lpTuple (lpVar "a") (lpVar "b") []) (eInt64 1) (eVar "a")

    /// `let (a, b) = (1, 2, 3) in a`
    let tupleIncorrectLen =
      eLet
        (lpTuple (lpVar "a") (lpVar "b") [])
        (eTuple (eInt64 1) (eInt64 2) [ eInt64 3 ])
        (eVar "a")


    /// `let (a, (b, c)) = (1, (2, 3)) in b`
    let tupleNested =
      eLet
        (lpTuple (lpVar "a") (lpTuple (lpVar "b") (lpVar "c") []) [])
        (eTuple (eInt64 1) (eTuple (eInt64 2) (eInt64 3) []) [])
        (eVar "b")

    let undefinedVar = eVar "a"


  module List =
    let simple = eList [ eBool true; eBool false; eBool true ]

    let nested =
      eList [ eList [ eBool true; eBool false ]; eList [ eBool false; eBool true ] ]

    let mixed = eList [ eInt64 1; eBool true ]


  module String =
    let simple = eStr [ strText "hello" ]

    let withInterpolation =
      eLet
        (lpVar "x")
        (eStr [ strText ", world" ])
        (eStr [ strText "hello"; strInterp (eVar "x") ])


  module Dict =
    let empty = eDict []
    let simple = eDict [ "key", eBool true ]
    let multEntries = eDict [ "t", eBool true; "f", eBool false ]
    let dupeKey = eDict [ "t", eBool true; "f", eBool false; "t", eBool false ]

  module If =
    let gotoThenBranch = eIf (eBool true) (eInt64 1) (Some(eInt64 2))
    let gotoElseBranch = eIf (eBool false) (eInt64 1) (Some(eInt64 2))
    let elseMissing = eIf (eBool false) (eInt64 1) None


  module Tuples =
    /// `(false, true)`
    let two = eTuple (eBool false) (eBool true) []

    /// `(false, true, false)`
    let three = eTuple (eBool false) (eBool true) [ eBool false ]

    /// `((false, true), true, (true, false))`
    let nested =
      eTuple
        (eTuple (eBool false) (eBool true) [])
        (eBool true)
        [ eTuple (eBool true) (eBool false) [] ]


  module Match =
    /// match true with
    /// | false -> "first branch"
    /// | true -> "second branch"
    let simple =
      eMatch
        (eBool true)
        [ { pat = PT.MPBool(gid (), false)
            whenCondition = None
            rhs = eStr [ strText "first branch" ] }
          { pat = PT.MPBool(gid (), true)
            whenCondition = None
            rhs = eStr [ strText "second branch" ] } ]

    /// match true with
    /// | false -> "first branch"
    let notMatched =
      eMatch
        (eBool true)
        [ { pat = PT.MPBool(gid (), false)
            whenCondition = None
            rhs = eStr [ strText "first branch" ] } ]

    /// match true with
    /// | x -> x
    let withVar =
      eMatch
        (eBool true)
        [ { pat = PT.MPVariable(gid (), "x"); whenCondition = None; rhs = eVar "x" } ]

    // /// match 4 with
    // /// | 1 -> "first branch"
    // /// | x when x % 2 == 0 -> "second branch"
    // let withVarAndWhenCondition =
    //   eMatch
    //     (eInt64 4)
    //     [ { pat = PT.MPInt64(gid (), 1)
    //         whenCondition = None
    //         rhs = eStr [ strText "first branch" ] }
    //       { pat = PT.MPVariable(gid (), "x")
    //         // "is even"
    //         whenCondition =
    //           Some(
    //             eApply
    //               (PT.EFnName(gid (), Ok(PT.FQFnName.fqBuiltIn "equals" 0)))
    //               []
    //               [ eApply
    //                   (PT.EFnName(gid (), Ok(PT.FQFnName.fqBuiltIn "int64Mod" 0)))
    //                   []
    //                   [ eVar "x" ]
    //                 eInt64 2 ]
    //           )
    //         rhs = eStr [ strText "second branch" ] } ]

    let list =
      eMatch
        (eList [ eInt64 1; eInt64 2 ])
        [ { pat = PT.MPList(gid (), [ PT.MPInt64(gid (), 1); PT.MPInt64(gid (), 2) ])
            whenCondition = None
            rhs = eStr [ strText "first branch" ] } ]

    let listCons =
      eMatch
        (eList [ eInt64 1; eInt64 2 ])
        [ { pat =
              PT.MPListCons(
                gid (),
                PT.MPInt64(gid (), 1),
                PT.MPVariable(gid (), "tail")
              )
            whenCondition = None
            rhs = eVar "tail" } ]

    let tuple =
      eMatch
        (eTuple (eInt64 1) (eInt64 2) [])
        [ { pat =
              PT.MPTuple(gid (), PT.MPInt64(gid (), 1), PT.MPInt64(gid (), 2), [])
            whenCondition = None
            rhs = eStr [ strText "first branch" ] } ]


  module Records =
    let simple =
      eRecord (typeNamePkg PM.Types.Records.singleField) [] [ "key", eBool true ]

    let nested = eRecord (typeNamePkg PM.Types.Records.nested) [] [ "outer", simple ]

  module RecordFieldAccess =
    let simple = eFieldAccess Records.simple "key"
    let notRecord = eFieldAccess (eInt64 1) "key"
    let missingField = eFieldAccess Records.simple "missing"
    let nested = eFieldAccess (eFieldAccess Records.nested "outer") "key"


  // //module RecordUpdate =

  module Lambdas =
    module Identity =

      let id = gid ()

      let unapplied = eLambda id [ lpVar "x" ] (eVar "x")

      let applied = eApply unapplied [] [ eInt64 1 ]

  // TODO:
  // module Add =
  // module AddWithClosedVar =
  // SomethingWIthMultipleClosedVars
  // TODO: partial application

  module Fns =
    module Builtin =
      let unapplied = eBuiltinFn "int64Add" 0
      let partiallyApplied = eApply unapplied [] [ eInt64 1 ]
      let fullyApplied = eApply unapplied [] [ eInt64 1; eInt64 2 ]
      let twoStepApplication = eApply partiallyApplied [] [ eInt64 2 ]

    module Package =
      module MyAdd =
        let id = System.Guid.Parse "a180ed3b-e8ee-42e5-b3c6-9e7ca32ee273"

        let unapplied = ePackageFn id
        let partiallyApplied = eApply unapplied [] [ eInt64 1 ]
        let fullyApplied = eApply unapplied [] [ eInt64 1; eInt64 2 ]


      module Fact =
        let id = System.Guid.Parse "34c0c7bb-2bfa-4dc3-85f9-b965ba3c7880"
        let unapplied = ePackageFn id
        let appliedWith2 = eApply unapplied [] [ eInt64 2 ]
        let appliedWith20 = eApply unapplied [] [ eInt64 20 ]


module PT2RT = LibExecution.ProgramTypesToRuntimeTypes

let pm : PT.PackageManager =
  PT.PackageManager.empty
  |> PT.PackageManager.withExtras
    []
    []
    [ { id = Expressions.Fns.Package.MyAdd.id
        name = PT.PackageFn.name "Test" [] "add"
        typeParams = []
        parameters =
          NEList.ofList
            { name = "a"; typ = PT.TInt64; description = "TODO" }
            [ { name = "b"; typ = PT.TInt64; description = "TODO" } ]
        returnType = PT.TInt64
        body = eApply (eBuiltinFn "int64Add" 0) [] [ eVar "a"; eVar "b" ]
        description = "TODO"
        deprecated = PT.NotDeprecated }

      { id = Expressions.Fns.Package.Fact.id
        name = PT.PackageFn.name "Test" [] "fact"
        typeParams = []
        parameters =
          NEList.ofList { name = "a"; typ = PT.TInt64; description = "TODO" } []
        returnType = PT.TInt64
        body =
          eIf
            (eApply (eBuiltinFn "equals" 0) [] [ eVar "a"; eInt64 1 ])
            (eInt64 1)
            (Some(
              eApply
                (eBuiltinFn "int64Multiply" 0)
                []
                [ eVar "a"
                  (eApply
                    (ePackageFn Expressions.Fns.Package.Fact.id)
                    []
                    [ eApply (eBuiltinFn "int64Subtract" 0) [] [ eVar "a"; eInt64 1 ] ]) ]
            ))

        description = "TODO"
        deprecated = PT.NotDeprecated } ]