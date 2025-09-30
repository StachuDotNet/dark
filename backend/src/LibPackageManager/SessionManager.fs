module LibPackageManager.SessionManager

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.ProgramTypes

module PT = LibExecution.ProgramTypes

// ===================================
// Default Session Management
// ===================================

let private defaultSessionId = System.Guid.Parse("00000000-0000-0000-0000-000000000000")

/// Ensure the default session exists
let ensureDefaultSession () : Ply<unit> =
  uply {
    match SessionProjection.getSession defaultSessionId with
    | Some _ -> return ()
    | None ->
      // Create default session from current database state
      let _projection = SessionProjection.createSession defaultSessionId
      // TODO: Load current package state from database into baseState
      return ()
  }

/// Get the current default session
let getDefaultSession () : Ply<SessionProjection.SessionProjection> =
  uply {
    do! ensureDefaultSession ()
    match SessionProjection.getSession defaultSessionId with
    | Some session -> return session
    | None -> return Exception.raiseInternal "Default session should exist after ensure" []
  }

// ===================================
// Session-Aware Package Operations
// ===================================

/// Get a type from the specified session (or default if None)
let getType (sessionId: uuid option) (typeId: uuid) : Ply<Option<PT.PackageType.PackageType>> =
  uply {
    let sessionId = defaultArg sessionId defaultSessionId
    match SessionProjection.getSession sessionId with
    | None -> return None
    | Some _projection ->
      // TODO: Look up type in session projection
      // For now, fall back to original implementation
      return! LibPackageManager.ProgramTypes.Type.get typeId
  }

/// Get a function from the specified session (or default if None)
let getFn (sessionId: uuid option) (fnId: uuid) : Ply<Option<PT.PackageFn.PackageFn>> =
  uply {
    let sessionId = defaultArg sessionId defaultSessionId
    match SessionProjection.getSession sessionId with
    | None -> return None
    | Some _projection ->
      // TODO: Look up function in session projection
      // For now, fall back to original implementation
      return! LibPackageManager.ProgramTypes.Fn.get fnId
  }

/// Get a value from the specified session (or default if None)
let getValue (sessionId: uuid option) (valueId: uuid) : Ply<Option<PT.PackageValue.PackageValue>> =
  uply {
    let sessionId = defaultArg sessionId defaultSessionId
    match SessionProjection.getSession sessionId with
    | None -> return None
    | Some _projection ->
      // TODO: Look up value in session projection
      // For now, fall back to original implementation
      return! LibPackageManager.ProgramTypes.Value.get valueId
  }

/// Search packages in the specified session (or default if None)
let searchInSession (sessionId: uuid option) (query: PT.Search.SearchQuery) : Ply<PT.Search.SearchResults> =
  uply {
    let sessionId = defaultArg sessionId defaultSessionId
    match SessionProjection.getSession sessionId with
    | None ->
      // Fall back to global search
      return! LibPackageManager.ProgramTypes.search query
    | Some _projection ->
      // TODO: Implement session-aware search that includes overrides
      // For now, delegate to existing search
      return! LibPackageManager.ProgramTypes.search query
  }

// ===================================
// High-Level Operations
// ===================================

let applyOpsToSession (sessionId: uuid) (ops: List<PT.Op.T>) : Ply<Result<unit, string>> =
  uply {
    match SessionProjection.getSession sessionId with
    | None -> return Error $"Session {sessionId} not found"
    | Some projection ->
      let mutable currentProjection = projection
      let mutable errors = []

      for op in ops do
        let validation = OpValidation.validateOp op currentProjection
        if validation.isValid then
          currentProjection <- OpExecution.applyOpToProjection op currentProjection
          currentProjection <- { currentProjection with appliedOps = currentProjection.appliedOps @ [op] }
        else
          let conflictMsgs =
            validation.conflicts
            |> List.map (fun c ->
              match c with
              | PT.Conflict.NameConflict(loc, h1, h2) ->
                $"Name conflict at {PT.PackageLocation.toString loc}: {h1} vs {h2}"
              | PT.Conflict.TypeIncompatibility(loc, _old, _new) ->
                $"Type incompatibility at {PT.PackageLocation.toString loc}")
          errors <- errors @ conflictMsgs @ validation.warnings

      if List.isEmpty errors then
        SessionProjection.updateSession sessionId currentProjection |> ignore<bool>
        return Ok ()
      else
        return Error (String.concat "; " errors)
  }

// ===================================
// Package Search (Session-Aware)
// ===================================

// This old function is now replaced by the searchInSession function above

// ===================================
// Patch Validation (using Op validation)
// ===================================

let validatePatch (patch: PT.Patch.T) (sessionId: uuid) : Ply<PT.ValidationResult.T> =
  uply {
    match SessionProjection.getSession sessionId with
    | None ->
      return { isValid = false
               conflicts = []
               dependencies = []
               warnings = ["Session not found"]
               suggestions = [] }
    | Some projection ->
      let mutable currentProjection = projection
      let mutable allConflicts = []
      let mutable allWarnings = []
      let mutable allSuggestions = []

      for op in patch.ops do
        let validation = OpValidation.validateOp op currentProjection
        if validation.isValid then
          currentProjection <- OpExecution.applyOpToProjection op currentProjection
        else
          allConflicts <- allConflicts @ validation.conflicts
          allWarnings <- allWarnings @ validation.warnings
          allSuggestions <- allSuggestions @ validation.suggestions

      return { isValid = List.isEmpty allConflicts
               conflicts = allConflicts
               dependencies = []
               warnings = allWarnings
               suggestions = allSuggestions }
  }