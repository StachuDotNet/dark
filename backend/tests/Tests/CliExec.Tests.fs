/// Executions: a run kept beside its trace, resumed and forked by replaying the log of its
/// effectful calls (`docs/processes.md`, "Executions"). Driven through the CLI, since that is
/// where a run is made, suspended and taken up again.
module Tests.CliExec

open Expecto
open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Fumble
open LibDB.Sqlite

open TestUtils.TestUtils
open Tests.CliTestHarness
open Tests.CliDsl

module RT = LibExecution.RuntimeTypes
module Executions = LibDB.Executions


let private latest () : Task<Executions.Execution> =
  task {
    let! rows = Executions.list 1
    match rows with
    | e :: _ -> return e
    | [] -> return failtest "no execution was recorded"
  }

/// An execution's id as `exec` shows it: the first eight characters.
let private prefixOf (e : Executions.Execution) : string =
  (string e.id).Substring(0, 8)

let private latestPrefix () : Task<string> =
  task {
    let! e = latest ()
    return prefixOf e
  }

/// Two uuids, so a replay is told apart from a rerun by its output.
let private twoUuids =
  "let a = Stdlib.Uuid.generate ()\nlet b = Stdlib.Uuid.generate ()\nStdlib.String.join [ Stdlib.Uuid.toString a, Stdlib.Uuid.toString b ] \" \""

let private words (s : string) : string list =
  s.Split(' ', System.StringSplitOptions.RemoveEmptyEntries) |> List.ofArray


let private recordThenResume =
  cliTestWithFreshTraces
    "a run is kept, and resume replays its effects rather than redoing them"
    (fun state ->
      task {
        let! first = runCli state [ "eval"; twoUuids ]
        let! listed = runCli state [ "exec" ]
        Expect.stringContains listed "done" $"the run is listed, done ({first})"
        Expect.stringContains listed "eval" "with its entry"
        let! prefix = latestPrefix ()
        let! resumed = runCli state [ "exec"; "resume"; prefix ]
        // The last line is the value; the first says what is being resumed.
        let last = resumed.Split('\n') |> Array.last
        Expect.equal
          (words last)
          (words first)
          "the same two uuids: both calls answered from the log"
        let! e = latest ()
        Expect.equal e.status Executions.Done "and the run is done again"
      })


let private forkDivergesAfterThePosition =
  cliTestWithFreshTraces
    "a fork keeps the log up to a position and goes live after it"
    (fun state ->
      task {
        let! first = runCli state [ "eval"; twoUuids ]
        let! parent = latest ()
        let prefix = prefixOf parent
        // Position 1: the first uuid's row (seq 0) is kept, the second (seq 1) is not.
        let! forked = runCli state [ "exec"; "fork"; prefix; "--at"; "1" ]
        Expect.stringContains forked "forked" "the fork is announced"
        let! child = latest ()
        Expect.equal child.status Executions.Suspended "a fork waits to be resumed"
        Expect.equal
          child.parent
          (Some(parent.id, 1L))
          "and knows where it came from"
        let! shown = runCli state [ "exec"; "show"; prefixOf child ]
        Expect.stringContains shown "forked from" "show says so"
        let! resumed = runCli state [ "exec"; "resume"; prefixOf child ]
        let last = resumed.Split('\n') |> Array.last
        match words first, words last with
        | [ a1; b1 ], [ a2; b2 ] ->
          Expect.equal a2 a1 "the first uuid came from the log"
          Expect.notEqual b2 b1 "the second was generated afresh"
        | _ -> failtest $"unexpected outputs: {first} / {last}"
      })


let private suspendThenResume =
  cliTestWithFreshTraces
    "a run suspended midway resumes with its first effects answered from the log"
    (fun state ->
      task {
        // The second uuid comes after a pause long enough to suspend during.
        let program =
          "let a = Stdlib.Uuid.generate ()\nlet _ = Stdlib.Cli.Posix.sleep 800.0\nlet b = Stdlib.Uuid.generate ()\nStdlib.String.join [ Stdlib.Uuid.toString a, Stdlib.Uuid.toString b ] \" \""
        let running = runCli state [ "eval"; program ]
        // Until the run is in the foreground, then a moment more for the first uuid (made in
        // the first few milliseconds; the tracer holds it until a flush, so there is nothing
        // to poll for).
        let deadline = System.DateTime.UtcNow.AddSeconds 5.
        while (Executions.Foreground.currentId ()).IsNone
              && System.DateTime.UtcNow < deadline do
          do! Task.Delay 10
        do! Task.Delay 300
        let! suspended = Executions.Foreground.suspend ()
        Expect.isSome suspended "the foreground run was suspended"
        let! e = latest ()
        Expect.equal e.status Executions.Suspended "and marked so"
        let! log = Executions.log e.traceId
        Expect.equal
          (List.length log)
          1
          "the log has the first uuid and nothing after the pause"
        // The interrupted run goes on (in the CLI it would have exited) and must not overwrite
        // what the suspend stored.
        let! first = running
        let! e = latest ()
        Expect.equal
          e.status
          Executions.Suspended
          "the run's own ending left the suspend alone"
        let! resumed = runCli state [ "exec"; "resume"; prefixOf e ]
        let last = resumed.Split('\n') |> Array.last
        match words first, words last with
        | [ a1; _ ], [ a2; b2 ] ->
          Expect.equal a2 a1 "the first uuid came from the log"
          Expect.isTrue (b2.Length > 30) "the second was made live"
        | _ -> failtest $"unexpected outputs: {first} / {last}"
      })


