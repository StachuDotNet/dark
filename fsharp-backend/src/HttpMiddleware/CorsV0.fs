/// Middleware V0 CORS
module HttpMiddleware.Cors

open FSharp.Control.Tasks
open System.Threading.Tasks

open Prelude
open Tablecloth

module Interpreter = LibExecution.Interpreter
module Req = RequestV0
module Resp = ResponseV0

// TODO: Remove access to LibBackend from here
module Canvas = LibBackend.Canvas


type CorsSetting =
  | AllOrigins
  | Origins of List<string>

/// Many tests have the cors feature enabled. This module exists so that test
/// code may add their canvases to the list of ones with the corsSetting set.
module Test =
  type ConcurrentDictionary<'k, 'v> =
    System.Collections.Concurrent.ConcurrentDictionary<'k, 'v>

  let mutable corsSettings : ConcurrentDictionary<string, CorsSetting> = null

  let initialize () : unit = corsSettings <- ConcurrentDictionary()

  let addAllOrigins (canvasName : CanvasName.T) : unit =
    corsSettings[string canvasName] <- AllOrigins

  let addOrigins (canvasName : CanvasName.T) (origins : List<string>) : unit =
    corsSettings[string canvasName] <- Origins origins

/// We used to have a feature where we'd set the cors setting on the canvas. It's
/// much better to do this in middleware. These canvases are the remaining user
/// canvases while we had a setting. We can remove all this once this is gone.
let private corsSettingForCanvas (canvasName : CanvasName.T) : Option<CorsSetting> =
  match string canvasName with
  | "ops-presence" ->
    Some(Origins [ "localhost"; "darklang.localhost"; "https://darklang.com" ])
  | "ops-adduser" -> Some(Origins [ "https://darklang.com" ])
  | "listo" ->
    Some(
      Origins(
        [ "http://localhost:8000"
          "https://usealtitude.com"
          "https://app.usealtitude.com"
          "http://localhost:3000"
          "https://elegant-galileo.netlify.com" ]
      )
    )
  | canvasName ->
    // There's actually a lot of tests for this. I don't want to slow things down by
    // adding every test canvas here, so instead there's a nullable dictionary.
    if isNull Test.corsSettings then
      None
    else
      let mutable result : CorsSetting = AllOrigins
      let success = Test.corsSettings.TryGetValue(string canvasName, &result)
      if success then Some result else None

// ---------------
// CORS
// ---------------
let private inferCorsOriginHeader
  (canvasName : CanvasName.T)
  (headers : HttpHeaders.T)
  : string option =
  let corsSetting = corsSettingForCanvas canvasName
  let originHeader = HttpHeaders.get "Origin" headers

  let defaultOrigins =
    [ "http://localhost:3000"; "http://localhost:5000"; "http://localhost:8000" ]

  let header =
    match originHeader, corsSetting with

    // if there's no explicit canvas setting, allow common localhosts
    | Some origin, None when List.contains origin defaultOrigins -> Some origin

    // if there's no explicit canvas setting and no default match, fall back to "*"
    | _, None -> Some "*"

    // If there's a "*" in the setting, always use it.
    // This is helpful as a debugging aid since users will always see
    // Access-Control-Allow-Origin: * in their browsers, even if the
    // request has no Origin.
    | _, Some AllOrigins -> Some "*"

    // if there's no supplied origin, don't set the header at all.
    | None, _ -> None

    // Return the origin if and only if it's in the setting
    | Some origin, Some (Origins origins) when List.contains origin origins ->
      Some origin

    // Otherwise: there was a supplied origin and it's not in the setting.
    // return "null" explicitly
    | Some _, Some _ -> Some "null"

  header

let addCorsHeaders
  (reqHeaders : HttpHeaders.T)
  (canvasName : CanvasName.T)
  (response : Resp.HttpResponse)
  : Resp.HttpResponse =
  inferCorsOriginHeader canvasName reqHeaders
  |> Option.map (fun origin ->
    { response with
        // these are added in order, so make sure the user's setting wins
        headers = [ "Access-Control-Allow-Origin", origin ] @ response.headers })
  |> Option.defaultValue response


let optionsResponse
  (reqHeaders : HttpHeaders.T)
  (canvasName : CanvasName.T)
  : Option<Resp.HttpResponse> =
  // When javascript in a browser tries to make an unusual cross-origin
  // request (for example, a POST with a weird content-type or something with
  // weird headers), the browser first makes an OPTIONS request to the
  // server in order to get its permission to make that request. It includes
  // "origin", the originating origin, and "access-control-request-headers",
  // which is the list of headers the javascript would like to use.

  // (Ordinary GETs and some POSTs get handled in addCorsHeaders, above,
  // without an OPTIONS).

  // Our strategy here is: if it's from an allowed origin (i.e., in the canvas
  // cors_setting) to:
  // - return an Access-Control-Allow-Origin header for that origin
  // - return Access-Control-Allow-Headers with the requested headers
  // - return Access-Control-Allow-Methods for all of the methods we think
  //   might be useful.

  let acReqHeaders = HttpHeaders.get "access-control-request-headers" reqHeaders
  let allowHeaders = Option.defaultValue "*" acReqHeaders

  (inferCorsOriginHeader canvasName reqHeaders)
  |> Option.map (fun origin ->
    { statusCode = 200
      body = [||]
      headers =
        [ "Access-Control-Allow-Headers", allowHeaders
          // CLEANUP: if the origin is null here, we probably shouldn't add the other headers
          "Access-Control-Allow-Origin", origin
          "Access-Control-Allow-Methods", "GET,PUT,POST,DELETE,PATCH,HEAD,OPTIONS" ] })
