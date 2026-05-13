/// PDD CLI subcommand — `dark pdd <subcommand> ...`.
///
/// H1 from pdd-thinking/README.md: gives users an end-user surface to
/// invoke the materializer and watch the runtime materialize code.
///
/// Subcommands:
///   dark pdd demo <fnName> <int64-arg>
///     Hand-constructs an Apply-of-Pending program (same shape as the
///     test harness) with `<fnName>` as the pending fn name and a single
///     Int64 arg. Installs PDDMaterializer.materialize as the materializer
///     (real OpenAI call). Installs PDDHTMLView as the EventSink. Runs.
///     Prints result + the HTML view path.
///
/// A future iteration adds:
///   dark pdd run <expr>     -- parse arbitrary Dark; unresolved names → Pending
///   dark pdd trace show <id>
///   dark pdd promote <hash>
module Cli.PddCommand

open System
open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
module RT = LibExecution.RuntimeTypes
module Exe = LibExecution.Execution
module View = LibExecution.PDDHTMLView
module Mat = LibExecution.PDDMaterializer


/// ANSI colors for stderr logging.
let private ansi (code : string) (text : string) : string =
  sprintf "\x1b[%sm%s\x1b[0m" code text
let private dim s = ansi "2" s
let private green s = ansi "32" s
let private yellow s = ansi "33" s
let private red s = ansi "31" s
let private blue s = ansi "34" s


/// Build the same Apply-of-Pending RT.Instructions shape as the test
/// harness: register 0 holds the DApplicable wrapping the Pending fn,
/// register 1 holds the Int64 arg, Apply lands the result in register 2.
let private buildPendingCall (pendingName : string) (arg : int64) : RT.Instructions =
  let pending = RT.FQFnName.fqPending pendingName
  let applicable : RT.ApplicableNamedFn =
    { name = pending
      typeSymbolTable = Map.empty
      typeArgs = []
      argsSoFar = [] }
  { registerCount = 3
    resultIn = 2
    instructions =
      [ RT.LoadVal(0, RT.DApplicable(RT.AppNamedFn applicable))
        RT.LoadVal(1, RT.DInt64 arg)
        RT.Apply(2, 0, [], NEList.singleton 1) ] }


/// stderr sink — prints colorized event lines as they fire.
let private stderrSink : Mat.EventSink =
  fun ev ->
    let prefix = dim "[pdd]"
    match ev with
    | Mat.MaterializeStart(name, model) ->
      eprintfn "%s %s %s %s" prefix (yellow "▸ start") name (dim model)
    | Mat.LLMResponse(name, elapsed, _body) ->
      eprintfn "%s %s %s %s" prefix (blue "▸ llm") name (dim (sprintf "%dms" elapsed))
    | Mat.ParseOk(_name, sig_, body) ->
      eprintfn "%s %s %s %s" prefix (dim "▸ parsed") (dim sig_) (dim body)
    | Mat.CompileBody(name, kind, _registerCount) ->
      eprintfn "%s %s %s %s" prefix (dim "▸ compiled") name (dim kind)
    | Mat.MaterializeDone(name, state, elapsed) ->
      let badge =
        match state with
        | Mat.Real -> green "✓ real"
        | Mat.Fake -> dim "▼ fake"
        | Mat.Cached -> blue "↻ cached"
        | Mat.Failed -> red "✗ failed"
        | Mat.InProgress -> yellow "⋯ in-progress"
      eprintfn "%s %s %s %s" prefix badge name (dim (sprintf "%dms" elapsed))
    | Mat.MaterializeFailed(name, reason) ->
      eprintfn "%s %s %s %s" prefix (red "✗ failed") name (dim reason)


/// Combined sink: writes to stderr AND updates the HTML view.
let private combinedSink (htmlSink : Mat.EventSink) : Mat.EventSink =
  fun ev ->
    stderrSink ev
    htmlSink ev


/// Handle `dark pdd demo <fnName> <int64>`.
let private handleDemo
  (packageManager : RT.PackageManager)
  (fnName : string)
  (arg : int64)
  : Task<int> =
  task {
    let sessionId = (System.Guid.NewGuid().ToString("N")).Substring(0, 8)
    let htmlPath = View.defaultPathFor sessionId
    let session = View.createSession sessionId htmlPath
    let htmlSink = View.sinkFor session
    Mat.currentSink <- combinedSink htmlSink

    let prefix = dim "[pdd]"
    eprintfn ""
    eprintfn "%s %s %s = %s" prefix (dim "session") (green sessionId) (dim htmlPath)
    eprintfn "%s %s %s %s %s" prefix (dim "running") (green fnName) (dim "with arg") (green (sprintf "%dL" arg))
    eprintfn ""

    // Install the real OpenAI materializer.
    let pm = { packageManager with materializeFn = Mat.materialize }

    // Build the program (Apply of Pending with one Int64 arg).
    let instrs = buildPendingCall fnName arg

    // Wire ExecutionState with PDD-aware PM.
    let program : RT.Program = { dbs = Map.empty }
    let notify _ _ _ _ = uply { return () }
    let reportException _ _ (_ : Metadata) (_ : exn) = uply { return () }
    let testBuiltins : RT.Builtins =
      // Need int64-arith builtins for arithmetic bodies. Reuse the CLI's
      // builtin set rather than rebuilding.
      LibExecution.Builtin.combine
        [ Builtins.Pure.Builtin.builtins () ]
        []
    let state =
      Exe.createState testBuiltins pm Exe.noTracing reportException notify
        (System.Guid.NewGuid()) program

    let! result = Exe.execute state (None, instrs)

    // Close the session, restore sink, print final result.
    View.close session
    Mat.currentSink <- Mat.nullSink

    eprintfn ""
    match result with
    | Ok dv ->
      eprintfn "%s %s %A" prefix (green "result:") dv
      print (sprintf "%A" dv)
      return 0
    | Error(rte, _) ->
      eprintfn "%s %s %A" prefix (red "error:") rte
      return 1
  }


/// Entry point for `dark pdd ...` commands. Returns Some exitCode if
/// the arg list matched a pdd subcommand; None to fall through to the
/// normal CLI dispatch.
let tryHandle
  (packageManager : RT.PackageManager)
  (args : List<string>)
  : Task<int option> =
  task {
    match args with
    | "pdd" :: "demo" :: name :: argStr :: _ ->
      match System.Int64.TryParse(argStr.TrimEnd('L', 'l')) with
      | true, v ->
        let! code = handleDemo packageManager name v
        return Some code
      | false, _ ->
        eprintfn "[pdd] arg must be an Int64 (got: %s)" argStr
        return Some 1
    | "pdd" :: _ ->
      eprintfn "[pdd] usage: dark pdd demo <fnName> <Int64>"
      eprintfn "[pdd]   Materializes the named Pending fn via gpt-4o-mini and"
      eprintfn "[pdd]   applies it to the given Int64 arg. Logs events to stderr"
      eprintfn "[pdd]   and writes an HTML view to rundir/pdd-view/<id>.html"
      return Some 1
    | _ -> return None
  }
