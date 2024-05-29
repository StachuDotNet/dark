module LibExecution.Execution

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

module RT = RuntimeTypes
module AT = AnalysisTypes

let noTracing (callStack : RT.CallStack) : RT.Tracing =
  { traceDval = fun _ _ -> ()
    traceExecutionPoint = fun _ -> ()
    loadFnResult = fun _ _ -> None
    storeFnResult = fun _ _ _ -> ()
    callStack = callStack }

let noTestContext : RT.TestContext =
  { sideEffectCount = 0

    exceptionReports = []
    expectedExceptionCount = 0
    postTestExecutionHook = fun _ -> () }

let createState
  (builtins : RT.Builtins)
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
    builtins = builtins

    program = program

    packageManager = packageManager
    symbolTable = Map.empty
    typeSymbolTable = Map.empty }

let executeExpr
  (state : RT.ExecutionState)
  (inputVars : RT.Symtable)
  (expr : RT.Expr)
  : Task<RT.ExecutionResult> =
  task {
    try
      try
        let state =
          { state with symbolTable = Interpreter.withGlobals state inputVars }
        let! result = Interpreter.eval state expr
        return Ok result
      with RT.RuntimeErrorException(source, rte) ->
        return Error(source, rte)
    finally
      // Does nothing in non-tests
      state.test.postTestExecutionHook state.test
  }


let executeFunction
  (state : RT.ExecutionState)
  (name : RT.FQFnName.FQFnName)
  (typeArgs : List<RT.TypeReference>)
  (args : NEList<RT.Dval>)
  : Task<RT.ExecutionResult> =
  task {
    try
      try
        let state =
          { state with
              tracing.callStack.entrypoint = RT.ExecutionPoint.Function name }
        let! result = Interpreter.callFn state name typeArgs args
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
  : Task<Result<RT.Dval, Option<RT.CallStack> * RT.RuntimeError>> =
  task {
    let fnName =
      RT.FQFnName.fqPackage
        "Darklang"
        [ "LanguageTools"; "RuntimeErrors"; "Error" ]
        "toString"
    let args = NEList.singleton (RT.RuntimeError.toDT rte)
    return! executeFunction state fnName [] args
  }

// /// Return a function to trace TLIDs (add it to state via
// /// state.tracing.traceExecutionPoint), and a mutable set which updates when the
// /// traceFn is used
// /// TRACINGTODO
// let traceTLIDs () : HashSet.HashSet<tlid> * RT.TraceExecutionPoint =
//   let touchedTLIDs = HashSet.empty ()
//   let traceExecutionPoint tlid : unit = HashSet.add tlid touchedTLIDs
//   (touchedTLIDs, traceExecutionPoint)

/// Return a function to trace Dvals (add it to state via
/// state.tracing.traceDval), and a mutable dictionary which updates when the
/// traceFn is used
let traceDvals () : Dictionary.T<id, RT.Dval> * RT.TraceDval =
  let results = Dictionary.empty ()

  let trace (id : id) (dval : RT.Dval) : unit =
    // Overwrites if present, which is what we want
    results[id] <- dval

  (results, trace)
