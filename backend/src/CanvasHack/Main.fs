module CanvasHack.Main

open System
open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Tablecloth

// TODO: bring in argu and make CanvasHack a more interactive experience

module PT = LibExecution.ProgramTypes
module Op = LibBackend.Op
module C = LibBackend.Canvas

let initSerializers () = ()

module CommandNames =
  [<Literal>]
  let import = "load-from-disk"

  [<Literal>]
  let export = "save-to-disk"




[<EntryPoint>]
let main (args : string []) =
  try
    initSerializers ()

    task {
      match args with
      | [||] ->
        print
          $"`canvas-hack {CommandNames.import}` to load dark-editor from disk or
            `canvas-hack {CommandNames.export}' to save dark-editor to disk"

      | [| CommandNames.import; canvasName |] ->
        let parsed = ParseCanvas.parse canvasName

        match parsed with
        | ParseCanvas.CanvasConfig.V0 config ->
          // create the canvas
          let domain = $"{canvasName}.dlio.localhost"

          let canvasID =
            if canvasName = "dark-editor" then
              LibBackend.Config.allowedDarkInternalCanvasID
            else
              System.Guid.NewGuid()

          let! ownerID = LibBackend.Account.createUser ()

          do! LibBackend.Canvas.createWithExactID canvasID ownerID domain

          // gather some Ops
          let ops =
            // config.HttpHandlers
            // |> List.map (fun handler ->
            //   let modul = Parser.parseModule [] handler.FileName handler.Code

            //   let types = modul.types |> List.map PT.Op.SetType
            //   let fns = modul.fns |> List.map PT.Op.SetFunction

            //   let handler =
            //     match modul.exprs with
            //     | [ expr ] ->
            //       PT.Op.SetHandler(
            //         { tlid = gid ()
            //           ast = expr
            //           spec =
            //             PT.Handler.Spec.HTTP(
            //               handler.Path,
            //               handler.Method,
            //               { moduleID = gid (); nameID = gid (); modifierID = gid () }
            //             ) }
            //       )
            //     | _ -> Exception.raiseCode "expected exactly one expr in file"

            //   [ PT.Op.TLSavepoint(140418122UL) ] @ types @ fns @ [ handler ])
            // |> List.flatten
            []

          let canvasWithTopLevels = C.fromOplist canvasID [] ops

          let oplists =
            ops
            |> Op.oplist2TLIDOplists
            |> List.filterMap (fun (tlid, oplists) ->
              match Map.get tlid (C.toplevels canvasWithTopLevels) with
              | Some tl -> Some(tlid, oplists, tl, C.NotDeleted)
              | None -> None)

          // persist the Ops
          do! C.saveTLIDs canvasID oplists

        | ParseCanvas.CanvasConfig.V1 config ->
          // create the canvas
          let domain = $"{canvasName}.dlio.localhost"

          let canvasID =
            if canvasName = "dark-editor" then
              LibBackend.Config.allowedDarkInternalCanvasID
            else
              System.Guid.NewGuid()

          let! ownerID = LibBackend.Account.createUser ()

          do! LibBackend.Canvas.createWithExactID canvasID ownerID domain

          //let modul = Parser.parseModule [] config.MainFileName config.MainFileCode

          // // gather some Ops
          // let ops =
          //   config.HttpHandlers
          //   |> List.map (fun handler ->
          //     let modul = Parser.parseModule [] handler.FileName handler.Code

          //     let types = modul.types |> List.map PT.Op.SetType
          //     let fns = modul.fns |> List.map PT.Op.SetFunction

          //     let handler =
          //       match modul.exprs with
          //       | [ expr ] ->
          //         PT.Op.SetHandler(
          //           { tlid = gid ()
          //             ast = expr
          //             spec =
          //               PT.Handler.Spec.HTTP(
          //                 handler.Path,
          //                 handler.Method,
          //                 { moduleID = gid (); nameID = gid (); modifierID = gid () }
          //               ) }
          //         )
          //       | _ -> Exception.raiseCode "expected exactly one expr in file"

          //     [ PT.Op.TLSavepoint(140418122UL) ] @ types @ fns @ [ handler ])
          //   |> List.flatten

          // let canvasWithTopLevels = C.fromOplist canvasID [] ops

          // let oplists =
          //   ops
          //   |> Op.oplist2TLIDOplists
          //   |> List.filterMap (fun (tlid, oplists) ->
          //     match Map.get tlid (C.toplevels canvasWithTopLevels) with
          //     | Some tl -> Some(tlid, oplists, tl, C.NotDeleted)
          //     | None -> None)

          // // persist the Ops
          // do! C.saveTLIDs canvasID oplists
          ()

      | [| CommandNames.export |] ->
        // Find the canvas
        print "TODO: get context of the canvas"

        // Get the list of HTTP Handlers configured
        print "TODO: get list of http handlers, incl. code, path, method"

        // Replace the .dark files on disk

        // For each of the current HTTP handlers
        //    - serialize it (`let a = 1 + 2`)
        //      (write serializer in dark?)
        //    - save to disk
        //      (? how do we choose the name)

        // 5. Save to .dark files

        print "TODO"

      | _ ->
        print
          $"CanvasHack isn't sure what to do with these arguments.
          Currently expecting just '{CommandNames.import}' or '{CommandNames.export}'"

      return 0
    }
    |> fun x -> x.Result
  with
  | e ->
    printException "" [] e
    1
