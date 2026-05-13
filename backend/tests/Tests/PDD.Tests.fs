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


// ---------------------------------------------------------------------------
// Day-2a: integration — Apply a Pending fn, the interpreter materializes
// it via a stub, runs the body, returns the result.
// ---------------------------------------------------------------------------

module Integration =

  /// Build a hardcoded "identity" PackageFn: takes one Int64 arg and returns
  /// it unchanged. body has 0 instructions and resultIn = 0 (the arg register).
  let identityFn : RT.PackageFn.PackageFn =
    { hash = RT.Hash "stub-identity-pdd-hash"
      typeParams = []
      parameters =
        NEList.singleton { name = "x"; typ = RT.TInt64 }
      returnType = RT.TInt64
      body =
        { registerCount = 1
          instructions = []
          resultIn = 0 } }

  /// Build the materializer stub: returns identityFn for any Pending called
  /// "myIdentity", else None. Counts calls.
  let buildStubMaterializer () =
    let mutable callCount = 0
    let materialize : RT.FQFnName.Pending -> Ply<RT.PackageFn.PackageFn option> =
      fun p ->
        callCount <- callCount + 1
        if p.name = "myIdentity" then Ply(Some identityFn) else Ply None
    materialize, (fun () -> callCount)

  /// Build an RT.Instructions block that loads a Pending fn ref + arg into
  /// registers and Apply's them. Returns the instruction set + the names of
  /// the registers used.
  let buildApplyPendingProgram (pendingName : string) (arg : int64) =
    let pending = RT.FQFnName.fqPending pendingName
    let applicable : RT.ApplicableNamedFn =
      { name = pending
        typeSymbolTable = Map.empty
        typeArgs = []
        argsSoFar = [] }
    let fnReg = 0
    let argReg = 1
    let resultReg = 2
    let instrs : RT.Instructions =
      { registerCount = 3
        resultIn = resultReg
        instructions =
          [ RT.LoadVal(fnReg, RT.DApplicable(RT.AppNamedFn applicable))
            RT.LoadVal(argReg, RT.DInt64 arg)
            RT.Apply(resultReg, fnReg, [], NEList.singleton argReg) ] }
    instrs

  let pendingFnGoesThroughInterpreter =
    testTask "Apply of Pending fn materializes via stub and returns DInt64 arg" {
      let instrs = buildApplyPendingProgram "myIdentity" 42L
      let materialize, getCallCount = buildStubMaterializer ()

      // Build minimum ExecutionState by overriding fns.materialize on top of
      // a default-empty state. We don't go through Execution.createState
      // because that needs a builtins map and PT PackageManager — overkill
      // for a hand-built RT-only test.
      let exeState : RT.ExecutionState =
        let pm = { RT.PackageManager.empty with materializeFn = materialize }
        let noOpReport : RT.ExceptionReporter = fun _ _ _ _ -> uply { return () }
        let noOpNotify : RT.Notifier = fun _ _ _ _ -> uply { return () }
        LibExecution.Execution.createState
          { values = Map.empty; fns = Map.empty }
          pm
          LibExecution.Execution.noTracing
          noOpReport
          noOpNotify
          (System.Guid.NewGuid())
          { dbs = Map.empty }

      let! result =
        LibExecution.Execution.execute exeState (None, instrs)

      match result with
      | Ok dv ->
        Expect.equal dv (RT.DInt64 42L) "Pending fn applied to 42 → returns 42"
      | Error(rte, _) ->
        Expect.equal 1 2 (sprintf "Expected success, got RTE: %A" rte)
      Expect.equal (getCallCount ()) 1 "materializer called exactly once"
    }

  /// Real arithmetic — uses the mini-parser's "x + 1L" output as a body
  /// and runs it through an interpreter with full Stdlib builtins available.
  /// Proves end-to-end: LLM-style body string → instructions → executes
  /// correctly with builtin calls.
  let addOnePendingActuallyComputes =
    testTask "Apply of Pending addOne (body 'x + 1L') with 5L → DInt64 6L" {
      let instrs = buildApplyPendingProgram "addOne" 5L

      // Build addOne fn using the mini-parser — same path the real
      // materializer takes.
      let addOneBody =
        match LibExecution.PDDMaterializer.parseMinimalBody "x" "x + 1L" with
        | Some b -> b
        | None -> Exception.raiseInternal "mini-parser failed" []
      let addOneFn : RT.PackageFn.PackageFn =
        { hash = RT.Hash "stub-addone-arith-hash"
          typeParams = []
          parameters = NEList.singleton { name = "x"; typ = RT.TInt64 }
          returnType = RT.TInt64
          body = addOneBody }

      let materialize : RT.FQFnName.Pending -> Ply<RT.PackageFn.PackageFn option> =
        fun p -> if p.name = "addOne" then Ply(Some addOneFn) else Ply None

      // Use the full test harness so Stdlib builtins are available
      // (int64Add lives in Builtins.Pure).
      let! (state : RT.ExecutionState) =
        TestUtils.TestUtils.executionStateFor TestValues.pm false Map.empty
      let exeState : RT.ExecutionState =
        { state with fns = { state.fns with materialize = materialize } }

      let! result =
        LibExecution.Execution.execute exeState (None, instrs)

      match result with
      | Ok dv ->
        Expect.equal dv (RT.DInt64 6L) "addOne 5 = 6"
      | Error(rte, _) ->
        Expect.equal 1 2 (sprintf "Expected success, got RTE: %A" rte)
    }

  let tests =
    testList
      "Integration"
      [ pendingFnGoesThroughInterpreter; addOnePendingActuallyComputes ]


