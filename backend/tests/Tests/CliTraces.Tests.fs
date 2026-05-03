/// Wires the Dark-side CLI trace test runner
/// (`Darklang.Cli.Tests.runAllTests` in `packages/darklang/cli/tests/tests.dark`)
/// into the F# Expecto suite, so `./scripts/run-backend-tests` triggers it.
///
/// The runner returns the number of failed tests as Int64; we assert that's 0.
/// The 38+ individual trace tests stay as Dark code where they're easier to
/// author; the only shim here is a single F# Expecto wrapper.
module Tests.CliTraces

open Expecto
open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

module RT = LibExecution.RuntimeTypes
module PT = LibExecution.ProgramTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module Exe = LibExecution.Execution
module PackageRefs = LibExecution.PackageRefs

open TestUtils.TestUtils


/// Build an ExecutionState whose builtin set includes BuiltinCliHost
/// (which TestUtils' shared `executionStateFor` doesn't pull in by
/// default — `executionStateFor` is the generic test state for the
/// rest of the suite, not CLI-host-specific).
let cliHostExecutionState () : Task<RT.ExecutionState> =
  task {
    let pmPTValue = pmPT
    let builtins = BuiltinCliHost.Libs.Cli.builtinsToUse ()
    let pmRT = PT2RT.PackageManager.toRT builtins.values pmPTValue
    let canvasID = System.Guid.NewGuid()
    let program : RT.Program =
      { canvasID = canvasID; dbs = Map.empty; secrets = [] }

    let notify (_state : RT.ExecutionState) (_vm : RT.VMState) (_msg : string) (_metadata : Metadata) =
      uply { return () }

    let sendException (_ : RT.ExecutionState) (_ : RT.VMState) (_metadata : Metadata) (_exn : exn) =
      uply { return () }

    return
      Exe.createState
        builtins
        pmRT
        Exe.noTracing
        sendException
        notify
        PT.mainBranchId
        program
  }


let runAllCliTests =
  testTask "all Dark-side CLI tests pass" {
    let! state = cliHostExecutionState ()

    let runAllTestsFn =
      RT.FQFnName.fqPackage (PackageRefs.Fn.Cli.Tests.runAllTests ())

    // runAllTests : Unit -> Int64
    let! execResult =
      Exe.executeFunction state runAllTestsFn [] (NEList.singleton RT.DUnit)

    match execResult with
    | Ok(RT.DInt64 0L) -> ()
    | Ok(RT.DInt64 n) ->
      Tests.failtestf "%d Dark-side CLI test(s) failed" n
    | Ok dval -> Tests.failtestf "runAllTests returned unexpected dval: %A" dval
    | Error(rte, _) -> Tests.failtestf "runAllTests errored: %A" rte
  }


let tests = testList "CliTraces" [ runAllCliTests ]