let private replayAfterAnEdit =
  cliTestWithFreshTraces
    "replay after a package edit runs the new pure code against the old effects"
    (fun state ->
      task {
        do! start state
        do! fn state "Tests.Exec.shape" "(s: String) : String = \"v1:\" ++ s"
        do! commit state "shape v1"
        let! first =
          runCli
            state
            [ "eval"
              "Tests.Exec.shape (Stdlib.Uuid.toString (Stdlib.Uuid.generate ()))" ]
        Expect.stringStarts first "v1:" "the first run went through v1"
        do! fn state "Tests.Exec.shape" "(s: String) : String = \"v2:\" ++ s"
        do! commit state "shape v2"
        let! prefix = latestPrefix ()
        let! resumed = runCli state [ "exec"; "resume"; prefix ]
        let last = resumed.Split('\n') |> Array.last
        Expect.stringStarts last "v2:" "the resume ran the new code"
        let uuidOf (s : string) = s.Substring(s.IndexOf(':') + 1).TrimEnd('"')
        Expect.equal
          (uuidOf last)
          (uuidOf first)
          "against the uuid the old run made"
      })


let private exportImportResume =
  cliTestWithFreshTraces
    "a run exported as a bundle, wiped and imported resumes with the same log"
    (fun state ->
      task {
        let! first = runCli state [ "eval"; twoUuids ]
        let! e = latest ()
        let prefix = prefixOf e
        let! bundle = Executions.Bundle.export e.id
        let text =
          match bundle with
          | Ok t -> t
          | Error m -> failtest $"export failed: {m}"
        Expect.stringStarts text "dark-execution-bundle v1" "a versioned bundle"
        // Wipe it, as another machine would never have had it.
        let tid = string e.traceId
        Sql.executeTransactionSync
          [ "DELETE FROM executions WHERE id = @id", [ [ "id", Sql.uuid e.id ] ]
            "DELETE FROM trace_fn_calls WHERE trace_id = @t",
            [ [ "t", Sql.string tid ] ]
            "DELETE FROM traces WHERE id = @t", [ [ "t", Sql.string tid ] ] ]
        |> ignore<List<int>>
        let! gone = Executions.get e.id
        Expect.isNone gone "wiped"
        let! imported = Executions.Bundle.import text
        match imported with
        | Ok id -> Expect.equal id e.id "the id is kept"
        | Error m -> failtest $"import failed: {m}"
        let! back = Executions.get e.id
        Expect.equal
          (back |> Option.map (fun b -> b.status))
          (Some e.status)
          "imported with the status it had"
        let! resumed = runCli state [ "exec"; "resume"; prefix ]
        let last = resumed.Split('\n') |> Array.last
        Expect.equal
          (words last)
          (words first)
          "the same two uuids: the imported log answered both calls"
      })


let private retentionKeepsTheNewestAndTheSuspended =
  cliTestWithFreshTraces
    "retention drops the oldest runs past trace.keep but never a suspended one"
    (fun state ->
      task {
        let keep, bytes =
          LibDB.Tracing.TraceRetention.keep, LibDB.Tracing.TraceRetention.maxBytes
        LibDB.Tracing.TraceRetention.setForTesting 2L 0L
        try
          // Four runs; the first is suspended by hand so it must survive.
          let! _ = runCli state [ "eval"; "1L" ]
          let! first = latest ()
          Executions.setStatus first.id Executions.Suspended
          let! _ = runCli state [ "eval"; "2L" ]
          let! _ = runCli state [ "eval"; "3L" ]
          // The pass runs at most every ten seconds; the seam reset its clock, and it ran on
          // the fourth store, which is the one past the cap.
          LibDB.Tracing.TraceRetention.setForTesting 2L 0L
          let! _ = runCli state [ "eval"; "4L" ]
          let! traces =
            Sql.query "SELECT id FROM traces ORDER BY timestamp"
            |> Sql.executeAsync (fun read -> read.string "id")
          Expect.isTrue
            (List.contains (string first.traceId) traces)
            "the suspended run's trace is kept whatever its age"
          Expect.isLessThanOrEqual
            (List.length traces)
            3
            "at most the cap plus the exempt one"
          let! rows = Executions.list 10
          Expect.isTrue
            (rows |> List.exists (fun e -> e.id = first.id))
            "the suspended execution is still listed"
          Expect.isTrue
            (rows |> List.forall (fun e -> List.contains (string e.traceId) traces))
            "every listed execution still has its trace: the rows went together"
        finally
          LibDB.Tracing.TraceRetention.setForTesting keep bytes
      })


