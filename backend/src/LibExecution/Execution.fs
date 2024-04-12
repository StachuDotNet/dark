module LibExecution.Execution

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

module RT = RuntimeTypes
module AT = AnalysisTypes

let traceNoDvals : RT.TraceDval = fun _ _ -> ()
let traceNoTLIDs : RT.TraceTLID = fun _ -> ()
let loadNoFnResults : RT.LoadFnResult = fun _ _ -> None
let storeNoFnResults : RT.StoreFnResult = fun _ _ _ -> ()

let noTracing : RT.Tracing =
  { traceDval = traceNoDvals
    traceTLID = traceNoTLIDs
    loadFnResult = loadNoFnResults
    storeFnResult = storeNoFnResults }

let noTestContext : RT.TestContext =
  { sideEffectCount = 0

    exceptionReports = []
    expectedExceptionCount = 0
    postTestExecutionHook = fun _ -> () }

let createState
  (builtIns : RT.Builtins)
  (packageManager : RT.PackageManager)
  (tracing : RT.Tracing)
  (reportException : RT.ExceptionReporter)
  (notify : RT.Notifier)
  (program : RT.Program)
  : RT.ExecutionState =
  { tracing = tracing
    test = noTestContext
    reportException = reportException
    notify = notify
    caller = None
    builtIns = builtIns

    program = program

    packageManager = packageManager
    typeSymbolTable = Map.empty }

let executeExpr
  (state : RT.ExecutionState)
  (tlid : tlid)
  (inputVars : RT.Symtable)
  (expr : RT.Expr)
  : Task<RT.ExecutionResult> =
  task {
    try
      try
        let symtable = Interpreter.withGlobals state inputVars
        let typeSymbolTable = Map.empty
        let! result = Interpreter.eval state tlid typeSymbolTable symtable expr
        return Ok result
      with RT.RuntimeErrorException(source, rte) ->
        return Error(source, rte)
    finally
      // Does nothing in non-tests
      state.test.postTestExecutionHook state.test
  }


let executeFunction
  (state : RT.ExecutionState)
  (caller : RT.Source)
  (name : RT.FQFnName.FQFnName)
  (typeArgs : List<RT.TypeReference>)
  (args : NEList<RT.Dval>)
  : Task<RT.ExecutionResult> =
  task {
    try
      try
        let typeSymbolTable = Map.empty
        let! result =
          Interpreter.callFn state typeSymbolTable caller name typeArgs args
        return Ok result
      with RT.RuntimeErrorException(source, rte) ->
        return Error(source, rte)
    finally
      // Does nothing in non-tests
      state.test.postTestExecutionHook state.test
  }

let runtimeErrorToString
  (state : RT.ExecutionState)
  (rte : RT.RuntimeError)
  : Task<Result<RT.Dval, RT.Source * RT.RuntimeError>> =
  task {
    let fnName =
      RT.FQFnName.fqPackage
        "Darklang"
        [ "LanguageTools"; "RuntimeErrors"; "Error" ]
        "toString"
        0
    let args = NEList.singleton (RT.RuntimeError.toDT rte)
    return! executeFunction state None fnName [] args
  }

/// Return a function to trace TLIDs (add it to state via
/// state.tracing.traceTLID), and a mutable set which updates when the
/// traceFn is used
let traceTLIDs () : HashSet.HashSet<tlid> * RT.TraceTLID =
  let touchedTLIDs = HashSet.empty ()
  let traceTLID tlid : unit = HashSet.add tlid touchedTLIDs
  (touchedTLIDs, traceTLID)

/// Return a function to trace Dvals (add it to state via
/// state.tracing.traceDval), and a mutable dictionary which updates when the
/// traceFn is used
let traceDvals () : Dictionary.T<id, RT.Dval> * RT.TraceDval =
  let results = Dictionary.empty ()

  let trace (id : id) (dval : RT.Dval) : unit =
    // Overwrites if present, which is what we want
    results[id] <- dval

  (results, trace)
