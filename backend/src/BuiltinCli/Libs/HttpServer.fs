/// Builtins for running HTTP servers using Kestrel
module BuiltinCli.Libs.HttpServer

open System
open System.Threading.Tasks
open FSharp.Control.Tasks

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open Prelude
open LibExecution.RuntimeTypes
module VT = LibExecution.ValueType
module Dval = LibExecution.Dval
module Builtin = LibExecution.Builtin
module Interpreter = LibExecution.Interpreter
module Execution = LibExecution.Execution
module PackageIDs = LibExecution.PackageIDs
open Builtin.Shortcuts


// Type names
let httpMethodTypeName =
  FQTypeName.fqPackage PackageIDs.Type.Stdlib.Http.httpMethod

let requestTypeName =
  FQTypeName.fqPackage PackageIDs.Type.Stdlib.Http.request

let responseTypeName =
  FQTypeName.fqPackage PackageIDs.Type.Stdlib.Http.response

let httpHandlerTypeName =
  FQTypeName.fqPackage PackageIDs.Type.Stdlib.Http.httpHandler


/// Convert HTTP method string to Darklang Method enum
let methodToDval (method : string) : Dval =
  let caseName =
    match method.ToUpper() with
    | "GET" -> "GET"
    | "POST" -> "POST"
    | "PUT" -> "PUT"
    | "DELETE" -> "DELETE"
    | "PATCH" -> "PATCH"
    | "HEAD" -> "HEAD"
    | "OPTIONS" -> "OPTIONS"
    | "TRACE" -> "TRACE"
    | "CONNECT" -> "CONNECT"
    | _ -> "GET"
  DEnum(httpMethodTypeName, httpMethodTypeName, [], caseName, [])


/// Convert Darklang Method enum to HTTP method string
let dvalToMethod (dval : Dval) : string =
  match dval with
  | DEnum(_, _, _, caseName, []) -> caseName
  | _ -> "GET"


/// Match a request path against a route pattern
/// Returns Some with extracted pathParams if matches, None otherwise
let matchRoute (pattern : string) (path : string) : Option<Map<string, string>> =
  // Handle wildcard pattern
  if pattern = "/*" then
    Some Map.empty
  else
    let patternParts = pattern.Split('/') |> Array.toList
    let pathParts = (path.Split('?')[0]).Split('/') |> Array.toList

    let rec matchParts (pats : List<string>) (paths : List<string>) (pathParams : Map<string, string>) =
      match pats, paths with
      | [], [] -> Some pathParams
      | [], _ -> None
      | _, [] -> None
      | pat :: restPat, p :: restPath ->
        if pat.StartsWith(":") then
          // Parameter capture
          let paramName = pat.Substring(1)
          matchParts restPat restPath (Map.add paramName p pathParams)
        elif pat = "*" then
          // Wildcard - match everything
          Some pathParams
        elif pat = p then
          // Exact match
          matchParts restPat restPath pathParams
        else
          None

    matchParts patternParts pathParts Map.empty


/// Helper to execute a Darklang applicable (lambda or named fn) with arguments
let executeApplicable
  (exeState : ExecutionState)
  (applicable : Dval)
  (typeArgs : List<TypeReference>)
  (args : List<Dval>)
  : Task<ExecutionResult> =
  task {
    let mutable rc = 0
    let resultReg = rc
    rc <- rc + 1

    let applicableReg = rc
    rc <- rc + 1
    let loadApplicable = LoadVal(applicableReg, applicable)

    let argInstrs, argRegs =
      args
      |> List.fold
        (fun (instrs, regs) arg ->
          let reg = rc
          rc <- rc + 1
          instrs @ [ LoadVal(reg, arg) ], regs @ [ reg ])
        ([], [])

    let applyInstr =
      match argRegs with
      | [] ->
        let unitReg = rc
        rc <- rc + 1
        let loadUnit = LoadVal(unitReg, DUnit)
        [ loadUnit; Apply(resultReg, applicableReg, typeArgs, NEList.singleton unitReg) ]
      | first :: rest ->
        [ Apply(resultReg, applicableReg, typeArgs, NEList.ofList first rest) ]

    let allInstrs = [ loadApplicable ] @ argInstrs @ applyInstr

    let instrs : Instructions =
      { registerCount = rc
        instructions = allInstrs
        resultIn = resultReg }

    return! Execution.executeExpr exeState instrs
  }


/// Handler info extracted from HttpHandler records
type HandlerInfo =
  { route : string
    method : string
    handler : Dval }


/// Extract handler info from a list of HttpHandler Dvals
let extractHandlerInfos (handlers : List<Dval>) : List<HandlerInfo> =
  handlers
  |> List.choose (fun h ->
    match h with
    | DRecord(_, _, _, fields) ->
      let route =
        Map.find "route" fields
        |> Option.bind (function DString s -> Some s | _ -> None)
        |> Option.defaultValue "/*"
      let method =
        Map.find "method" fields
        |> Option.map dvalToMethod
        |> Option.defaultValue "GET"
      let handler = Map.find "handler" fields
      handler |> Option.map (fun hf -> { route = route; method = method; handler = hf })
    | _ -> None)


