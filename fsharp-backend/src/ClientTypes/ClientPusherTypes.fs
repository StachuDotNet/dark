/// Types used for data "pushed" with with Pusher.com
module ClientTypes.Pusher

open Prelude
open Tablecloth

module AT = LibExecution.AnalysisTypes // todo: the clienttypes variant of this?
module COT = ClientTypes.Ops

/// Payloads that we send to the client via Pusher.com
module Event =
  type NewTraceID = AT.TraceID * tlid list

  type AddOpEventV1 =
    { result : COT.AddOpResultV1.T
      ``params`` : COT.AddOpParamsV1.T }

  type AddOpEventTooBigPayload = { tlids : List<tlid> }

  type F404 = string * string * string * NodaTime.Instant * AT.TraceID

  type DeployStatus =
    | Deploying
    | Deployed

  type NewStaticDeploy =


    { deployHash : string
      url : string
      lastUpdate : NodaTime.Instant
      status : DeployStatus }


  type WorkerState =
    | Running
    | Blocked
    | Paused

    override this.ToString() : string =
      match this with
      | Running -> "run"
      | Blocked -> "block"
      | Paused -> "pause"

  type UpdateWorkerStates = Map<string, WorkerState>

  type T =
    | NewTraceID of NewTraceID
    | New404 of F404
    | NewStaticDeploy of NewStaticDeploy
    | UpdateWorkerStates of UpdateWorkerStates
    | AddOpV1 of AddOpEventV1
    | AddOpTooBig of AddOpEventTooBigPayload
    // TODO: remove this once DarkInternal::pushStrollerEvent is ... removed?
    | Custom of eventName : string * payload : string

  let toEventNameAndPayload (e : T) : string * string =
    match e with
    | NewTraceID payload -> "new_trace", Json.Vanilla.serialize payload
    | New404 payload -> "new_404", Json.Vanilla.serialize payload
    | NewStaticDeploy payload -> "new_static_deploy", Json.Vanilla.serialize payload
    | UpdateWorkerStates payload -> "worker_state", Json.Vanilla.serialize payload
    | AddOpV1 payload -> "v1/add_op", Json.Vanilla.serialize payload
    | AddOpTooBig payload -> "addOpTooBig", Json.Vanilla.serialize payload
    | Custom (eventName, payload) -> (eventName, payload)
