/// Builtins for running CLI and Daemon apps
module BuiltinCli.Libs.Apps

open System
open System.Threading.Tasks
open FSharp.Control.Tasks
open System.Collections.Concurrent
open System.IO

open Prelude
open LibExecution.RuntimeTypes
module VT = LibExecution.ValueType
module Dval = LibExecution.Dval
module Builtin = LibExecution.Builtin
module Interpreter = LibExecution.Interpreter
module Execution = LibExecution.Execution
module PackageIDs = LibExecution.PackageIDs
open Builtin.Shortcuts


// Type names for Apps types
let subscriptionTypeName =
  FQTypeName.fqPackage PackageIDs.Type.Cli.Apps.subscription

let updateResultTypeName =
  FQTypeName.fqPackage PackageIDs.Type.Cli.Apps.updateResult

let cliAppTypeName =
  FQTypeName.fqPackage PackageIDs.Type.Cli.Apps.cliApp

let daemonEventTypeName =
  FQTypeName.fqPackage PackageIDs.Type.Cli.Apps.daemonEvent

let daemonAppTypeName =
  FQTypeName.fqPackage PackageIDs.Type.Cli.Apps.daemonApp

let daemonStatusTypeName =
  FQTypeName.fqPackage PackageIDs.Type.Cli.Apps.daemonStatus


/// Helper to execute a Darklang applicable (lambda or named fn) with arguments
let executeApplicable
  (exeState : ExecutionState)
  (applicable : Dval)
  (typeArgs : List<TypeReference>)
  (args : List<Dval>)
  : Task<ExecutionResult> =
  task {
    // Build instructions to load the applicable and args, then apply
    let mutable rc = 0
    let resultReg = rc
    rc <- rc + 1

    // Load the applicable
    let applicableReg = rc
    rc <- rc + 1
    let loadApplicable = LoadVal(applicableReg, applicable)

    // Load each argument
    let argInstrs, argRegs =
      args
      |> List.fold
        (fun (instrs, regs) arg ->
          let reg = rc
          rc <- rc + 1
          instrs @ [ LoadVal(reg, arg) ], regs @ [ reg ])
        ([], [])

    // Build apply instruction
    let applyInstr =
      match argRegs with
      | [] ->
        // No args - still need to apply with unit
        let unitReg = rc
        rc <- rc + 1
        let loadUnit = LoadVal(unitReg, DUnit)
        [ loadUnit; Apply(resultReg, applicableReg, typeArgs, NEList.singleton unitReg) ]
      | first :: rest ->
        [ Apply(resultReg, applicableReg, typeArgs, NEList.ofList first rest) ]

    let allInstrs = [ loadApplicable ] @ argInstrs @ applyInstr

    let instrs : Instructions =
      { registerCount = rc
        instructions = allInstrs
        resultIn = resultReg }

    return! Execution.executeExpr exeState instrs
  }


