module LibPackageManager.OpExecution

open Prelude
open LibExecution.ProgramTypes

module PT = LibExecution.ProgramTypes

let applyOpToProjection (op: PT.Op.T) (projection: SessionProjection.SessionProjection) : SessionProjection.SessionProjection =
  match op with
  | PT.Op.AddFunctionContent (hash, content) ->
    ContentStore.addContent hash (ContentStore.FunctionContent content)
    projection

  | PT.Op.AddTypeContent (hash, content) ->
    ContentStore.addContent hash (ContentStore.TypeContent content)
    projection

  | PT.Op.AddValueContent (hash, content) ->
    ContentStore.addContent hash (ContentStore.ValueContent content)
    projection

  | PT.Op.CreateName (location, hash, contentType) ->
    let entry : NameStore.NameEntry = {
      location = location
      hash = hash
      contentType =
        match contentType with
        | "function" -> ContentStore.Function
        | "type" -> ContentStore.Type
        | "value" -> ContentStore.Value
        | _ -> ContentStore.Function // default
    }
    let locationKey = PT.PackageLocation.toString location
    { projection with
        nameOverrides = Map.add locationKey entry projection.nameOverrides }

  | PT.Op.UpdateNamePointer (location, _oldHash, newHash) ->
    let locationKey = PT.PackageLocation.toString location
    match Map.tryFind locationKey projection.nameOverrides with
    | Some entry ->
      let updated = { entry with hash = newHash }
      { projection with
          nameOverrides = Map.add locationKey updated projection.nameOverrides }
    | None ->
      match Map.tryFind locationKey projection.baseState with
      | Some entry ->
        let updated = { entry with hash = newHash }
        { projection with
            nameOverrides = Map.add locationKey updated projection.nameOverrides }
      | None -> projection

  | PT.Op.MoveName (oldLocation, newLocation) ->
    let oldKey = PT.PackageLocation.toString oldLocation
    let newKey = PT.PackageLocation.toString newLocation

    // Find the entry to move
    let entry =
      match Map.tryFind oldKey projection.nameOverrides with
      | Some e -> Some e
      | None -> Map.tryFind oldKey projection.baseState

    match entry with
    | Some e ->
      let updatedEntry = { e with location = newLocation }
      { projection with
          nameOverrides =
            projection.nameOverrides
            |> Map.remove oldKey
            |> Map.add newKey updatedEntry }
    | None -> projection

  | PT.Op.UnassignName location ->
    let locationKey = PT.PackageLocation.toString location
    { projection with
        nameOverrides = Map.remove locationKey projection.nameOverrides }

  | PT.Op.DeprecateContent (hash, reason, replacement) ->
    ContentStore.deprecateContent hash reason replacement
    projection