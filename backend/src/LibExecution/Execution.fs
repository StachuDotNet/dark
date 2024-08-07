module LibExecution.Execution

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

module RT = RuntimeTypes

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
      RT.FQFnName.fqPackage PackageIDs.Fn.LanguageTools.RuntimeErrors.Error.toString
    let args = NEList.singleton (RT.RuntimeError.toDT rte)
    return! executeFunction state fnName [] args
  }


let exprString
  (state : RT.ExecutionState)
  (expr : RT.Expr)
  (id : Option<id>)
  : Ply<string> =
  match id with
  | None -> Ply "Unknown Expr"
  | Some id ->
    let mutable foundExpr = None

    RuntimeTypesAst.preTraversal
      (fun expr ->
        if RT.Expr.toID expr = id then foundExpr <- Some expr
        expr)
      identity
      identity
      identity
      identity
      identity
      identity
      expr
    |> ignore<RT.Expr>

    let prettyPrint (expr : RT.Expr) : Ply<string> =
      uply {
        let fnName =
          RT.FQFnName.fqPackage PackageIDs.Fn.PrettyPrinter.RuntimeTypes.expr
        let args = NEList.singleton (RuntimeTypesToDarkTypes.Expr.toDT expr)

        match! executeFunction state fnName [] args with
        | Ok(RT.DString s) -> return s
        | _ -> return string expr
      }

    match foundExpr with
    | None ->
      uply {
        let! pretty = prettyPrint expr
        return $"Root Expr:\n{pretty}"
      }
    | Some expr -> prettyPrint expr


// TODO: consider dumping symTable while we're at it.
// (beware of secrets in scope, though)
let callStackString
  (state : RT.ExecutionState)
  (callStack : Option<RT.CallStack>)
  : Ply<string> =
  match callStack with
  | None -> Ply "No call stack"
  | Some cs ->
    let (executionPoint, exprId) = cs.lastCalled

    let handleFn (fn : Option<RT.PackageFn.PackageFn>) : Ply<string> =
      uply {
        match fn with
        | None -> return "<Couldn't find package function>"
        | Some fn ->
          let fnName = string fn.id
          let! exprString = exprString state fn.body exprId
          return fnName + ": " + exprString
      }

    match executionPoint with
    | RT.ExecutionPoint.Script -> Ply "Input script"
    | RT.ExecutionPoint.Toplevel tlid -> Ply $"Toplevel {tlid}"
    | RT.ExecutionPoint.Function fnName ->
      match fnName with
      | RT.FQFnName.Package name ->
        state.packageManager.getFn name |> Ply.bind handleFn
      | RT.FQFnName.Builtin name -> Ply $"Builtin {name}"


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


let rec rteToString
  (state : RT.ExecutionState)
  (rte : RT.RuntimeError)
  : Ply<string> =
  uply {
    let errorMessageFn =
      RT.FQFnName.fqPackage
        PackageIDs.Fn.LanguageTools.RuntimeErrors.Error.toErrorMessage

    let rte = RT.RuntimeError.toDT rte

    let! rteMessage = executeFunction state errorMessageFn [] (NEList.ofList rte [])

    match rteMessage with
    | Ok(RT.DString msg) -> return msg
    | Ok(other) -> return string other
    | Error(_, rte) ->
      debuG "Error converting RTE to string" rte
      return! rteToString state rte
  }