/// Run a CLI app with the TEA (The Elm Architecture) pattern
let runCliApp
  (exeState : ExecutionState)
  (appRecord : Dval)
  (args : List<Dval>)
  : Task<Dval> =
  task {
    // Extract fields from the CliApp record
    let fields =
      match appRecord with
      | DRecord(_, _, _, fields) -> fields
      | _ -> Exception.raiseInternal "Expected CliApp record" [ "got", appRecord ]

    let getField name =
      Map.find name fields
      |> Option.defaultWith (fun () ->
        Exception.raiseInternal $"Missing field {name}" [])

    let initFn = getField "init"
    let updateFn = getField "update"
    let viewFn = getField "view"
    let subscriptionsFn = getField "subscriptions"

    // Convert args to a Darklang list
    let argsList =
      args |> List.map (fun a -> a) |> fun l -> DList(VT.string, l)

    // Call init to get initial model
    let! initResult = executeApplicable exeState initFn [] [ argsList ]
    let mutable model =
      match initResult with
      | Ok m -> m
      | Error(rte, _) ->
        Exception.raiseInternal "Init function failed" [ "error", rte ]

    // Main loop
    let mutable exitCode : int64 option = None

    while exitCode.IsNone do
      // Get subscriptions for current model
      let! subsResult = executeApplicable exeState subscriptionsFn [] [ model ]
      let subscriptions =
        match subsResult with
        | Ok(DList(_, subs)) -> subs
        | Ok other ->
          Exception.raiseInternal "subscriptions must return List" [ "got", other ]
        | Error(rte, _) ->
          Exception.raiseInternal "subscriptions failed" [ "error", rte ]

      // Render the view
      let! viewResult = executeApplicable exeState viewFn [] [ model ]
      let viewLines =
        match viewResult with
        | Ok(DList(_, lines)) ->
          lines
          |> List.choose (function
            | DString s -> Some s
            | _ -> None)
        | Ok other ->
          Exception.raiseInternal "view must return List<String>" [ "got", other ]
        | Error(rte, _) ->
          Exception.raiseInternal "view failed" [ "error", rte ]

      // Clear screen and render
      Console.Clear()
      for line in viewLines do
        Console.WriteLine(line)

      // Process keyboard subscription if present
      let keyboardSub =
        subscriptions
        |> List.tryPick (function
          | DEnum(_, _, _, "Keyboard", [ handler ]) -> Some handler
          | _ -> None)

      match keyboardSub with
      | Some handler ->
        // Read a key
        let readKey = Console.ReadKey(true)

        // Build KeyRead record
        let altHeld =
          (readKey.Modifiers &&& ConsoleModifiers.Alt) <> ConsoleModifiers.None
        let shiftHeld =
          (readKey.Modifiers &&& ConsoleModifiers.Shift) <> ConsoleModifiers.None
        let ctrlHeld =
          (readKey.Modifiers &&& ConsoleModifiers.Control) <> ConsoleModifiers.None

        let modifiersTypeName =
          FQTypeName.fqPackage PackageIDs.Type.Stdlib.Cli.Stdin.modifiers
        let modifiers =
          DRecord(
            modifiersTypeName,
            modifiersTypeName,
            [],
            Map [ "alt", DBool altHeld; "shift", DBool shiftHeld; "ctrl", DBool ctrlHeld ]
          )

        let keyCaseName =
          match readKey.Key with
          | ConsoleKey.Backspace -> "Backspace"
          | ConsoleKey.Tab -> "Tab"
          | ConsoleKey.Enter -> "Enter"
          | ConsoleKey.Escape -> "Escape"
          | ConsoleKey.Spacebar -> "Spacebar"
          | ConsoleKey.PageUp -> "PageUp"
          | ConsoleKey.PageDown -> "PageDown"
          | ConsoleKey.End -> "End"
          | ConsoleKey.Home -> "Home"
          | ConsoleKey.LeftArrow -> "LeftArrow"
          | ConsoleKey.UpArrow -> "UpArrow"
          | ConsoleKey.RightArrow -> "RightArrow"
          | ConsoleKey.DownArrow -> "DownArrow"
          | ConsoleKey.Delete -> "Delete"
          | ConsoleKey.D0 -> "D0"
          | ConsoleKey.D1 -> "D1"
          | ConsoleKey.D2 -> "D2"
          | ConsoleKey.D3 -> "D3"
          | ConsoleKey.D4 -> "D4"
          | ConsoleKey.D5 -> "D5"
          | ConsoleKey.D6 -> "D6"
          | ConsoleKey.D7 -> "D7"
          | ConsoleKey.D8 -> "D8"
          | ConsoleKey.D9 -> "D9"
          | ConsoleKey.A -> "A"
          | ConsoleKey.B -> "B"
          | ConsoleKey.C -> "C"
          | ConsoleKey.D -> "D"
          | ConsoleKey.E -> "E"
          | ConsoleKey.F -> "F"
          | ConsoleKey.G -> "G"
          | ConsoleKey.H -> "H"
          | ConsoleKey.I -> "I"
          | ConsoleKey.J -> "J"
          | ConsoleKey.K -> "K"
          | ConsoleKey.L -> "L"
          | ConsoleKey.M -> "M"
          | ConsoleKey.N -> "N"
          | ConsoleKey.O -> "O"
          | ConsoleKey.P -> "P"
          | ConsoleKey.Q -> "Q"
          | ConsoleKey.R -> "R"
          | ConsoleKey.S -> "S"
          | ConsoleKey.T -> "T"
          | ConsoleKey.U -> "U"
          | ConsoleKey.V -> "V"
          | ConsoleKey.W -> "W"
          | ConsoleKey.X -> "X"
          | ConsoleKey.Y -> "Y"
          | ConsoleKey.Z -> "Z"
          | ConsoleKey.OemPlus -> "OemPlus"
          | ConsoleKey.OemMinus -> "OemMinus"
          | _ -> "None"

        let keyTypeName =
          FQTypeName.fqPackage PackageIDs.Type.Stdlib.Cli.Stdin.key
        let key = DEnum(keyTypeName, keyTypeName, [], keyCaseName, [])

        let keyChar =
          let ch = readKey.KeyChar
          if Char.IsControl(ch) || ch = '\u0000' then DString ""
          else ch |> string |> DString

        let keyReadTypeName =
          FQTypeName.fqPackage PackageIDs.Type.Stdlib.Cli.Stdin.keyRead
        let keyRead =
          DRecord(
            keyReadTypeName,
            keyReadTypeName,
            [],
            Map [ "key", key; "modifiers", modifiers; "keyChar", keyChar ]
          )

        // Call the handler to get the message
        let! msgResult = executeApplicable exeState handler [] [ keyRead ]
        let msg =
          match msgResult with
          | Ok m -> m
          | Error(rte, _) ->
            Exception.raiseInternal "Keyboard handler failed" [ "error", rte ]

        // Call update with model and message
        let! updateResult = executeApplicable exeState updateFn [] [ model; msg ]
        let newModelOrExit =
          match updateResult with
          | Ok r -> r
          | Error(rte, _) ->
            Exception.raiseInternal "Update function failed" [ "error", rte ]

        // Check if we should continue or exit
        match newModelOrExit with
        | DEnum(_, _, _, "Continue", [ newModel ]) -> model <- newModel
        | DEnum(_, _, _, "Exit", [ DInt64 code ]) -> exitCode <- Some code
        | other ->
          Exception.raiseInternal "Update must return UpdateResult" [ "got", other ]

      | None ->
        // No keyboard subscription - just wait a bit and continue
        // This prevents a tight loop
        System.Threading.Thread.Sleep(100)

    return DInt64(exitCode |> Option.defaultValue 0L)
  }


