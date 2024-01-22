/// Package functions that require access to other types/fns in this module
module LocalExec.Libs.Packages2

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module VT = ValueType
module Dval = LibExecution.Dval
module PT2DT = LibExecution.ProgramTypesToDarkTypes

let packageManager = LibCloud.PackageManager.packageManager

let resolver : LibParser.NameResolver.NameResolver =
  let builtinResolver =
    // CLEANUP we need a better way to determine what builtins should be
    // available to the name resolver, as this currently assumes builtins
    // from _all_ environments are available
    LibExecution.Builtin.combine
      // We are missing the builtins that contain this function (and all associated ones)
      [ BuiltinExecution.Builtin.contents
          BuiltinExecution.Libs.HttpClient.defaultConfig
        BuiltinCli.Builtin.contents
        Packages.contents
        Cli.contents
        TestUtils.LibTest.contents
        BuiltinCloudExecution.Builtin.contents
        BuiltinCliHost.Builtin.contents ]
      []
    |> LibParser.NameResolver.fromBuiltins

  let thisResolver =
    { LibParser.NameResolver.empty with
        allowError = false
        builtinFns =
          Set
            [ LibExecution.ProgramTypes.FQFnName.builtIn
                "localExecPackagesParseAndSave"
                0
              LibExecution.ProgramTypes.FQFnName.builtIn "localExecPackagesParse" 0 ] }

  LibParser.NameResolver.merge builtinResolver thisResolver (Some packageManager)

let typ =
  FQTypeName.Package
    { owner = "Darklang"; modules = [ "Stdlib" ]; name = "Packages"; version = 0 }

let fns : List<BuiltInFn> =
  [ { name = fn "parseLocalPackageSourceFile" 0
      typeParams = []
      parameters =
        [ Param.make "package source" TString "The source code of the package"
          Param.make
            "filename"
            TString
            "Name of the local file we're parsing (used for error reporting)" ]
      returnType = TypeReference.result (TCustomType(Ok typ, [])) TString
      description = "Parse a package source file"
      fn =
        function
        | _, _, [ DString contents; DString path ] ->
          uply {
            let! (fns, types, constants) =
              LibParser.Parser.parsePackageFile resolver path contents

            let packagesFns =
              fns
              |> List.map PT2DT.PackageFn.toDT
              |> Dval.list (KTCustomType(PT2DT.PackageFn.typeName, []))
            let packagesTypes =
              types
              |> List.map PT2DT.PackageType.toDT
              |> Dval.list (KTCustomType(PT2DT.PackageType.typeName, []))
            let packagesConstants =
              constants
              |> List.map PT2DT.PackageConstant.toDT
              |> Dval.list (KTCustomType(PT2DT.PackageConstant.typeName, []))

            let fields =
              [ "fns", packagesFns
                "types", packagesTypes
                "constants", packagesConstants ]

            return
              DRecord(typ, typ, [], Map fields)
              |> Dval.resultOk (KTCustomType(typ, [])) KTString
          }
        | _ -> incorrectArgs ()
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]


let contents : LibExecution.Builtin.Contents = (fns, [])
