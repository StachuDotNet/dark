module ClientTypes.Init

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Tablecloth


// todo: actually call this.
let init() =
  do Json.Vanilla.allow<ClientTypes.Pusher.Payloads.NewTraceID> "LibBackend.Pusher"
  do Json.Vanilla.allow<ClientTypes.Pusher.Payloads.AddOpEventV1> "ClientTypes.Pusher"
  //do Json.Vanilla.allow<NewTraceID> "ClientTypes.Pusher"