// Daemon process tracking
let daemonPidDir =
  Path.Combine(
    Environment.GetEnvironmentVariable("HOME") |> Option.ofObj |> Option.defaultValue "/tmp",
    ".dark",
    "daemons"
  )

let ensureDaemonDir () =
  if not (Directory.Exists(daemonPidDir)) then
    Directory.CreateDirectory(daemonPidDir) |> ignore<DirectoryInfo>


let fns : List<BuiltInFn> =
  [ { name = fn "cliRunCliApp" 0
      typeParams = []
      parameters =
        [ Param.make "app" (TVariable "a") "The CLI app to run (CliApp record)"
          Param.make "args" (TList TString) "Command line arguments" ]
      returnType = TInt64
      description = "Runs an interactive CLI app using the TEA pattern. Returns exit code."
      fn =
        (function
        | exeState, _, _, [ app; DList(_, args) ] ->
          uply {
            let stringArgs =
              args
              |> List.choose (function
                | DString s -> Some(DString s)
                | _ -> None)
            let! result = runCliApp exeState app stringArgs
            return result
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "cliRunDaemonApp" 0
      typeParams = []
      parameters =
        [ Param.make "app" (TVariable "a") "The daemon app to run (DaemonApp record)"
          Param.make "args" (TList TString) "Command line arguments" ]
      returnType = TInt64
      description = "Runs a daemon app in the foreground. Returns exit code when daemon stops."
      fn =
        (function
        | exeState, _, _, [ app; DList(_, args) ] ->
          uply {
            // Extract fields from the DaemonApp record
            let fields =
              match app with
              | DRecord(_, _, _, fields) -> fields
              | _ -> Exception.raiseInternal "Expected DaemonApp record" [ "got", app ]

            let getField name =
              Map.find name fields
              |> Option.defaultWith (fun () ->
                Exception.raiseInternal $"Missing field {name}" [])

            let initFn = getField "init"
            let onEventFn = getField "onEvent"
            let timerIntervalMs =
              match getField "timerIntervalMs" with
              | DInt64 ms -> ms
              | _ -> 1000L

            // Convert args
            let argsList =
              args
              |> List.choose (function
                | DString s -> Some(DString s)
                | _ -> None)
              |> fun l -> DList(VT.string, l)

            // Call init
            let! initResult = executeApplicable exeState initFn [] [ argsList ]
            let mutable state =
              match initResult with
              | Ok s -> s
              | Error(rte, _) ->
                Exception.raiseInternal "Daemon init failed" [ "error", rte ]

            // Send Started event
            let startedEvent = DEnum(daemonEventTypeName, daemonEventTypeName, [], "Started", [])
            let! startResult = executeApplicable exeState onEventFn [] [ state; startedEvent ]
            let mutable running =
              match startResult with
              | Ok(DEnum(_, _, _, "Some", [ newState ])) ->
                state <- newState
                true
              | Ok(DEnum(_, _, _, "None", [])) -> false
              | Ok other ->
                Exception.raiseInternal "onEvent must return Option" [ "got", other ]
              | Error(rte, _) ->
                Exception.raiseInternal "onEvent failed" [ "error", rte ]

            // Timer loop
            while running do
              System.Threading.Thread.Sleep(int timerIntervalMs)

              let timerEvent =
                DEnum(daemonEventTypeName, daemonEventTypeName, [], "Timer", [ DString "main" ])
              let! eventResult = executeApplicable exeState onEventFn [] [ state; timerEvent ]
              match eventResult with
              | Ok(DEnum(_, _, _, "Some", [ newState ])) -> state <- newState
              | Ok(DEnum(_, _, _, "None", [])) -> running <- false
              | Ok other ->
                Exception.raiseInternal "onEvent must return Option" [ "got", other ]
              | Error(rte, _) ->
                Exception.raiseInternal "onEvent failed" [ "error", rte ]

            return DInt64 0L
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "cliSpawnDaemon" 0
      typeParams = []
      parameters =
        [ Param.make "daemonPath" TString "The fully qualified path to the daemon (e.g., Darklang.Apps.Examples.fileWatcherDaemon)"
          Param.make "args" (TList TString) "Command line arguments" ]
      returnType = TypeReference.result TInt64 TString
      description = "Spawns a daemon as a background process. Returns the PID on success."
      fn =
        (function
        | _, _, _, [ DString daemonPath; DList(_, args) ] ->
          uply {
            ensureDaemonDir ()

            // Build command to run the daemon
            let argsStr =
              args
              |> List.choose (function DString s -> Some s | _ -> None)
              |> String.concat " "

            // Use the current CLI executable to run the daemon
            // Get the path to the current running CLI
            let currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName
            let psi =
              System.Diagnostics.ProcessStartInfo(
                FileName = currentExe,
                Arguments = $"run @{daemonPath} {argsStr}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
              )

            try
              let proc = System.Diagnostics.Process.Start(psi)
              let pid = int64 proc.Id

              // Write PID file
              let daemonName = daemonPath.Split('.') |> Array.last
              let pidFile = Path.Combine(daemonPidDir, $"{daemonName}.pid")
              File.WriteAllText(pidFile, string pid)

              return Dval.resultOk KTInt64 KTString (DInt64 pid)
            with ex ->
              return Dval.resultError KTInt64 KTString (DString ex.Message)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "cliStopDaemon" 0
      typeParams = []
      parameters = [ Param.make "name" TString "The daemon name to stop" ]
      returnType = TypeReference.result TUnit TString
      description = "Stops a running daemon by name."
      fn =
        (function
        | _, _, _, [ DString name ] ->
          uply {
            ensureDaemonDir ()
            let pidFile = Path.Combine(daemonPidDir, $"{name}.pid")

            if not (File.Exists(pidFile)) then
              return Dval.resultError KTUnit KTString (DString $"Daemon '{name}' not found")
            else
              try
                let pid = File.ReadAllText(pidFile) |> int
                let proc = System.Diagnostics.Process.GetProcessById(pid)
                proc.Kill()
                File.Delete(pidFile)
                return Dval.resultOk KTUnit KTString DUnit
              with
              | :? ArgumentException ->
                // Process not running, clean up PID file
                File.Delete(pidFile)
                return Dval.resultOk KTUnit KTString DUnit
              | ex ->
                return Dval.resultError KTUnit KTString (DString ex.Message)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "cliListRunningDaemons" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TList(TCustomType(Ok daemonStatusTypeName, []))
      description = "Lists all running daemons with their status."
      fn =
        (function
        | _, _, _, [ DUnit ] ->
          uply {
            ensureDaemonDir ()

            let statuses =
              if Directory.Exists(daemonPidDir) then
                Directory.GetFiles(daemonPidDir, "*.pid")
                |> Array.toList
                |> List.map (fun pidFile ->
                  let name =
                    Path.GetFileNameWithoutExtension(pidFile)
                  let pid = File.ReadAllText(pidFile) |> int64

                  let isRunning =
                    try
                      let proc = System.Diagnostics.Process.GetProcessById(int pid)
                      not proc.HasExited
                    with _ ->
                      false

                  DRecord(
                    daemonStatusTypeName,
                    daemonStatusTypeName,
                    [],
                    Map
                      [ "name", DString name
                        "pid", DInt64 pid
                        "isRunning", DBool isRunning ]
                  ))
              else
                []

            return DList(VT.customType daemonStatusTypeName [], statuses)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]


let builtins : Builtins = Builtin.make [] fns
