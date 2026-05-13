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


/// Walk RT.Instructions and collect every Pending fn reference. Used to
/// kick off parallel materializations before the interpreter runs.
let rec private collectPendings (instrs : RT.Instructions) : List<RT.FQFnName.Pending> =
  let fromInstr (i : RT.Instruction) : List<RT.FQFnName.Pending> =
    match i with
    | RT.LoadVal(_, dv) -> dvalPendings dv
    | RT.CreateLambda(_, lambdaImpl) -> collectPendings lambdaImpl.instructions
    | _ -> []
  instrs.instructions |> List.collect fromInstr

and private dvalPendings (dv : RT.Dval) : List<RT.FQFnName.Pending> =
  match dv with
  | RT.DApplicable(RT.AppNamedFn app) ->
    match app.name with
    | RT.FQFnName.Pending p -> [ p ]
    | _ -> []
  | _ -> []


/// Build a materializer wrapper that consults an in-flight cache before
/// calling the real materializer. Each pending's materialization runs as
/// a Task started ahead of time; the wrapper awaits the existing Task.
let private parallelMaterializer
  (inFlight : System.Collections.Concurrent.ConcurrentDictionary<System.Guid, Task<Option<RT.PackageFn.PackageFn>>>)
  : RT.FQFnName.Pending -> Ply<Option<RT.PackageFn.PackageFn>> =
  fun p ->
    uply {
      match inFlight.TryGetValue p.handle with
      | true, t ->
        let! fn = t |> Async.AwaitTask |> Async.StartAsTask
        return fn
      | false, _ ->
        // Wasn't pre-kicked-off (e.g. recursive materialization). Fall
        // back to the standard materializer; this preserves correctness.
        let! fn = Mat.materialize p
        return fn
    }


/// Handle `dark pdd run "<dark expression>"`.
/// Parses the expression with OnMissing.AllowPending so unresolved fn
/// names become PT.FQFnName.Pending; the runtime then materializes via
/// the LLM at call time.
let private handleRun
  (packageManager : RT.PackageManager)
  (exprStr : string)
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
    eprintfn "%s %s %s" prefix (dim "running") (green exprStr)
    eprintfn ""

    // Build PT PackageManager from the RT one for the parser.
    let ptPm = LibDB.PackageManager.pt

    // Builtins needed: int64-arith for arithmetic bodies + general utility.
    let allBuiltins : RT.Builtins =
      LibExecution.Builtin.combine
        [ Builtins.Pure.Builtin.builtins () ]
        []
    // Parse the expression with AllowPending.
    let! ptExpr =
      LibParser.Parser.parsePTExpr
        allBuiltins
        ptPm
        LibParser.NameResolver.OnMissing.AllowPending
        "<dark pdd run>"
        exprStr
      |> Ply.toTask

    // Lower PT → RT instructions.
    let instrs =
      LibExecution.ProgramTypesToRuntimeTypes.Expr.toRT Map.empty 0 None ptExpr

    // PDD parallel scheduler: walk the instructions, find every Pending
    // fn ref, and kick off materializations in parallel (Task.Run). The
    // interpreter's wrapper materializer then awaits the in-flight Task
    // instead of calling the LLM serially.
    let pendings = collectPendings instrs
    let inFlight =
      System.Collections.Concurrent.ConcurrentDictionary<
        System.Guid,
        Task<Option<RT.PackageFn.PackageFn>>>()
    if not (List.isEmpty pendings) then
      eprintfn "%s %s %s" prefix (dim "kick-off")
        (dim (sprintf "%d pendings in parallel" (List.length pendings)))
      for p in pendings do
        let t = Task.Run<Option<RT.PackageFn.PackageFn>>(fun () ->
          Mat.materialize p |> Ply.toTask)
        inFlight[p.handle] <- t

    // Build ExecutionState with the parallel materializer.
    let pm = { packageManager with materializeFn = parallelMaterializer inFlight }
    let program : RT.Program = { dbs = Map.empty }
    let notify _ _ _ _ = uply { return () }
    let reportException _ _ (_ : Metadata) (_ : exn) = uply { return () }
    let state =
      Exe.createState allBuiltins pm Exe.noTracing reportException notify
        (System.Guid.NewGuid()) program

    let! result = Exe.execute state (None, instrs)

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