let private byteCapSparesTheRunThatTrippedIt =
  cliTestWithFreshTraces
    "the byte cap drops older logs, never the newest"
    (fun state ->
      task {
        // One byte: every stored log is over the cap on its own. The pass must keep the
        // newest trace, or a run's own log would go the moment it was written. Called
        // directly: the pass after a store only scans bytes once there are fifty traces.
        let! _ = runCli state [ "eval"; "Stdlib.printLine \"one\"" ]
        let! _ = runCli state [ "eval"; "Stdlib.printLine \"two\"" ]
        let! newest = latest ()
        let went = LibDB.Tracing.TraceRetention.prune None (Some 1L)
        let! traces =
          Sql.query "SELECT id FROM traces"
          |> Sql.executeAsync (fun read -> read.string "id")
        Expect.equal went 1 "the older one went"
        Expect.equal
          traces
          [ string newest.traceId ]
          "only the newest survives, and it does"
      })


let private replayEchoesAndRefuses =
  cliTestWithFreshTraces
    "a resume echoes what the old run printed, and stops at a call it cannot reproduce"
    (fun state ->
      task {
        // Printed once, then a uuid: the echo must show the line again, the uuid must be the log's.
        let! first =
          runCli
            state
            [ "eval"
              "let _ = Stdlib.printLine \"hello from the log\"\nStdlib.Uuid.toString (Stdlib.Uuid.generate ())" ]
        let! prefix = latestPrefix ()
        let! resumed = runCli state [ "exec"; "resume"; prefix ]
        Expect.stringContains
          resumed
          "hello from the log"
          "the logged print is echoed"
        let uuid = first.Split('\n') |> Array.last
        Expect.stringContains resumed uuid "the uuid came from the log"
        // A spawned process is a live handle the log cannot hand back. The grant lands in the
        // shared store, so it is taken back whatever happens.
        let! _ = runCli state [ "permissions"; "allow"; "process"; "/bin/bash" ]
        try
          let! _ =
            runCli
              state
              [ "eval"
                "let h = Stdlib.Cli.Process.spawn \"sleep 0\"\nStdlib.Cli.Process.terminate h" ]
          let! spawned = latest ()
          let! logBefore = Executions.log spawned.traceId
          let! refused = runCli state [ "exec"; "resume"; prefixOf spawned ]
          Expect.stringContains refused "cannot resume past step" "the resume stops"
          Expect.stringContains refused "cliSpawnProcess" "naming the call"
          let! after = Executions.get spawned.id
          Expect.equal
            (after |> Option.map (fun e -> e.status))
            (Some spawned.status)
            "and the run is left with the status it had"
          let! logAfter = Executions.log spawned.traceId
          Expect.equal
            (List.length logAfter)
            (List.length logBefore)
            "with its log as it was"
        finally
          (runCli state [ "permissions"; "remove"; "process"; "/bin/bash" ]).Wait()
      })


/// The header half of the redaction, at the unit: the names in the table are blanked in the
/// arguments a row stores, whatever case they were written in, and nothing else is touched.
let private secretHeadersAreRedacted =
  testTask "a secret header's value is replaced in the stored arguments" {
    let headers =
      RT.DList(
        RT.ValueType.Unknown,
        [ RT.DTuple(RT.DString "Authorization", RT.DString "Bearer abc123", [])
          RT.DTuple(RT.DString "cookie", RT.DString "session=xyz", [])
          RT.DTuple(RT.DString "Accept", RT.DString "application/json", []) ]
      )
    let stored =
      LibDB.Tracing.Redact.args
        "httpClientRead"
        [ RT.DString "GET"; RT.DString "https://example.com/"; headers ]
    match stored with
    | [ _; _; RT.DList(_, items) ] ->
      let value (name : string) =
        items
        |> List.tryPick (fun item ->
          match item with
          | RT.DTuple(RT.DString n, RT.DString v, []) when n = name -> Some v
          | _ -> None)
      Expect.equal
        (value "Authorization")
        (Some "[redacted]")
        "the bearer token is gone"
      Expect.equal (value "cookie") (Some "[redacted]") "the cookie is gone"
      Expect.equal
        (value "Accept")
        (Some "application/json")
        "an ordinary header is untouched"
    | other -> failtest $"expected three arguments with a header list, got {other}"
  }


