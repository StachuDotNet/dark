/// Standard libraries for running processes
module BuiltinCli.Libs.Process

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.RuntimeTypes

module Dval = LibExecution.Dval
module Builtin = LibExecution.Builtin
open Builtin.Shortcuts
open System.Runtime.InteropServices

let types : List<BuiltInType> =
  [ { name = typ [ "Process" ] "Result" 0
      description = "An error that occurred while running a process."
      declaration =
        { typeParams = []
          definition =
            TypeDeclaration.Record(
              NEList.ofList
                { name = "exitCode"; typ = TInt }
                [ { name = "stdout"; typ = TBytes }
                  { name = "stderr"; typ = TBytes } ]
            ) }
      deprecated = NotDeprecated } ]

let fns : List<BuiltInFn> =
  [ { name = fn [ "Process" ] "run" 0
      description = "Runs a process, return exitCode, stdout and stderr"
      typeParams = []
      parameters = [ Param.make "command" TString "The command to run" ]
      returnType = stdlibTypeRef [ "Process" ] "Result" 0
      fn =
        (function
        | state, _, [ DString command ] ->
          let types = ExecutionState.availableTypes state

          let (commandName, args) =
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
              "cmd.exe", $"/c {command}"
            elif
              RuntimeInformation.IsOSPlatform OSPlatform.Linux
              || RuntimeInformation.IsOSPlatform OSPlatform.OSX
            then
              "/bin/bash", $"-c \"{command}\""
            else
              raiseUntargetedRTE (RuntimeError.oldError "Unsupported OS")

          let psi =
            System.Diagnostics.ProcessStartInfo()
            |> fun psi ->
              psi.FileName <- commandName
              psi.UseShellExecute <- false
              psi.RedirectStandardOutput <- true
              psi.RedirectStandardError <- true
              psi.CreateNoWindow <- true
              psi.Arguments <- args
              psi

          let p = System.Diagnostics.Process.Start psi

          let stdout = p.StandardOutput.ReadToEnd()
          let stderr = p.StandardError.ReadToEnd()

          p.WaitForExit()

          Dval.record
            types
            (TypeName.fqBuiltIn [ "Process" ] "Result" 0)
            (Some [])
            [ ("exitCode", DInt p.ExitCode)
              ("stdout", DString stdout)
              ("stderr", DString stderr) ]
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]

let constants : List<BuiltInConstant> = []
let contents : Builtin.Contents = (fns, types, constants)
