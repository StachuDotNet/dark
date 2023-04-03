module CanvasHack.ParseCanvas

open System
open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Tablecloth


let baseDir = "canvases"

// multiple files, etc - only really supports http handlers
module V0 =
  module Yml =
    type HttpHandler =
      { [<Legivel.Attributes.YamlField("method")>]
        Method : string

        [<Legivel.Attributes.YamlField("path")>]
        Path : string }

    type Config =
      { [<Legivel.Attributes.YamlField("http-handlers")>]
        HttpHandlers : Map<string, HttpHandler> }

  module Response =
    type HttpHandler =
      { Method : string
        Path : string
        FileName : string
        Code : string }

    type T = { CanvasName : string; HttpHandlers : List<HttpHandler> }

  let parse canvasName : Response.T =
    let dir = $"${baseDir}/${canvasName}"
    let config =
      $"{dir}/config.yml"
      |> System.IO.File.ReadAllText
      |> Legivel.Serialization.Deserialize<Yml.Config>

    let config : Yml.Config =
      match List.head config with
      | Some (Legivel.Serialization.Success s) -> s.Data
      | _ -> Exception.raiseCode "couldn't parse config file for canvas"

    let httpHandlers : List<Response.HttpHandler> =
      config.HttpHandlers
      |> Map.toList
      |> List.map (fun (name, details) ->
        let sourceFile = $"{dir}/{name}"
        let sourceCode = System.IO.File.ReadAllText sourceFile
        { Method = details.Method
          Path = details.Path
          FileName = sourceFile
          Code = sourceCode })

    { CanvasName = canvasName; HttpHandlers = httpHandlers }

// multiple files, etc - only really supports http handlers
module V1 =
  module Yml =
    type Config =
      { [<Legivel.Attributes.YamlField("main")>]
        Main : string }

  module Response =
    type T = { CanvasName : string; MainFileName : string; MainFileCode : string }

  let parse canvasName : Response.T =
    let dir = $"${baseDir}/${canvasName}"
    let config =
      $"{dir}/config.yml"
      |> System.IO.File.ReadAllText
      |> Legivel.Serialization.Deserialize<Yml.Config>

    let config : Yml.Config =
      match List.head config with
      | Some (Legivel.Serialization.Success s) -> s.Data
      | _ -> Exception.raiseCode "couldn't parse config file for canvas"

    let sourceFile = $"{dir}/{config.Main}"
    let mainFileCode = System.IO.File.ReadAllText sourceFile

    { CanvasName = canvasName
      MainFileName = config.Main
      MainFileCode = mainFileCode }


type CanvasConfig =
  | V0 of V0.Response.T
  | V1 of V1.Response.T

type ConfigFileVersionYml =
  { [<Legivel.Attributes.YamlField("version")>]
    Version : string }

let parse canvasName : CanvasConfig =
  let configFileContents : string =
    $"${baseDir}/${canvasName}/config.yml" |> System.IO.File.ReadAllText

  let configFileVersion =
    Legivel.Serialization.Deserialize<ConfigFileVersionYml> configFileContents

  let configFileVersion : ConfigFileVersionYml =
    match List.head configFileVersion with
    | Some (Legivel.Serialization.Success s) -> s.Data
    | _ -> Exception.raiseCode "couldn't parse config file for canvas"

  match configFileVersion.Version with
  | "0" -> CanvasConfig.V0(V0.parse canvasName)
  | "1" -> CanvasConfig.V1(V1.parse canvasName)
  | _ -> Exception.raiseCode "TODO"