/// `dark ps` as a command: the captioned table, and a refusal that says what to do.
/// Secrets: a request header the log must not keep, and an env read whose value it must not
/// keep. The header is redacted in the stored arguments; the env read is not stored at all and
/// is performed again on a resume, so the run still replays.
let private secretsAreNotInTheLog =
  cliTestWithFreshTraces
    "an authorization header is redacted in the log, and an env read is run again on resume"
    (fun state ->
      task {
        // At the shipped level (`effects`), which is what redaction is about: under `on` every
        // call and its values are recorded, wrappers included, which `dark docs processes` says.
        LibDB.Tracing.TraceDetail.setForTesting LibDB.Tracing.TraceDetail.Effects
        let! _ =
          runCli
            state
            [ "permissions"; "allow"; "env"; "read"; "'DARK_TEST_SECRET'" ]
        try
          // The program never prints the value (stdout IS logged, by design); it prints its
          // length, so the test can tell the two runs apart without putting a secret in the log.
          System.Environment.SetEnvironmentVariable(
            "DARK_TEST_SECRET",
            "twelve-chars"
          )
          let! _ =
            runCli
              state
              [ "eval"
                "match Stdlib.Env.get \"DARK_TEST_SECRET\" with | Some v -> Stdlib.printLine (Stdlib.Int.toString (Stdlib.String.length v)) | None -> Stdlib.printLine \"unset\"" ]
          let! e = latest ()
          let! rows =
            Sql.query
              "SELECT fn_hash, args, result FROM trace_fn_calls WHERE trace_id = @t"
            |> Sql.parameters [ "t", Sql.string (string e.traceId) ]
            |> Sql.executeAsync (fun read ->
              (read.stringOrNone "fn_hash" |> Option.defaultValue ""),
              read.bytes "args",
              read.bytes "result")
          let envRows =
            rows |> List.filter (fun (fn, _, _) -> fn = "environmentGet")
          Expect.isNonEmpty envRows "the env read is in the log"
          for (_, _, result) in envRows do
            let dv =
              LibSerialization.Binary.Serialization.RT.Dval.deserialize "t" result
            Expect.equal dv RT.DUnit "with no value in it"
          let bytes = rows |> List.collect (fun (_, a, r) -> [ a; r ])
          for b in bytes do
            Expect.isFalse
              ((UTF8.ofBytesWithReplacement b).Contains "twelve-chars")
              "the value is nowhere in any row"
          // The resume runs the env read again, so it still answers, and the run replays.
          System.Environment.SetEnvironmentVariable(
            "DARK_TEST_SECRET",
            "nineteen-chars-long"
          )
          let! resumed = runCli state [ "exec"; "resume"; prefixOf e ]
          Expect.stringContains resumed "19" "the resume read the environment again"
        finally
          LibDB.Tracing.TraceDetail.setForTesting LibDB.Tracing.TraceDetail.On
          System.Environment.SetEnvironmentVariable("DARK_TEST_SECRET", null)
          (runCli
            state
            [ "permissions"; "remove"; "env"; "read"; "'DARK_TEST_SECRET'" ])
            .Wait()
      })


let private psListsTheTree =
  cliTestWithFreshTraces
    "ps prints this dark's table and refuses an unknown id by name"
    (fun state ->
      task {
        // The harness runs commands unscheduled, so the table is empty here; the shape is
        // what this pins. The tree itself is `Scheduler.Tests`' and the demo's.
        let! out = runCli state [ "ps" ]
        Expect.stringContains out "this dark (pid" "the local table is captioned"
        // The columns are as wide as their widest cell (and the header is bold), so it is
        // matched word by word.
        let plain =
          System.Text.RegularExpressions.Regex.Replace(out, "\u001b\\[[0-9;]*m", "")
        let header = plain.Split('\n') |> Array.find (fun l -> l.StartsWith "id ")
        for column in [ "entry"; "status"; "instructions"; "parent" ] do
          Expect.stringContains header column "with its columns"
        let! missing = runCli state [ "ps"; "show"; "nope" ]
        Expect.stringContains
          missing
          "no process whose id starts with nope"
          "an unknown id is refused, not answered plausibly"
      })


let tests =
  [ recordThenResume
    forkDivergesAfterThePosition
    suspendThenResume
    replayAfterAnEdit
    exportImportResume
    retentionKeepsTheNewestAndTheSuspended
    byteCapSparesTheRunThatTrippedIt
    replayEchoesAndRefuses
    secretsAreNotInTheLog
    secretHeadersAreRedacted
    psListsTheTree ]
