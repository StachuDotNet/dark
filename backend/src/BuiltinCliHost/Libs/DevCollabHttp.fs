/// HTTP client operations for syncing with collaboration server
module BuiltinCliHost.Libs.DevCollabHttp

open System.Threading.Tasks
open FSharp.Control.Tasks
open System.Net.Http
open System.Text
open Newtonsoft.Json

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module Errors = LibExecution.Errors

type SyncRequest = {
  patches: obj list
}

type SyncResponse = {
  success: bool
  conflicts: string list
  patches: obj list
}

let httpClient = new HttpClient()

let fns : List<BuiltInFn> =
  [ { name = fn "devCollabHttpPush" 0
      typeParams = []
      parameters = [ Param.make "serverUrl" TString ""; Param.make "patches" (TList(TDict TString)) "" ]
      returnType = TDict TString
      description = "Push patches to collaboration server"
      fn =
        (function
        | _, _, _, [ DString serverUrl; DList(_, patches) ] ->
          uply {
            try
              let request = { patches = [] } // TODO: Convert patches
              let requestJson = JsonConvert.SerializeObject(request)
              let content = new StringContent(requestJson, Encoding.UTF8, "application/json")
              
              let! response = httpClient.PostAsync($"{serverUrl}/patches/push", content)
              let! responseText = response.Content.ReadAsStringAsync()
              
              if response.IsSuccessStatusCode then
                return
                  Map.ofList [
                    ("success", DString "true")
                    ("message", DString "Patches pushed successfully")
                    ("response", DString responseText)
                  ]
                  |> DDict (ValueType.Known KTString)
              else
                return
                  Map.ofList [
                    ("success", DString "false")
                    ("error", DString $"HTTP {response.StatusCode}: {responseText}")
                  ]
                  |> DDict (ValueType.Known KTString)
            with
            | ex ->
              return
                Map.ofList [
                  ("success", DString "false")
                  ("error", DString ex.Message)
                ]
                |> DDict (ValueType.Known KTString)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }

    { name = fn "devCollabHttpPull" 0
      typeParams = []
      parameters = [ Param.make "serverUrl" TString "" ]
      returnType = TDict TString
      description = "Pull patches from collaboration server"
      fn =
        (function
        | _, _, _, [ DString serverUrl ] ->
          uply {
            try
              let! response = httpClient.GetAsync($"{serverUrl}/patches/pull")
              let! responseText = response.Content.ReadAsStringAsync()
              
              if response.IsSuccessStatusCode then
                return
                  Map.ofList [
                    ("success", DString "true")
                    ("message", DString "Patches pulled successfully")
                    ("response", DString responseText)
                  ]
                  |> DDict (ValueType.Known KTString)
              else
                return
                  Map.ofList [
                    ("success", DString "false")
                    ("error", DString $"HTTP {response.StatusCode}: {responseText}")
                  ]
                  |> DDict (ValueType.Known KTString)
            with
            | ex ->
              return
                Map.ofList [
                  ("success", DString "false")
                  ("error", DString ex.Message)
                ]
                |> DDict (ValueType.Known KTString)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }

    { name = fn "devCollabHttpStatus" 0
      typeParams = []
      parameters = [ Param.make "serverUrl" TString "" ]
      returnType = TDict TString
      description = "Check collaboration server status"
      fn =
        (function
        | _, _, _, [ DString serverUrl ] ->
          uply {
            try
              let! response = httpClient.GetAsync(serverUrl)
              let isConnected = response.IsSuccessStatusCode
              
              return
                Map.ofList [
                  ("connected", DString (if isConnected then "true" else "false"))
                  ("server", DString serverUrl)
                  ("status", DString (response.StatusCode.ToString()))
                ]
                |> DDict (ValueType.Known KTString)
            with
            | ex ->
              return
                Map.ofList [
                  ("connected", DString "false")
                  ("server", DString serverUrl)
                  ("error", DString ex.Message)
                ]
                |> DDict (ValueType.Known KTString)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]

let builtins = LibExecution.Builtin.make [] fns []