/// Standard libraries for running processes
module StdLibCli.Libs.Process

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.RuntimeTypes

module StdLib = LibExecution.StdLib
open StdLib.Shortcuts

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
      parameters =
        [ Param.make "command" TString "The command to run"
          Param.make "input" TString "The input to the command" ]
      returnType = stdlibTypeRef [ "Process" ] "Result" 0
      fn =
        (function
        | _, _, [ DString command; DString input ] ->
          let psi =
            System.Diagnostics.ProcessStartInfo()
            |> fun psi ->
              psi.FileName <- command
              psi.UseShellExecute <- true
              psi.RedirectStandardOutput <- true
              psi.RedirectStandardError <- true
              psi.CreateNoWindow <- true
              //psi.Arguments <- input

              psi.Arguments <- "-c \" " + input + " \""
              psi

          let p = System.Diagnostics.Process.Start(psi)

          let stdout = p.StandardOutput.ReadToEnd()
          let stderr = p.StandardError.ReadToEnd()

          p.WaitForExit()

          Dval.record
            (TypeName.fqBuiltIn [ "Process" ] "Result" 0)
            [ ("exitCode", DInt(p.ExitCode))
              ("stdout", DString(stdout))
              ("stderr", DString(stderr)) ]
          |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn [ "Process"; "Linux" ] "run" 0
      description = "Runs a process in Linux - return exitCode, stdout, and stderr"
      typeParams = []
      parameters = [ Param.make "command" TString "The command to run" ]
      returnType = stdlibTypeRef [ "Process" ] "Result" 0
      fn =
        (function
        | _, _, [ DString input ] ->
          let psi =
            System.Diagnostics.ProcessStartInfo()
            |> fun psi ->
              psi.FileName <- "/bin/bash"
              psi.UseShellExecute <- false
              psi.RedirectStandardOutput <- true
              psi.RedirectStandardError <- true
              psi.CreateNoWindow <- true
              psi.Arguments <- "-c \" " + input + " \""
              psi

          let p = System.Diagnostics.Process.Start(psi)

          let stdout = p.StandardOutput.ReadToEnd()
          let stderr = p.StandardError.ReadToEnd()

          p.WaitForExit()

          Dval.record
            (TypeName.fqBuiltIn [ "Process" ] "Result" 0)
            [ ("exitCode", DInt(p.ExitCode))
              ("stdout", DString(stdout))
              ("stderr", DString(stderr)) ]
          |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]

let constants : List<BuiltInConstant> = []
let contents : StdLib.Contents = (fns, types, constants)
