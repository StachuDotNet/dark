module TestUtils.LibTest

// Functions which are not part of the Dark standard library, but which are
// useful for testing

open System.Threading.Tasks

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module VT = LibExecution.ValueType
module PT = LibExecution.ProgramTypes
module Dval = LibExecution.Dval
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module PackageIDs = LibExecution.PackageIDs

open Fumble
open LibDB.Db


let varA = TVariable "a"
let varB = TVariable "b"


let values : List<BuiltInValue> =
  [ { name = value "testNan" 0
      typ = TFloat
      description = "Return a NaN"
      body = DFloat(System.Double.NaN)
      deprecated = NotDeprecated }

    { name = value "testInfinity" 0
      typ = TFloat
      description = "Returns positive infitity"
      body = DFloat(System.Double.PositiveInfinity)
      deprecated = NotDeprecated }

    { name = value "testNegativeInfinity" 0
      typ = TFloat
      description = "Returns negative infinity"
      body = DFloat(System.Double.NegativeInfinity)
      deprecated = NotDeprecated } ]

let fns : List<BuiltInFn> =
  [ { name = fn "testDerrorMessage" 0
      typeParams = []
      parameters = [ Param.make "errorMessage" TString "" ]
      returnType =
        TCustomType(
          Ok(
            FQTypeName.Package
              PackageIDs.Type.PrettyPrinter.RuntimeTypes.RuntimeError.errorMessage
          ),
          []
        )
      description = "Return a value representing a runtime type error"
      fn =
        (function
        | _, _, _, [ DString error ] ->
          let typeName =
            FQTypeName.Package
              PackageIDs.Type.PrettyPrinter.RuntimeTypes.RuntimeError.errorMessage
          DEnum(typeName, typeName, [], "ErrorString", [ DString error ]) |> Task.FromResult
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }

    { name = fn "testRuntimeError" 0
      typeParams = []
      parameters = [ Param.make "errorString" TString "" ]
      returnType = TInt64
      description = "Return a value representing a type error"
      fn =
        (function
        | _, _, _, [ DString errorString ] ->
          raiseUntargetedRTE (RuntimeError.UncaughtException(errorString, []))
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }


    { name = fn "testDerrorSqlMessage" 0
      typeParams = []
      parameters = [ Param.make "errorString" TString "" ]
      returnType =
        TCustomType(
          Ok(
            FQTypeName.Package
              PackageIDs.Type.PrettyPrinter.RuntimeTypes.RuntimeError.errorMessage
          ),
          []
        )
      description = "Return a value that matches errors thrown by the SqlCompiler"
      fn =
        (function
        | _, _, _, [ DString errorString ] ->
          let msg = LibExecution.RTQueryCompiler.errorTemplate + errorString
          let typeName =
            FQTypeName.Package
              PackageIDs.Type.PrettyPrinter.RuntimeTypes.RuntimeError.errorMessage
          DEnum(typeName, typeName, [], "ErrorString", [ DString msg ]) |> Task.FromResult
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }

    { name = fn "testToChar" 0
      typeParams = []
      parameters = [ Param.make "c" TString "" ]
      returnType = TypeReference.option TChar
      description = "Turns a string of length 1 into a character"
      fn =
        (function
        | _, _, _, [ DString s ] ->
          let chars = String.toEgcSeq s

          if Seq.length chars = 1 then
            chars
            |> Seq.toList
            |> (fun l -> l[0])
            |> DChar
            |> Dval.optionSome KTChar
            |> Task.FromResult
          else
            Dval.optionNone KTChar |> Task.FromResult
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }


    { name = fn "testIncrementSideEffectCounter" 0
      typeParams = []
      parameters =
        [ Param.make "passThru" (TVariable "a") "Task.FromResult which will be returned" ]
      returnType = TVariable "a"
      description =
        "Increases the side effect counter by one, to test real-world side-effects. Returns its argument."
      fn =
        (function
        | state, _, _, [ arg ] ->
          state.test.sideEffectCount <- state.test.sideEffectCount + 1
          Task.FromResult(arg)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }


    { name = fn "testSideEffectCount" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TInt64
      description = "Return the value of the side-effect counter"
      fn =
        (function
        | state, _, _, [ DUnit ] -> Task.FromResult(Dval.int64 state.test.sideEffectCount)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }


    { name = fn "testInspect" 0
      typeParams = []
      parameters = [ Param.make "var" varA ""; Param.make "msg" TString "" ]
      returnType = varA
      description = "Prints the value into stdout"
      fn =
        (function
        | _, _, _, [ v; DString msg ] ->
          print $"{msg}: {v}"
          Task.FromResult v
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }


    { name = fn "testDeleteUser" 0
      typeParams = []
      parameters = [ Param.make "username" TString "" ]
      returnType = TypeReference.result TUnit varB
      description = "Delete a user (test only)"
      fn =
        (function
        | _, _, _, [ DString username ] ->
          task {
            do!
              // This is unsafe. A user has canvases, and canvases have traces. It
              // will either break or cascade (haven't checked)
              Sql.query "DELETE FROM accounts_v0 WHERE username = @username"
              |> Sql.parameters [ "username", Sql.string (string username) ]
              |> Sql.executeStatementAsync
            return DUnit
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }


    { name = fn "testRaiseException" 0
      typeParams = []
      parameters = [ Param.make "message" TString "" ]
      returnType = TVariable "a"
      description = "A function that raises an F# exception"
      fn =
        (function
        | _, _, _, [ DString message ] -> raise (System.Exception message)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }


    { name = fn "testGetCanvasID" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TUuid
      description = "Get the name of the canvas that's running"
      fn =
        (function
        | state, _, _, [ DUnit ] -> state.program.canvasID |> DUuid |> Task.FromResult
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated }


    { name = fn "testSetExpectedExceptionCount" 0
      typeParams = []
      parameters = [ Param.make "count" TInt64 "" ]
      returnType = TUnit
      description = "Set the expected exception count for the current test"
      fn =
        (function
        | state, _, _, [ DInt64 count ] ->
          task {
            state.test.expectedExceptionCount <- int count
            return DUnit
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      deprecated = NotDeprecated } ]

let builtins = LibExecution.Builtin.make values fns
