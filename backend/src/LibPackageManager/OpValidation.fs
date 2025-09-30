module LibPackageManager.OpValidation

open Prelude
open LibExecution.ProgramTypes

module PT = LibExecution.ProgramTypes

let validateOp (op: PT.Op.T) (projection: SessionProjection.SessionProjection) : PT.ValidationResult.T =
  match op with
  | PT.Op.AddFunctionContent (_hash, _content) ->
    // Check if content already exists (no problem if it does - content is immutable)
    { isValid = true; conflicts = []; dependencies = []; warnings = []; suggestions = [] }

  | PT.Op.AddTypeContent (_hash, _content) ->
    { isValid = true; conflicts = []; dependencies = []; warnings = []; suggestions = [] }

  | PT.Op.AddValueContent (_hash, _content) ->
    { isValid = true; conflicts = []; dependencies = []; warnings = []; suggestions = [] }

  | PT.Op.CreateName (location, hash, _contentType) ->
    // Check if name already exists in this projection
    let locationKey = PT.PackageLocation.toString location
    let existsInBase = Map.containsKey locationKey projection.baseState
    let existsInOverride = Map.containsKey locationKey projection.nameOverrides

    if existsInBase || existsInOverride then
      let existing =
        match Map.tryFind locationKey projection.nameOverrides with
        | Some entry -> entry.hash
        | None ->
          match Map.tryFind locationKey projection.baseState with
          | Some entry -> entry.hash
          | None -> hash // shouldn't happen

      if existing <> hash then
        { isValid = false
          conflicts = [PT.Conflict.NameConflict(location, existing, hash)]
          dependencies = []
          warnings = []
          suggestions = ["Use UpdateNamePointer to change existing name"] }
      else
        // Same hash - no problem
        { isValid = true; conflicts = []; dependencies = []; warnings = []; suggestions = [] }
    else
      { isValid = true; conflicts = []; dependencies = []; warnings = []; suggestions = [] }

  | PT.Op.UpdateNamePointer (location, oldHash, newHash) ->
    // Verify the old hash matches current state
    let locationKey = PT.PackageLocation.toString location
    let currentHash =
      match Map.tryFind locationKey projection.nameOverrides with
      | Some entry -> Some entry.hash
      | None ->
        match Map.tryFind locationKey projection.baseState with
        | Some entry -> Some entry.hash
        | None -> None

    match currentHash with
    | Some current when current <> oldHash ->
      { isValid = false
        conflicts = [PT.Conflict.NameConflict(location, current, newHash)]
        dependencies = []
        warnings = []
        suggestions = [$"Current hash is {current}, not {oldHash}"] }
    | None ->
      { isValid = false
        conflicts = []
        dependencies = []
        warnings = [$"Name {PT.PackageLocation.toString location} does not exist"]
        suggestions = ["Use CreateName for new names"] }
    | _ ->
      { isValid = true; conflicts = []; dependencies = []; warnings = []; suggestions = [] }

  | PT.Op.MoveName (oldLocation, newLocation) ->
    // Check old exists and new doesn't
    let oldKey = PT.PackageLocation.toString oldLocation
    let newKey = PT.PackageLocation.toString newLocation

    let oldExists =
      Map.containsKey oldKey projection.nameOverrides ||
      Map.containsKey oldKey projection.baseState

    let newExists =
      Map.containsKey newKey projection.nameOverrides ||
      Map.containsKey newKey projection.baseState

    match oldExists, newExists with
    | false, _ ->
      { isValid = false
        conflicts = []
        dependencies = []
        warnings = [$"Source name {oldKey} does not exist"]
        suggestions = [] }
    | true, true ->
      { isValid = false
        conflicts = []
        dependencies = []
        warnings = [$"Destination name {newKey} already exists"]
        suggestions = ["Deprecate or update the existing name first"] }
    | true, false ->
      { isValid = true; conflicts = []; dependencies = []; warnings = []; suggestions = [] }

  | PT.Op.UnassignName location ->
    let locationKey = PT.PackageLocation.toString location
    let exists =
      Map.containsKey locationKey projection.nameOverrides ||
      Map.containsKey locationKey projection.baseState

    if not exists then
      { isValid = false
        conflicts = []
        dependencies = []
        warnings = [$"Name {locationKey} does not exist"]
        suggestions = [] }
    else
      { isValid = true; conflicts = []; dependencies = []; warnings = []; suggestions = [] }

  | PT.Op.DeprecateContent (_hash, _reason, _replacement) ->
    // Content deprecation is always valid - we can deprecate any hash
    { isValid = true; conflicts = []; dependencies = []; warnings = []; suggestions = [] }