/// For executing code with the appropriate production "Dark cloud" execution,
/// setting traces, stdlib, etc, appropriately.
/// Used by any "Cloud" service (bwdserver, queueworker, cronchecker, etc.)
module LibCloudExecution.CloudExecution

open FSharp.Control.Tasks
open System.Threading.Tasks

open Prelude

module RT = LibExecution.RuntimeTypes
module Dval = LibExecution.Dval
module PT = LibExecution.ProgramTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module AT = LibExecution.AnalysisTypes
module Exe = LibExecution.Execution

open LibCloud


let builtins : RT.Builtins =
  LibExecution.Builtin.combine
    [ BuiltinExecution.Builtin.builtins HttpClient.configuration
      BuiltinCloudExecution.Builtin.builtins
      BuiltinDarkInternal.Builtin.builtins ]
    []

let packageManager = PackageManager.packageManager

let createState
  (traceID : AT.TraceID.T)
  (program : RT.Program)
  (tracing : RT.Tracing)
  : Task<RT.ExecutionState> =
  task {
    let extraMetadata (_state : RT.ExecutionState) : Metadata =
      //let tlid, id = Option.defaultValue (0UL, 0UL) state.tracing.callStack
      [ //"callerTLID", tlid
        //"callerID", id
        "traceID", traceID
        "canvasID", program.canvasID ]

    let notify (state : RT.ExecutionState) (msg : string) (metadata : Metadata) =
      let metadata = extraMetadata state @ metadata
      LibService.Rollbar.notify msg metadata

    let sendException (state : RT.ExecutionState) (metadata : Metadata) (exn : exn) =
      let metadata = extraMetadata state @ metadata
      LibService.Rollbar.sendException None metadata exn

    return
      Exe.createState builtins packageManager tracing sendException notify program
  }

type ExecutionReason =
  /// The first time a trace is executed. This means more data should be stored and
  /// more users notified.
  | InitialExecution of PT.Handler.HandlerDesc * varname : string * RT.Dval

  /// A reexecution is a trace that already exists, being amended with new values
  | ReExecution

