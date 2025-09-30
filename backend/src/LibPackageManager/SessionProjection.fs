module LibPackageManager.SessionProjection

open System.Collections.Concurrent
open Prelude
open LibExecution.ProgramTypes

module PT = LibExecution.ProgramTypes

/// Session projection - represents the package state for a specific session
type SessionProjection = {
  sessionId: uuid
  baseState: Map<string, NameStore.NameEntry>  // Starting point
  appliedOps: List<PT.Op.T>                     // Operations applied in this session
  nameOverrides: Map<string, NameStore.NameEntry> // Session-specific name mappings
}

/// Active session projections
let private activeProjections = ConcurrentDictionary<uuid, SessionProjection>()

let createSession (sessionId: uuid) : SessionProjection =
  let projection = {
    sessionId = sessionId
    baseState = Map.empty  // TODO: Load from current global state
    appliedOps = []
    nameOverrides = Map.empty
  }
  activeProjections.AddOrUpdate(sessionId, projection, fun _ _ -> projection) |> ignore<SessionProjection>
  projection

let getSession (sessionId: uuid) : Option<SessionProjection> =
  match activeProjections.TryGetValue(sessionId) with
  | true, projection -> Some projection
  | false, _ -> None

let updateSession (sessionId: uuid) (projection: SessionProjection) : bool =
  match activeProjections.TryGetValue(sessionId) with
  | true, oldProjection ->
    activeProjections.TryUpdate(sessionId, projection, oldProjection)
  | false, _ -> false