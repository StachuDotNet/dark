module ClientTypes.Init

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Tablecloth


// todo: actually call this.
let init() =
  do Json.Vanilla.allow<ClientTypes.Pusher.Event.NewTraceID> "LibBackend.Pusher"
  do Json.Vanilla.allow<ClientTypes.Pusher.Event.AddOpEventV1> "ClientTypes.Pusher"
  // todo: the rest
  //do Json.Vanilla.allow<NewTraceID> "ClientTypes.Pusher"