/// Execute handler.
/// This could be
/// - the first execution, which will
///   - have an ExecutionReason of InitialExecution
///   - initialize traces
///   - send pushes
/// - or ReExecution, which will
///   - update existing traces
///   - not send pushes
let executeHandler
  (pusherSerializer : Pusher.PusherEventSerializer)
  (h : RT.Handler.T)
  (program : RT.Program)
  (traceID : AT.TraceID.T)
  (inputVars : Map<string, RT.Dval>)
  (reason : ExecutionReason)
  : Task<RT.Dval * Tracing.TraceResults.T> =
  task {
    let tracing = Tracing.create program.canvasID h.tlid traceID

    // Store the inputs of the trace (i.e. the arguments to the handler)
    match reason with
    | InitialExecution(desc, varname, inputVar) ->
      tracing.storeTraceInput desc varname inputVar
    | ReExecution -> ()

    let! state = createState traceID program tracing.executionTracing
    let state =
      { state with tracing.callStack.entrypoint = RT.ExecutionPoint.Toplevel h.tlid }
    HashSet.add h.tlid tracing.results.tlids
    let! result = Exe.executeExpr state inputVars h.ast

    let findPackageBody (id : uuid) : Ply<Option<string * RT.Expr>> =
      packageManager.getFnByID id
      |> Ply.map (
        Option.map (fun (pkg : RT.PackageFn.T) -> string pkg.name, pkg.body)
      )

    // let sourceOf (tlid : tlid) (id : id) : Ply<string> =
    //   uply {
    //     let! data = findPackageBody tlid
    //     let mutable result = "unknown caller", "unknown body", "unknown expr"
    //     match data with
    //     | None -> ()
    //     | Some(fnName, e) ->
    //       LibExecution.RuntimeTypesAst.preTraversal
    //         (fun expr ->
    //           if RT.Expr.toID expr = id then result <- fnName, string e, string expr
    //           expr)
    //         identity
    //         identity
    //         identity
    //         identity
    //         identity
    //         identity
    //         e
    //       |> ignore<RT.Expr>
    //     let (fnName, _body, expr) = result
    //     return $"fn {fnName}\nexpr:\n{expr}\n"
    //   }

    // todo: rename
    let callStackString (_callStack : Option<RT.CallStack>) : Ply<string> =
      //match callStack with
      //| Some(tlid, id) -> sourceOf tlid id
      //| None -> Ply "No source"

      Ply "TODO"

    let error (msg : string) : RT.Dval =
      let typeName =
        RT.FQTypeName.fqPackage "Darklang" [ "Stdlib"; "Http" ] "Response"

      let fields =
        [ ("statusCode", RT.DInt64 500)
          ("headers",
           [] |> Dval.list (RT.KTTuple(RT.ValueType.string, RT.ValueType.string, [])))
          ("body", msg |> UTF8.toBytes |> Dval.byteArrayToDvalList) ]

      RT.DRecord(typeName, typeName, [], Map fields)

    // CLEANUP This is a temporary hack to make it easier to work on local dev
    // servers. We should restrict this to dev mode only
    let! result =
      task {
        match result with
        | Ok result -> return result
        | Error(originalCallStack, originalRTE) ->
          let! originalCallStack = callStackString originalCallStack

          match! Exe.runtimeErrorToString state originalRTE with
          | Ok(RT.DString msg) ->
            let msg = $"Error: {msg}\n\nSource: {originalCallStack}"
            return error msg
          | Ok result -> return result
          | Error(firstErrorCallStack, firstErrorRTE) ->
            let! firstErrorCallStack = callStackString firstErrorCallStack
            match! Exe.runtimeErrorToString state firstErrorRTE with
            | Ok(RT.DString msg) ->
              return
                error (
                  $"An error occured trying to print a runtime error."
                  + $"\n\nThe formatting error occurred in {firstErrorCallStack}. The error was:\n{msg}"
                  + $"\n\nThe original error is ({originalCallStack}) {originalRTE}"
                )
            | Ok result -> return result
            | Error(secondErrorCallStack, secondErrorRTE) ->
              let! secondErrorCallStack = callStackString secondErrorCallStack
              return
                error (
                  $"Two errors occured trying to print a runtime error."
                  + $"\n\nThe 2nd formatting error occurred in {secondErrorCallStack}. The error was:\n{secondErrorRTE}"
                  + $"\n\nThe first formatting error occurred in {firstErrorCallStack}. The error was:\n{firstErrorRTE}"
                  + $"\n\nThe original error is ({originalCallStack}) {originalRTE}"
                )
      }

    tracing.storeTraceResults ()

    match reason with
    | ReExecution -> ()
    | InitialExecution _ ->
      if tracing.enabled then
        let tlids = HashSet.toList tracing.results.tlids
        Pusher.push
          pusherSerializer
          program.canvasID
          (Pusher.NewTrace(traceID, tlids))
          None

    return (result, tracing.results)
  }

/// We call this reexecuteFunction because it always runs in an existing trace.
let reexecuteFunction
  (canvasID : CanvasID)
  (program : RT.Program)
  (traceID : AT.TraceID.T)
  (rootTLID : tlid)
  (name : RT.FQFnName.FQFnName)
  (typeArgs : List<RT.TypeReference>)
  (args : NEList<RT.Dval>)
  : Task<RT.ExecutionResult * Tracing.TraceResults.T> =
  task {
    let tracing = Tracing.create canvasID rootTLID traceID
    let! state = createState traceID program tracing.executionTracing
    let! result = Exe.executeFunction state name typeArgs args
    tracing.storeTraceResults ()
    return result, tracing.results
  }


/// Ensure library is ready to be called. Throws if it cannot initialize.
let init () : Task<unit> =
  task {
    do! packageManager.init
    return ()
  }
