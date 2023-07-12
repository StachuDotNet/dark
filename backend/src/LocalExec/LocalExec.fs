/// Run scripts locally using some builtin F#/dotnet libraries
module LocalExec.LocalExec

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Tablecloth

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module Exe = LibExecution.Execution
module StdLibCli = StdLibCli.StdLib


let builtIns : RT.BuiltIns =
  let (fns, types) =
    LibExecution.StdLib.combine
      [ StdLibExecution.StdLib.contents; StdLibCli.StdLib.contents; StdLib.contents ]
      []
      []
  { types = types |> Map.fromListBy (fun typ -> typ.name)
    fns = fns |> Map.fromListBy (fun fn -> fn.name) }

// TODO
let packageManager : RT.PackageManager = RT.PackageManager.Empty


let defaultTLID = 7UL


let execute
  (mod' : Parser.CanvasV2.PTCanvasModule)
  (symtable : Map<string, RT.Dval>)
  : Task<RT.Dval> =
  task {
    let config : RT.Config =
      { allowLocalHttpAccess = true; httpclientTimeoutInMs = 30000 }

    let program : RT.Program =
      { canvasID = System.Guid.NewGuid()
        internalFnsAllowed = false
        fns =
          mod'.fns
          |> List.map (fun fn -> PT2RT.UserFunction.toRT fn)
          |> Map.fromListBy (fun fn -> fn.name)
        types =
          mod'.types
          |> List.map (fun typ -> PT2RT.UserType.toRT typ)
          |> Map.fromListBy (fun typ -> typ.name)
        dbs = Map.empty
        secrets = [] }

    let tracing = Exe.noTracing RT.Real

    let extraMetadata (state : RT.ExecutionState) : Metadata =
      [ "executing_fn_name", state.executingFnName; "callstack", state.callstack ]

    let notify (state : RT.ExecutionState) (msg : string) (metadata : Metadata) =
      let metadata = extraMetadata state @ metadata
      let metadata =
        metadata |> List.map (fun (k, v) -> $"  {k}: {v}") |> String.concat ", "
      print $"Notification: {msg}, {metadata}"

    let sendException (state : RT.ExecutionState) (metadata : Metadata) (exn : exn) =
      let metadata = extraMetadata state @ metadata @ Exception.toMetadata exn
      let metadata =
        metadata |> List.map (fun (k, v) -> $"  {k}: {v}") |> String.concat "\n"
      print
        $"Exception: {exn.Message}\nMetadata:\n{metadata}\nStacktrace:\n{exn.StackTrace}"

    let state =
      Exe.createState
        builtIns
        packageManager
        tracing
        sendException
        notify
        defaultTLID
        program
        config

    return! Exe.executeExpr state symtable (PT2RT.Expr.toRT mod'.exprs[0])
  }

let sourceOf
  (tlid : tlid)
  (id : id)
  (modul : Parser.CanvasV2.PTCanvasModule)
  : string =
  let ast =
    if tlid = defaultTLID then
      Some modul.exprs[0]
    else
      modul.fns
      |> List.find (fun fn -> fn.tlid = tlid)
      |> Option.map (fun fn -> fn.body)
  let mutable result = "unknown"
  ast
  |> Option.tap (fun e ->
    LibExecution.ProgramTypesAst.preTraversal
      (fun expr ->
        if PT.Expr.toID expr = id then result <- string expr
        expr)
      (fun pipeExpr ->
        if PT.PipeExpr.toID pipeExpr = id then result <- string pipeExpr
        pipeExpr)
      identity
      identity
      identity
      identity
      identity
      e
    |> ignore<PT.Expr>)
  result




let initSerializers () = ()

[<EntryPoint>]
let main (args : string[]) : int =
  let name = "LocalExec"
  try
    initSerializers ()
    LibService.Init.init name
    LibService.Telemetry.Console.loadTelemetry
      name
      LibService.Telemetry.DontTraceDBQueries
    (LibBackend.Init.init LibBackend.Init.WaitForDB name).Result
    let mainFile = "/home/dark/app/backend/src/LocalExec/local-exec.dark"
    let modul = Parser.CanvasV2.parseFromFile mainFile
    let args = args |> Array.toList |> List.map RT.DString |> RT.DList
    let result = execute modul (Map [ "args", args ])
    NonBlockingConsole.wait ()
    match result.Result with
    | RT.DError(RT.SourceID(tlid, id), msg) ->
      System.Console.WriteLine $"Error: {msg}"
      System.Console.WriteLine $"Failure at: {sourceOf tlid id modul}"
      // System.Console.WriteLine $"module is: {modul}"
      // System.Console.WriteLine $"(source {tlid}, {id})"
      1
    | RT.DError(RT.SourceNone, msg) ->
      System.Console.WriteLine $"Error: {msg}"
      System.Console.WriteLine $"(source unknown)"
      1
    | RT.DInt i -> (int i)
    | dval ->
      let output = LibExecution.DvalReprDeveloper.toRepr dval
      System.Console.WriteLine
        $"Error: main function must return an int, not {output}"
      1
  with e ->
    // Don't reraise or report as LocalExec is only run interactively
    printException "Exception" [] e
    // LibService.Init.shutdown name
    NonBlockingConsole.wait ()
    1