/// Handle `dark prompt "<free-text request>"`. Calls the LLM to decompose
/// the request into a Darklang expression, then runs it through the
/// pdd pipeline (parser with AllowPending + materializer + interpreter).
let private handlePrompt
  (packageManager : RT.PackageManager)
  (request : string)
  : Task<int> =
  task {
    let prefix = dim "[pdd]"
    eprintfn ""
    eprintfn "%s %s %s" prefix (dim "prompt") (green request)

    // Check decompose cache first.
    match Mat.tryLookupDecomposed request with
    | Some cachedExpr ->
      eprintfn "%s %s %s" prefix (blue "decompose ↻ cached") (dim "(no LLM call)")
      eprintfn "%s %s" prefix (dim "expr →")
      eprintfn "%s   %s" prefix (green cachedExpr)
      let! code = handleRun packageManager cachedExpr
      return code
    | None ->
      eprintfn "%s %s" prefix (dim "decomposing via gpt-4o-mini...")
      let apiKey = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY")
      if System.String.IsNullOrEmpty(apiKey) then
        eprintfn "%s %s" prefix (red "OPENAI_API_KEY not set")
        return 1
      else
        let userPrompt = Mat.buildDecomposePrompt request
        let! resp =
          Mat.callOpenAI apiKey Mat.decomposeSystemPrompt userPrompt
        match resp with
        | Error e ->
          eprintfn "%s %s %s" prefix (red "decompose http error:") e
          return 1
        | Ok body ->
          match Mat.extractContent body with
          | Error e ->
            eprintfn "%s %s %s" prefix (red "decompose content error:") e
            return 1
          | Ok exprStr ->
            let trimmed = exprStr.Trim()
            let cleaned =
              // strip markdown fences if any
              if trimmed.StartsWith("```") then
                let after = trimmed.Substring(3)
                let stripLang =
                  if after.StartsWith("dark") || after.StartsWith("darklang") then
                    after.Substring(after.IndexOf('\n') + 1)
                  else after
                let endIdx = stripLang.LastIndexOf("```")
                if endIdx >= 0 then stripLang.Substring(0, endIdx).Trim()
                else stripLang.Trim()
              else trimmed
            eprintfn "%s %s" prefix (dim "decomposed →")
            eprintfn "%s   %s" prefix (green cleaned)
            // Persist for next time
            Mat.persistDecomposed request cleaned
            let! code = handleRun packageManager cleaned
            return code
  }


/// Handle `dark pdd cache list` / `clear` / `paths`.
let private handleCache (subcmd : string) : Task<int> =
  task {
    let prefix = dim "[pdd]"
    let promotedPath = "rundir/pdd-cache/promoted.jsonl"
    let decomposedPath = "rundir/pdd-cache/decomposed.jsonl"
    match subcmd with
    | "list" ->
      eprintfn "%s %s" prefix (dim "promoted (Pending → PackageFn cache):")
      if System.IO.File.Exists promotedPath then
        let lines = System.IO.File.ReadAllLines promotedPath
        for line in lines do
          if not (System.String.IsNullOrWhiteSpace line) then
            try
              let doc = System.Text.Json.JsonDocument.Parse line
              let r = doc.RootElement
              let name = r.GetProperty("name").GetString()
              let body = r.GetProperty("body").GetString()
              eprintfn "  %s %s %s" (green name) (dim "←") body
            with _ -> ()
        eprintfn "  %s %s" (dim (sprintf "%d entries" lines.Length)) (dim promotedPath)
      else
        eprintfn "  %s" (dim "(no cache file)")
      eprintfn ""
      eprintfn "%s %s" prefix (dim "decomposed (free-text → expr cache):")
      if System.IO.File.Exists decomposedPath then
        let lines = System.IO.File.ReadAllLines decomposedPath
        for line in lines do
          if not (System.String.IsNullOrWhiteSpace line) then
            try
              let doc = System.Text.Json.JsonDocument.Parse line
              let r = doc.RootElement
              let prompt = r.GetProperty("prompt").GetString()
              let expr = r.GetProperty("expr").GetString()
              eprintfn "  %s" (green prompt)
              eprintfn "    %s %s" (dim "→") expr
            with _ -> ()
        eprintfn "  %s %s" (dim (sprintf "%d entries" lines.Length)) (dim decomposedPath)
      else
        eprintfn "  %s" (dim "(no cache file)")
      return 0
    | "clear" ->
      for path in [ promotedPath; decomposedPath ] do
        if System.IO.File.Exists path then
          System.IO.File.Delete path
          eprintfn "%s %s %s" prefix (dim "deleted") path
        else
          eprintfn "%s %s %s" prefix (dim "skipped (not found)") path
      return 0
    | "paths" ->
      eprintfn "%s promoted    %s" prefix promotedPath
      eprintfn "%s decomposed  %s" prefix decomposedPath
      eprintfn "%s pdd-view    %s" prefix "rundir/pdd-view/<sessionId>.html"
      eprintfn "%s materialize %s" prefix "rundir/logs/pdd-materialize.jsonl"
      return 0
    | other ->
      eprintfn "%s unknown cache subcommand: %s" prefix other
      eprintfn "%s   dark pdd cache list   - show entries" prefix
      eprintfn "%s   dark pdd cache clear  - delete both caches" prefix
      eprintfn "%s   dark pdd cache paths  - print file locations" prefix
      return 1
  }


