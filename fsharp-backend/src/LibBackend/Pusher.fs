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


module ClientEvent = ClientTypes.Pusher.Event


module Event =
  let new404 space name modifier timestamp traceID : ClientEvent.T =
    ClientEvent.New404(space, name, modifier, timestamp, traceID)

  let newStaticDeploy deployHash url lastUpdate status : ClientEvent.T =
    ClientEvent.NewStaticDeploy
      { deployHash = deployHash
        url = url
        lastUpdate = lastUpdate
        status =
          match status with
          | StaticAssets.Deploying -> ClientEvent.DeployStatus.Deploying
          | StaticAssets.Deployed -> ClientEvent.DeployStatus.Deployed }


  let updateWorkerStates workerStates =
    workerStates
    |> Map.map (fun state ->
      match state with
      | QueueSchedulingRules.WorkerStates.Blocked -> ClientEvent.WorkerState.Blocked
      | QueueSchedulingRules.WorkerStates.Running -> ClientEvent.WorkerState.Running
      | QueueSchedulingRules.WorkerStates.Paused -> ClientEvent.WorkerState.Paused)
    |> ClientEvent.UpdateWorkerStates

  let addOpV1 p r =
    ClientEvent.AddOpV1
      { result = r |> Op.AddOpResultV1.toClientType
        ``params`` = p |> Op.AddOpParamsV1.toClientType }


  let addOpTooBig tlids = ClientEvent.AddOpTooBig { tlids = tlids }


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
let push (canvasID) (evt : ClientEvent.T) (fallback : Option<ClientEvent.T>) : unit =
  let (eventName, payload) = ClientEvent.toEventNameAndPayload evt

  let (eventName, payload) =
    if String.length payload > 10240 then
      match fallback with
      | Some fallback -> ClientEvent.toEventNameAndPayload fallback // TODO: maybe log here?
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