// ---------------------------------------------------------------------------
// Day-2b: PDDMaterializer — parser for LLM JSON responses
// ---------------------------------------------------------------------------

module LLMParser =
  module M = LibExecution.PDDMaterializer

  let parsesCleanJson =
    test "parseLLMResponse handles clean {sig, body} JSON" {
      let input = """{"sig":"(x: Int64): Int64","body":"x"}"""
      match M.parseLLMResponse input with
      | Ok gen ->
        Expect.equal gen.sig_ "(x: Int64): Int64" "sig"
        Expect.equal gen.body "x" "body"
      | Error e -> Expect.equal 1 2 (sprintf "expected Ok, got Error: %s" e)
    }

  let parsesMarkdownFencedJson =
    test "parseLLMResponse strips ```json fences" {
      let input = "```json\n{\"sig\":\"foo\",\"body\":\"bar\"}\n```"
      match M.parseLLMResponse input with
      | Ok gen ->
        Expect.equal gen.sig_ "foo" "sig"
        Expect.equal gen.body "bar" "body"
      | Error e -> Expect.equal 1 2 (sprintf "expected Ok, got Error: %s" e)
    }

  let parsesPlainFencedJson =
    test "parseLLMResponse strips ``` (no lang tag) fences" {
      let input = "```\n{\"sig\":\"a\",\"body\":\"b\"}\n```"
      match M.parseLLMResponse input with
      | Ok gen -> Expect.equal gen.sig_ "a" "sig"
      | Error e -> Expect.equal 1 2 (sprintf "expected Ok, got Error: %s" e)
    }

  let errorsOnMissingField =
    test "parseLLMResponse errors when 'body' field is missing" {
      let input = """{"sig":"(x: Int64): Int64"}"""
      match M.parseLLMResponse input with
      | Ok _ -> Expect.equal 1 2 "expected Error"
      | Error e -> Expect.isTrue (e.Contains("body")) e
    }

  let errorsOnNonJson =
    test "parseLLMResponse errors on non-JSON input" {
      match M.parseLLMResponse "just some text" with
      | Ok _ -> Expect.equal 1 2 "expected Error"
      | Error _ -> ()
    }

  let buildUserPromptIsTerse =
    test "buildUserPrompt produces a short directive" {
      let p = M.buildUserPrompt "fib"
      Expect.isTrue (p.Contains("fib")) "contains the name"
      Expect.isTrue (p.Length < 200) "stays short"
    }

  let v4SystemPromptIsComplete =
    test "v4 system prompt mentions key syntax rules" {
      let p = M.v4SystemPrompt
      // spot-check that v4-specific rules are present
      Expect.isTrue (p.Contains("Int64")) "Int64 mentioned"
      Expect.isTrue (p.Contains("fun x ->")) "lambda syntax"
      Expect.isTrue (p.Contains("PREFIX")) "prefix-app rule"
      Expect.isTrue (p.Contains("Stdlib")) "stdlib prefix"
    }

  let tests =
    testList
      "LLMParser"
      [ parsesCleanJson
        parsesMarkdownFencedJson
        parsesPlainFencedJson
        errorsOnMissingField
        errorsOnNonJson
        buildUserPromptIsTerse
        v4SystemPromptIsComplete ]