/// Handle `dark pdd trace list` / `last`.
let private handleTrace (subcmd : string) : Task<int> =
  task {
    let prefix = dim "[pdd]"
    let viewDir = "rundir/pdd-view"
    if not (System.IO.Directory.Exists viewDir) then
      eprintfn "%s %s" prefix (dim "no sessions yet")
      return 0
    else
      let files =
        System.IO.Directory.GetFiles(viewDir, "*.html")
        |> Array.map (fun p -> p, System.IO.File.GetLastWriteTime p)
        |> Array.sortByDescending snd
      match subcmd with
      | "last" ->
        if files.Length = 0 then
          eprintfn "%s %s" prefix (dim "no sessions yet")
          return 1
        else
          let path, _ = files[0]
          print path
          return 0
      | "list" | _ ->
        if files.Length = 0 then
          eprintfn "%s %s" prefix (dim "no sessions yet")
        else
          eprintfn "%s %s" prefix (dim (sprintf "%d sessions in %s" files.Length viewDir))
          for (path, ts) in files |> Array.truncate 20 do
            let sessionId = System.IO.Path.GetFileNameWithoutExtension path
            eprintfn "  %s %s %s" (dim (ts.ToString("HH:mm:ss"))) (green sessionId) (dim path)
          if files.Length > 20 then
            eprintfn "  %s" (dim (sprintf "... and %d more" (files.Length - 20)))
        return 0
  }


/// Entry point for `dark pdd ...` and `dark prompt ...` commands.
/// Returns Some exitCode if the arg list matched; None to fall through
/// to the normal CLI dispatch.
let tryHandle
  (packageManager : RT.PackageManager)
  (args : List<string>)
  : Task<int option> =
  task {
    match args with
    | "prompt" :: rest ->
      // `dark prompt "..."` — high-level surface: free-text in,
      // Dark-expression-via-LLM-decompose, then materialized + run.
      let request = String.concat " " rest
      if String.IsNullOrWhiteSpace request then
        eprintfn "[pdd] usage: dark prompt \"<free-text request>\""
        return Some 1
      else
        let! code = handlePrompt packageManager request
        return Some code
    | "pdd" :: "demo" :: name :: argStr :: _ ->
      match System.Int64.TryParse(argStr.TrimEnd('L', 'l')) with
      | true, v ->
        let! code = handleDemo packageManager name v
        return Some code
      | false, _ ->
        eprintfn "[pdd] arg must be an Int64 (got: %s)" argStr
        return Some 1
    | "pdd" :: "run" :: rest ->
      let exprStr = String.concat " " rest
      if String.IsNullOrWhiteSpace exprStr then
        eprintfn "[pdd] usage: dark pdd run <dark-expression>"
        return Some 1
      else
        let! code = handleRun packageManager exprStr
        return Some code
    | "pdd" :: "cache" :: subcmd :: _ ->
      let! code = handleCache subcmd
      return Some code
    | "pdd" :: "cache" :: [] ->
      let! code = handleCache "list"
      return Some code
    | "pdd" :: "trace" :: subcmd :: _ ->
      let! code = handleTrace subcmd
      return Some code
    | "pdd" :: "trace" :: [] ->
      let! code = handleTrace "list"
      return Some code
    | "pdd" :: _ ->
      eprintfn "[pdd] usage:"
      eprintfn "[pdd]   dark prompt \"<free-text request>\""
      eprintfn "[pdd]   dark pdd run <dark-expression>"
      eprintfn "[pdd]   dark pdd demo <fnName> <Int64-arg>"
      eprintfn "[pdd]   dark pdd cache (list|clear|paths)"
      eprintfn "[pdd]   dark pdd trace (list|last)"
      return Some 1
    | _ -> return None
  }
