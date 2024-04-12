/// Builtin functions for building Dark functionality via Dark canvases
module BuiltinDarkInternal.Libs.Canvases

open System.Threading.Tasks

open Prelude

open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module VT = ValueType
module Dval = LibExecution.Dval
module PT = LibExecution.ProgramTypes
module Canvas = LibCloud.Canvas
module Serialize = LibCloud.Serialize
module PT2DT = LibExecution.ProgramTypesToDarkTypes



let ptTyp
  (submodules : List<string>)
  (name : string)
  (version : int)
  : FQTypeName.FQTypeName =
  FQTypeName.fqPackage
    "Darklang"
    ("Stdlib" :: "ProgramTypes" :: submodules)
    name
    version

let packageCanvasType (addlModules : List<string>) (name : string) (version : int) =
  FQTypeName.fqPackage
    "Darklang"
    ("Internal" :: "Canvas" :: addlModules)
    name
    version


let constants : List<BuiltInConstant> = []

let fns : List<BuiltInFn> =
  [ { name = fn "darkInternalCanvasList" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TList TUuid
      description = "Get a list of all canvas IDs"
      fn =
        (function
        | _, _, [ DUnit ] ->
          uply {
            let! hosts = Canvas.allCanvasIDs ()
            return DList(VT.uuid, List.map DUuid hosts)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "darkInternalCanvasCreate" 0
      typeParams = []
      parameters = [ Param.make "owner" TUuid ""; Param.make "name" TString "" ]
      returnType = TUuid
      description = "Creates a new canvas"
      fn =
        (function
        | _, _, [ DUuid owner; DString name ] ->
          uply {
            let! canvasID = Canvas.create owner name
            return DUuid canvasID
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "darkInternalCanvasOwner" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid "" ]
      returnType = TUuid
      description = "Get the owner of a canvas"
      fn =
        (function
        | _, _, [ DUuid canvasID ] ->
          uply {
            let! owner = Canvas.getOwner canvasID
            return
              owner
              |> Exception.unwrapOptionInternal
                "Canvas owner not found"
                [ "canvasID", canvasID ]
              |> DUuid
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // ---------------------
    // Toplevels
    // ---------------------
    { name = fn "darkInternalCanvasDeleteToplevelForever" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid ""; Param.make "tlid" TUInt64 "" ]
      returnType = TBool
      description =
        "Delete a toplevel forever. Requires that the toplevel already by deleted. If so, deletes and returns true. Otherwise returns false"
      fn =
        (function
        | _, _, [ DUuid canvasID; DUInt64 tlid ] ->
          uply {
            let tlid = uint64 tlid
            let! c = Canvas.loadFrom canvasID [ tlid ]
            if
              Map.containsKey tlid c.deletedHandlers
              || Map.containsKey tlid c.deletedDBs
              || Map.containsKey tlid c.deletedUserTypes
              || Map.containsKey tlid c.deletedUserFunctions
            then
              do! Canvas.deleteToplevelForever canvasID tlid
              return DBool true
            else
              return DBool false
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }

    // ---------------------
    // Programs
    // ---------------------
    { name = fn "darkInternalCanvasDarkEditorCanvasID" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TUuid
      description = "Returns the ID of the special dark-editor canvas"
      fn =
        (function
        | state, _, [ DUnit ] -> state.program.canvasID |> DUuid |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "darkInternalCanvasFullProgram" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid "" ]
      returnType =
        TypeReference.result
          (TCustomType(Ok(packageCanvasType [] "Program" 0), []))
          TString
      description =
        "Returns a list of toplevel ids of http handlers in canvas <param canvasId>"
      fn =
        (function
        | _, _, [ DUuid canvasID ] ->
          uply {
            let! canvas = Canvas.loadAll canvasID

            let types =
              canvas.userTypes
              |> Map.values
              |> Seq.toList
              |> List.map PT2DT.UserType.toDT
            let types = DList(VT.customType PT2DT.UserType.typeName [], types)

            let fns =
              canvas.userFunctions
              |> Map.values
              |> Seq.toList
              |> List.map PT2DT.UserFunction.toDT
            let fns = DList(VT.customType PT2DT.UserFunction.typeName [], fns)

            // let dbs =
            //   Map.values canvas.dbs
            //   |> Seq.toList
            //   |> List.map (fun db ->
            //     [ "tlid", DString(db.tlid.ToString()); "name", DString db.name ]
            //     |> Dval.dict)
            //   |> Dval.list VT.unknownTODO

            // let httpHandlers =
            //   Map.values canvas.handlers
            //   |> Seq.toList
            //   |> List.choose (fun handler ->
            //     match handler.spec with
            //     | PT.Handler.Worker _
            //     | PT.Handler.Cron _
            //     | PT.Handler.REPL _ -> None
            //     | PT.Handler.HTTP (route, method) ->
            //       [ "tlid", DString(handler.tlid.ToString())
            //         "method", DString method
            //         "route", DString route ]
            //       |> Dval.dict
            //       |> Some)
            //   |> Dval.list VT.unknownTODO


            let typeName = packageCanvasType [] "Program" 0
            return
              DRecord(typeName, typeName, [], Map [ "types", types; "fns", fns ])
              |> Dval.resultOk (KTCustomType(typeName, [])) KTString
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]

let contents : Builtins = LibExecution.Builtin.fromContents constants fns
