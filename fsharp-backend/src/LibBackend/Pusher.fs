/// Module supporting Pusher.com usage
module LibBackend.Pusher

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Tablecloth

module AT = LibExecution.AnalysisTypes
module FireAndForget = LibService.FireAndForget


let pusherClient : Lazy<PusherServer.Pusher> =
  lazy
    (let options = PusherServer.PusherOptions()
     options.Cluster <- Config.pusherCluster
     options.Encrypted <- true
     // Use the raw serializer and expect everywhere to serialize appropriately
     options.set_JsonSerializer (PusherServer.RawBodySerializer())

     PusherServer.Pusher(
       Config.pusherID,
       Config.pusherKey,
       Config.pusherSecret,
       options
     ))

type EventTooBigEvent = { eventName : string }


module Payload = ClientTypes.Pusher.Payloads

// todo: move this to ClientTypes, and only reference client types
module Event =
  type T =
    | NewTraceID of traceID : AT.TraceID * tlids : List<tlid>

    | New404 of
      space : string *
      name : string *
      modifier : string *
      timestamp : NodaTime.Instant *
      traceID : AT.TraceID

    | NewStaticDeploy of
      deployHash : string *
      url : string *
      lastUpdate : NodaTime.Instant *
      status : StaticAssets.DeployStatus

    | UpdateWorkerStates of QueueSchedulingRules.WorkerStates.T

    | AddOpV1 of Op.AddOpParamsV1.T * Op.AddOpResultV1.T

    | AddOpTooBig of List<tlid>

    // do we really need this for anything? reconsider.
    | Custom of eventName : string * payload : string

  let toEventNameAndPayload (e : T) : string * string =
    match e with
    | NewTraceID (traceId, tlids) ->
      let payload : Payload.NewTraceID = (traceId, tlids)
      "new_trace", Json.Vanilla.serialize payload

    | New404 (space, name, modifier, timestamp, traceID) ->
      let payload = Payload.F404(space, name, modifier, timestamp, traceID)
      "new_404", Json.Vanilla.serialize payload

    | NewStaticDeploy (deployHash, url, lastUpdate, status) ->
      let payload : Payload.NewStaticDeploy.T =
        { deployHash = deployHash
          url = url
          lastUpdate = lastUpdate
          status =
            match status with
            | StaticAssets.Deploying -> Payload.NewStaticDeploy.Deploying
            | StaticAssets.Deployed -> Payload.NewStaticDeploy.Deployed }
      "new_static_deploy", Json.Vanilla.serialize payload

    | UpdateWorkerStates (states) ->
      let payload : Payload.UpdateWorkerStates.T =
        states
        |> Map.map (fun state ->
          match state with
          | QueueSchedulingRules.WorkerStates.Blocked ->
            Payload.UpdateWorkerStates.Blocked
          | QueueSchedulingRules.WorkerStates.Running ->
            Payload.UpdateWorkerStates.Running
          | QueueSchedulingRules.WorkerStates.Paused ->
            Payload.UpdateWorkerStates.Paused)
      "worker_state", Json.Vanilla.serialize payload

    | AddOpV1 (p, r) ->
      let payload : Payload.AddOpEventV1 =
        { result = r |> Op.AddOpResultV1.toClientType
          ``params`` = p |> Op.AddOpParamsV1.toClientType }

      "v1/add_op", Json.Vanilla.serialize payload

    | AddOpTooBig tlids ->
      let payload : Payload.AddOpEventTooBigPayload = { tlids = tlids }
      "addOpTooBig", Json.Vanilla.serialize payload

    | Custom (eventName, payload) -> (eventName, payload)


/// <summary>Send an event to Pusher.com.</summary>
///
/// <remarks>
/// This is fired in the background, and does not take any time from the current thread.
/// You cannot wait for it, by design.
///
/// Do not send requests over 10240 bytes. Each caller should check their payload,
/// and send a different push if appropriate (eg, instead of sending
/// `TraceData hugePayload`, send `TraceDataTooBig traceID`
/// </remarks>
let push (canvasID) (evt : Event.T) (fallback : Option<Event.T>) : unit =
  let (eventName, payload) = Event.toEventNameAndPayload evt

  let (eventName, payload) =
    if String.length payload > 10240 then
      match fallback with
      | Some fallback -> Event.toEventNameAndPayload fallback // TODO: maybe log here?
      | None -> failwithf "TODO: something"
    else
      (eventName, payload)

  FireAndForget.fireAndForgetTask $"pusher: {eventName}" (fun () ->
    task {
      // TODO: make channels private and end-to-end encrypted in order to add public canvases
      let client = Lazy.force pusherClient
      let channel = $"canvas_{canvasID}"

      let! (_ : PusherServer.ITriggerResult) =
        client.TriggerAsync(channel, eventName, payload)

      return ()
    })


type JsConfig = { enabled : bool; key : string; cluster : string }

let jsConfigString =
  // CLEANUP use JSON serialization
  $"{{enabled: true, key: '{Config.pusherKey}', cluster: '{Config.pusherCluster}'}}"
