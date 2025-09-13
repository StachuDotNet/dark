/// Simple HTTP server for developer collaboration patch sharing
module DevCollabServer.Server

open System
open System.Threading.Tasks
open FSharp.Control.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Newtonsoft.Json

open Prelude
open LibPackageManager.DevCollab
open LibPackageManager.DevCollabDb

type PatchDto = {
  id: string
  author: string
  intent: string
  status: string
  createdAt: string
  opsCount: int
}

type SyncRequest = {
  patches: PatchDto list
}

type SyncResponse = {
  success: bool
  conflicts: string list
  patches: PatchDto list
}

let patchToDto (patch: Patch) : PatchDto =
  { id = patch.id.ToString()
    author = patch.author
    intent = patch.intent
    status = match patch.status with
             | Draft -> "draft"
             | Ready -> "ready"
             | Applied -> "applied"
             | Rejected -> "rejected"
    createdAt = patch.createdAt.ToString("yyyy-MM-dd HH:mm:ss")
    opsCount = List.length patch.ops }

let handlePush (context: HttpContext) : Task<unit> =
  task {
    try
      use reader = new System.IO.StreamReader(context.Request.Body)
      let! requestBody = reader.ReadToEndAsync()
      let request = JsonConvert.DeserializeObject<SyncRequest>(requestBody)
      
      // For now, just accept all patches
      let response = {
        success = true
        conflicts = []
        patches = []
      }
      
      context.Response.ContentType <- "application/json"
      let responseJson = JsonConvert.SerializeObject(response)
      do! context.Response.WriteAsync(responseJson)
      
    with
    | ex ->
      context.Response.StatusCode <- 500
      do! context.Response.WriteAsync($"Error: {ex.Message}")
  }

let handlePull (context: HttpContext) : Task<unit> =
  task {
    try
      // Get patches since timestamp (or all if no timestamp provided)
      let! patches = loadPatches ()
      let patchDtos = patches |> List.map patchToDto
      
      let response = {
        success = true
        conflicts = []
        patches = patchDtos
      }
      
      context.Response.ContentType <- "application/json"
      let responseJson = JsonConvert.SerializeObject(response)
      do! context.Response.WriteAsync(responseJson)
      
    with
    | ex ->
      context.Response.StatusCode <- 500
      do! context.Response.WriteAsync($"Error: {ex.Message}")
  }

let handleGetPatch (context: HttpContext) : Task<unit> =
  task {
    try
      let patchId = context.Request.RouteValues.["id"] :?> string
      match Guid.TryParse patchId with
      | true, guid ->
        let! patchOpt = loadPatchById guid
        match patchOpt with
        | Some patch ->
          let patchDto = patchToDto patch
          context.Response.ContentType <- "application/json"
          let responseJson = JsonConvert.SerializeObject(patchDto)
          do! context.Response.WriteAsync(responseJson)
        | None ->
          context.Response.StatusCode <- 404
          do! context.Response.WriteAsync("Patch not found")
      | false, _ ->
        context.Response.StatusCode <- 400
        do! context.Response.WriteAsync("Invalid patch ID")
    with
    | ex ->
      context.Response.StatusCode <- 500
      do! context.Response.WriteAsync($"Error: {ex.Message}")
  }

let configureServices (services: IServiceCollection) : unit =
  services.AddRouting() |> ignore

let configureApp (app: IApplicationBuilder) : unit =
  app.UseRouting() |> ignore
  
  app.UseEndpoints(fun endpoints ->
    endpoints.MapPost("/patches/push", Func<HttpContext, Task>(handlePush)) |> ignore
    endpoints.MapGet("/patches/pull", Func<HttpContext, Task>(handlePull)) |> ignore
    endpoints.MapGet("/patches/{id}", Func<HttpContext, Task>(handleGetPatch)) |> ignore
    endpoints.MapGet("/", fun context ->
      task {
        context.Response.ContentType <- "text/html"
        do! context.Response.WriteAsync("""
          <h1>Darklang Developer Collaboration Server</h1>
          <p>API Endpoints:</p>
          <ul>
            <li>POST /patches/push - Push patches to server</li>
            <li>GET /patches/pull - Pull patches from server</li>
            <li>GET /patches/{id} - Get specific patch</li>
          </ul>
        """)
      } :> Task
    ) |> ignore
  ) |> ignore

let createHost (urls: string array) : IHost =
  Host.CreateDefaultBuilder()
    .ConfigureWebHostDefaults(fun webBuilder ->
      webBuilder
        .UseUrls(urls)
        .ConfigureServices(configureServices)
        .Configure(configureApp)
      |> ignore
    )
    .Build()

let startServer (port: int) : Task<IHost> =
  task {
    let urls = [| $"http://localhost:{port}" |]
    let host = createHost urls
    
    // Initialize database
    do! initSchema ()
    
    do! host.StartAsync()
    printfn $"🚀 DevCollab server started on http://localhost:{port}"
    printfn "Press Ctrl+C to stop"
    
    return host
  }

// Entry point for running as standalone server
[<EntryPoint>]
let main args =
  let port = if args.Length > 0 then int args.[0] else 3000
  
  task {
    let! host = startServer port
    
    // Wait for shutdown signal
    let cancellationToken = System.Threading.CancellationToken.None
    do! host.WaitForShutdownAsync(cancellationToken)
    
    return 0
  }
  |> fun t -> t.Result