/// Types used for data "pushed" with with Pusher.com
module ClientTypes.Pusher

open Prelude
open Tablecloth

module AT = LibExecution.AnalysisTypes // todo: the clienttypes variant of this?
module COT = ClientTypes.Ops

/// Payloads that we send to the client via Pusher.com
module Payloads =
  type NewTraceID = AT.TraceID * tlid list

  type AddOpEventV1 =
    { result : COT.AddOpResultV1.T
      ``params`` : COT.AddOpParamsV1.T }

  type AddOpEventTooBigPayload = { tlids : List<tlid> }

  type F404 = string * string * string * NodaTime.Instant * AT.TraceID

  module NewStaticDeploy =
    type DeployStatus =
      | Deploying
      | Deployed

    type T =
      { deployHash : string
        url : string
        lastUpdate : NodaTime.Instant
        status : DeployStatus }

  module UpdateWorkerStates =
    type WorkerState =
      | Running
      | Blocked
      | Paused

      override this.ToString() : string =
        match this with
        | Running -> "run"
        | Blocked -> "block"
        | Paused -> "pause"

    type T = Map<string, WorkerState>