// ---------------------------------------------------------------------------
// Day-2c: PDDMaterializer minimal-body parser → real RT.Instructions
// ---------------------------------------------------------------------------

module MinimalBody =
  module M = LibExecution.PDDMaterializer

  let parsesInt64Literal =
    test "parseMinimalBody '42L' → loads DInt64 42 into result reg" {
      match M.parseMinimalBody "x" "42L" with
      | Some instrs ->
        Expect.equal instrs.resultIn 1 "result reg is 1"
        Expect.equal instrs.registerCount 2 "2 registers"
        match instrs.instructions with
        | [ RT.LoadVal(1, RT.DInt64 42L) ] -> ()
        | other -> Expect.equal 1 2 (sprintf "wrong instrs: %A" other)
      | None -> Expect.equal 1 2 "expected Some"
    }

  let parsesNegativeInt64 =
    test "parseMinimalBody '-7L' → DInt64 -7" {
      match M.parseMinimalBody "x" "-7L" with
      | Some instrs ->
        match instrs.instructions with
        | [ RT.LoadVal(_, RT.DInt64 -7L) ] -> ()
        | other -> Expect.equal 1 2 (sprintf "wrong instrs: %A" other)
      | None -> Expect.equal 1 2 "expected Some"
    }

  let parsesIdentity =
    test "parseMinimalBody 'x' → identity (resultIn=0, no instructions)" {
      match M.parseMinimalBody "x" "x" with
      | Some instrs ->
        Expect.equal instrs.resultIn 0 "result reg is arg reg"
        Expect.equal instrs.instructions [] "no instructions"
      | None -> Expect.equal 1 2 "expected Some"
    }

  let stripsWhitespace =
    test "parseMinimalBody handles surrounding whitespace" {
      match M.parseMinimalBody "x" "  42L  " with
      | Some _ -> ()
      | None -> Expect.equal 1 2 "expected Some"
    }

  let returnsNoneOnUnknown =
    test "parseMinimalBody returns None on unrecognized body" {
      match M.parseMinimalBody "x" "complicated stuff" with
      | None -> ()
      | Some _ -> Expect.equal 1 2 "expected None"
    }

  let respectsParamName =
    test "identity respects the given param name (not just 'x')" {
      match M.parseMinimalBody "input" "input" with
      | Some instrs ->
        Expect.equal instrs.resultIn 0 "result reg is arg reg"
      | None -> Expect.equal 1 2 "expected Some"
    }

  let parsesAddition =
    test "parseMinimalBody 'x + 1L' → Apply(int64Add, [x, 1L])" {
      match M.parseMinimalBody "x" "x + 1L" with
      | Some instrs ->
        Expect.equal instrs.resultIn 3 "result reg is 3"
        Expect.equal instrs.registerCount 4 "4 registers"
        // Expect 3 instructions: load builtin, load constant, apply
        Expect.equal instrs.instructions.Length 3 "3 instructions"
        match instrs.instructions[1] with
        | RT.LoadVal(2, RT.DInt64 1L) -> ()
        | other -> Expect.equal 1 2 (sprintf "constant load wrong: %A" other)
      | None -> Expect.equal 1 2 "expected Some"
    }

  let parsesSubtraction =
    test "parseMinimalBody 'x - 3L' → uses int64Subtract" {
      match M.parseMinimalBody "x" "x - 3L" with
      | Some instrs ->
        match instrs.instructions[0] with
        | RT.LoadVal(_, RT.DApplicable(RT.AppNamedFn app)) ->
          match app.name with
          | RT.FQFnName.Builtin b ->
            Expect.equal b.name "int64Subtract" "uses subtract builtin"
          | _ -> Expect.equal 1 2 "expected Builtin"
        | other -> Expect.equal 1 2 (sprintf "wrong instr: %A" other)
      | None -> Expect.equal 1 2 "expected Some"
    }

  let parsesMultiplication =
    test "parseMinimalBody 'x * 7L' → uses int64Multiply" {
      match M.parseMinimalBody "x" "x * 7L" with
      | Some instrs ->
        match instrs.instructions[0] with
        | RT.LoadVal(_, RT.DApplicable(RT.AppNamedFn app)) ->
          match app.name with
          | RT.FQFnName.Builtin b ->
            Expect.equal b.name "int64Multiply" "uses multiply builtin"
          | _ -> Expect.equal 1 2 "expected Builtin"
        | _ -> Expect.equal 1 2 "expected LoadVal"
      | None -> Expect.equal 1 2 "expected Some"
    }

  let arithmeticRespectsParamName =
    test "arithmetic only matches when LHS is the param name" {
      // 'y + 1L' should not match when paramName is 'x'
      Expect.isNone (M.parseMinimalBody "x" "y + 1L") "should not match"
    }

  let parsesTwoParamArith =
    test "parseMinimalBodyN ['x';'y'] 'x * y' → Apply with both arg regs" {
      match M.parseMinimalBodyN [ "x"; "y" ] "x * y" with
      | Some instrs ->
        Expect.equal instrs.resultIn 3 "result is reg 3 (arity + 1)"
        Expect.equal instrs.registerCount 4 "4 regs total"
        Expect.equal instrs.instructions.Length 2 "load builtin + apply"
        match instrs.instructions[1] with
        | RT.Apply(_, _, _, args) ->
          let argList = NEList.toList args
          Expect.equal argList [ 0; 1 ] "apply with arg regs 0 and 1"
        | other -> Expect.equal 1 2 (sprintf "wrong second instr: %A" other)
      | None -> Expect.equal 1 2 "expected Some for 'x * y'"
    }

  let twoParamArithUsesBuiltin =
    test "parseMinimalBodyN 'x + y' uses int64Add builtin" {
      match M.parseMinimalBodyN [ "x"; "y" ] "x + y" with
      | Some instrs ->
        match instrs.instructions[0] with
        | RT.LoadVal(_, RT.DApplicable(RT.AppNamedFn app)) ->
          match app.name with
          | RT.FQFnName.Builtin b ->
            Expect.equal b.name "int64Add" "int64Add"
          | _ -> Expect.equal 1 2 "expected Builtin"
        | _ -> Expect.equal 1 2 "expected LoadVal"
      | None -> Expect.equal 1 2 "expected Some"
    }

  let twoParamArithReversedOrder =
    test "parseMinimalBodyN 'y - x' applies in the right register order" {
      match M.parseMinimalBodyN [ "x"; "y" ] "y - x" with
      | Some instrs ->
        match instrs.instructions[1] with
        | RT.Apply(_, _, _, args) ->
          // 'y - x' → y (reg 1) minus x (reg 0); arg order is [lhs; rhs] = [1; 0]
          Expect.equal (NEList.toList args) [ 1; 0 ] "y first, then x"
        | _ -> Expect.equal 1 2 "expected Apply"
      | None -> Expect.equal 1 2 "expected Some"
    }

  let parseParamNamesSingle =
    test "parseParamNames extracts one name" {
      match M.parseParamNames "(x: Int64): Int64" with
      | Some [ "x" ] -> ()
      | other -> Expect.equal 1 2 (sprintf "wrong: %A" other)
    }

  let parseParamNamesMulti =
    test "parseParamNames extracts multiple names" {
      match M.parseParamNames "(x: Int64, y: Int64): Int64" with
      | Some [ "x"; "y" ] -> ()
      | other -> Expect.equal 1 2 (sprintf "wrong: %A" other)
    }

  let parseParamNamesEmpty =
    test "parseParamNames returns Some [] for nullary sig" {
      match M.parseParamNames "(): Int64" with
      | Some [] -> ()
      | other -> Expect.equal 1 2 (sprintf "wrong: %A" other)
    }

  let parseParamNamesMalformed =
    test "parseParamNames returns None for non-sig input" {
      Expect.isNone (M.parseParamNames "just text") ""
    }

  let tests =
    testList
      "MinimalBody"
      [ parsesInt64Literal
        parsesNegativeInt64
        parsesIdentity
        stripsWhitespace
        returnsNoneOnUnknown
        respectsParamName
        parsesAddition
        parsesSubtraction
        parsesMultiplication
        arithmeticRespectsParamName
        parsesTwoParamArith
        twoParamArithUsesBuiltin
        twoParamArithReversedOrder
        parseParamNamesSingle
        parseParamNamesMulti
        parseParamNamesEmpty
        parseParamNamesMalformed ]