/// Run HTTP server with a list of HttpHandler values using Kestrel
let runHttpServeHandlers
  (exeState : ExecutionState)
  (port : int)
  (host : string)
  (handlers : List<Dval>)
  : Task<Dval> =
  task {
    let handlerInfos = extractHandlerInfos handlers

    let builder = WebApplication.CreateBuilder()

    // Configure Kestrel
    builder.WebHost.ConfigureKestrel(fun options ->
      if host = "0.0.0.0" then
        options.ListenAnyIP(port)
      else
        options.Listen(System.Net.IPAddress.Parse(host), port)
    ) |> ignore<IWebHostBuilder>

    // Disable default logging noise
    builder.Logging.ClearProviders() |> ignore<ILoggingBuilder>

    let app = builder.Build()

    // Main request handler
    let handleRequest (ctx : HttpContext) : Task =
      task {
        try
          let requestMethod = ctx.Request.Method
          let requestPath = ctx.Request.Path.Value

          // Build Darklang Request
          let methodDval = methodToDval requestMethod
          let url = ctx.Request.Path.Value + ctx.Request.QueryString.Value

          let headers =
            ctx.Request.Headers
            |> Seq.collect (fun kvp ->
              kvp.Value |> Seq.map (fun v -> DTuple(DString kvp.Key, DString v, [])))
            |> Seq.toList

          let! bodyBytes =
            task {
              use ms = new System.IO.MemoryStream()
              do! ctx.Request.Body.CopyToAsync(ms)
              return ms.ToArray()
            }

          let body =
            bodyBytes
            |> Array.toList
            |> List.map (fun b -> DUInt8 b)

          let darkRequest =
            DRecord(
              requestTypeName,
              requestTypeName,
              [],
              Map
                [ "method", methodDval
                  "url", DString url
                  "headers", DList(VT.tuple VT.string VT.string [], headers)
                  "body", DList(VT.uint8, body) ]
            )

          // Find matching handler using specificity
          let matchedHandler =
            handlerInfos
            |> List.choose (fun info ->
              if info.method = requestMethod || info.method = "*" then
                match matchRoute info.route requestPath with
                | Some pathParams -> Some (info, pathParams)
                | None -> None
              else
                None)
            // Sort by specificity - more specific routes first
            |> List.sortByDescending (fun (info, _) ->
              let parts = info.route.Split('/') |> Array.toList
              parts
              |> List.map (fun p -> if p.StartsWith(":") then 0 else 1)
              |> List.sum)
            |> List.tryHead

          let! darkResponse =
            match matchedHandler with
            | Some(info, pathParams) ->
              task {
                let pathParamsDval =
                  pathParams
                  |> Map.toList
                  |> List.map (fun (k, v) -> k, DString v)
                  |> Map.ofList
                  |> fun m -> DDict(VT.string, m)

                let! result =
                  executeApplicable exeState info.handler [] [ darkRequest; pathParamsDval ]
                match result with
                | Ok resp -> return resp
                | Error(_, _) ->
                  return
                    DRecord(
                      responseTypeName,
                      responseTypeName,
                      [],
                      Map
                        [ "statusCode", DInt64 500L
                          "headers", DList(VT.tuple VT.string VT.string [], [])
                          "body", DList(VT.uint8, "Internal Server Error" |> System.Text.Encoding.UTF8.GetBytes |> Array.toList |> List.map DUInt8) ]
                    )
              }
            | None ->
              task {
                return
                  DRecord(
                    responseTypeName,
                    responseTypeName,
                    [],
                    Map
                      [ "statusCode", DInt64 404L
                        "headers", DList(VT.tuple VT.string VT.string [], [])
                        "body", DList(VT.uint8, $"Not Found: {requestMethod} {requestPath}" |> System.Text.Encoding.UTF8.GetBytes |> Array.toList |> List.map DUInt8) ]
                  )
              }

          // Write response
          match darkResponse with
          | DRecord(_, _, _, respFields) ->
            let statusCode =
              Map.find "statusCode" respFields
              |> Option.bind (function DInt64 c -> Some(int c) | _ -> None)
              |> Option.defaultValue 200
            let respHeaders =
              Map.find "headers" respFields
              |> Option.bind (function DList(_, h) -> Some h | _ -> None)
              |> Option.defaultValue []
            let respBody =
              Map.find "body" respFields
              |> Option.bind (function
                | DList(_, bytes) ->
                  bytes
                  |> List.choose (function DUInt8 b -> Some b | _ -> None)
                  |> List.toArray
                  |> Some
                | _ -> None)
              |> Option.defaultValue [||]

            ctx.Response.StatusCode <- statusCode

            respHeaders
            |> List.iter (fun h ->
              match h with
              | DTuple(DString key, DString value, []) ->
                try
                  ctx.Response.Headers[key] <- Microsoft.Extensions.Primitives.StringValues(value)
                with _ ->
                  ()
              | _ -> ())

            ctx.Response.ContentLength <- Nullable(int64 respBody.Length)
            do! ctx.Response.Body.WriteAsync(respBody, 0, respBody.Length)
          | _ ->
            ctx.Response.StatusCode <- 500

        with _ ->
          ctx.Response.StatusCode <- 500
      }

    app.Run(RequestDelegate handleRequest)

    // Run the server (blocks until stopped)
    do! app.RunAsync()

    return DInt64 0L
  }


let fns : List<BuiltInFn> =
  [ { name = fn "httpServeHandlers" 0
      typeParams = []
      parameters =
        [ Param.make "port" TInt64 "Port to listen on"
          Param.make "host" TString "Host to bind to (e.g., '0.0.0.0')"
          Param.make "handlers" (TList(TCustomType(Ok httpHandlerTypeName, []))) "List of HttpHandler values to serve" ]
      returnType = TInt64
      description = "Runs an HTTP server with the given handlers. Blocks until stopped. Returns exit code."
      fn =
        (function
        | exeState, _, _, [ DInt64 port; DString host; DList(_, handlers) ] ->
          uply {
            let! result = runHttpServeHandlers exeState (int port) host handlers
            return result
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]


let builtins : Builtins = Builtin.make [] fns
