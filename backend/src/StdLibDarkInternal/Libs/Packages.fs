/// StdLib functions for building Dark functionality via Dark packages
module StdLibDarkInternal.Libs.Packages

open Prelude
open Tablecloth

open LibExecution.RuntimeTypes
open LibExecution.StdLib.Shortcuts

module PT = LibExecution.ProgramTypes
module Canvas = LibBackend.Canvas
module Serialize = LibBackend.Serialize
module PT2DT = ProgramTypes2DarkTypes

let modul = [ "DarkInternal"; "Packages" ]

let stdlibPackageTyp
  (submodules : List<string>)
  (name : string)
  (version : int)
  : FQTypeName.T =
  pkgTyp "Darklang" (NonEmptyList.ofList ([ "Stdlib" ] @ submodules)) name version

let fn (name : string) (version : int) : FQFnName.StdlibFnName =
  FQFnName.stdlibFnName' modul name version


let types : List<BuiltInType> = []

let fns : List<BuiltInFn> =
  [ { name = fn "all" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TCustomType(stdlibPackageTyp [ "Packages" ] "Type" 0, [])
      description = "List all package types and functions"
      fn =
        function
        | _, _, [ DUnit ] ->
          uply {
            let! packages = LibBackend.PackageManager.allTypes ()
            let packages: List<Dval> = List.map PT2DT.PackageType.toDT packages

            let! fns = LibBackend.PackageManager.allFunctions ()
            let fns: List<Dval> = List.map PT2DT.PackageFn.toDT fns

            return
              DRecord(
                stdlibPackageTyp [] "Packages" 0,
                Map([ "types", DList packages; "fns", DList fns ])
              )
          }
        | _ -> incorrectArgs ()
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]

let contents = (fns, types)