// ---------------------------------------------------------------------------
// EventSink (Task #14)
// ---------------------------------------------------------------------------

module Events =
  module M = LibExecution.PDDMaterializer

  let captureSink () =
    let captured = System.Collections.Generic.List<M.PDDEvent>()
    let sink : M.EventSink = fun ev -> captured.Add ev
    sink, (fun () -> List.ofSeq captured)

  let nullSinkIsNoop =
    test "nullSink swallows all events without error" {
      M.nullSink (M.MaterializeStart("foo", "gpt-4o-mini"))
      M.nullSink (M.MaterializeDone("foo", M.Real, 0))
      ()
    }

  let captureSinkCollects =
    test "a capture sink records events in order" {
      let sink, get = captureSink ()
      sink (M.MaterializeStart("foo", "m"))
      sink (M.ParseOk("foo", "(): Int64", "0L"))
      let events = get ()
      Expect.equal events.Length 2 "two events"
      match events[0] with
      | M.MaterializeStart("foo", "m") -> ()
      | _ -> Expect.equal 1 2 "wrong first event"
    }

  let currentSinkIsSettable =
    test "currentSink can be swapped and restored" {
      let sink, get = captureSink ()
      let saved = M.currentSink
      try
        M.currentSink <- sink
        // emit via the internal helper indirectly — use the public sink:
        M.currentSink (M.MaterializeDone("foo", M.Cached, 12))
        let events = get ()
        Expect.equal events.Length 1 "captured one"
      finally
        M.currentSink <- saved
    }

  let fnStateValuesExhaustive =
    test "FnState covers the documented set" {
      // Just a smoke test that we can construct each variant.
      let _ = [ M.InProgress; M.Real; M.Fake; M.Cached; M.Failed ]
      ()
    }

  let tests =
    testList
      "Events"
      [ nullSinkIsNoop
        captureSinkCollects
        currentSinkIsSettable
        fnStateValuesExhaustive ]


let tests =
  testList
    "PDD"
    [ Defaults.tests
      Pending.tests
      PMField.tests
      Integration.tests
      LLMParser.tests
      MinimalBody.tests
      Events.tests ]
