module LibPackageManager.SessionAwarePackageManager

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.ProgramTypes
open LibExecution.RuntimeTypes

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module PMRT = LibPackageManager.RuntimeTypes

/// Create a PackageManager that uses the specified session for lookups
/// For now, this delegates to the existing RuntimeTypes lookups
/// TODO: Make this session-aware once we implement session projection lookup
let createForSession (_sessionId: uuid option) : RT.PackageManager =
  { getType = fun typeId -> PMRT.Type.get typeId
    getValue = fun valueId -> PMRT.Value.get valueId
    getFn = fun fnId -> PMRT.Fn.get fnId
    init =
      uply {
        do! SessionManager.ensureDefaultSession ()
        return ()
      } }

/// Create a PackageManager that uses the default session
let createDefault () : RT.PackageManager =
  createForSession None

/// Create a PackageManager for a specific session
let createForSpecificSession (sessionId: uuid) : RT.PackageManager =
  createForSession (Some sessionId)