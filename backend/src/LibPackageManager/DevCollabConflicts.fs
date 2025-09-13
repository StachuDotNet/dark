/// Conflict detection and resolution for developer collaboration
module LibPackageManager.DevCollabConflicts

open Prelude
open LibPackageManager.DevCollab

type ConflictType =
  | SameFunctionDifferentImpl of fnId: uuid * patch1: PatchId * patch2: PatchId
  | DeletedDependency of deleted: uuid * dependent: uuid
  | TypeIncompatibility of typeId: uuid * patch1: PatchId * patch2: PatchId
  | NameCollision of name: string * patch1: PatchId * patch2: PatchId

type Conflict = {
  id: uuid
  type_: ConflictType
  description: string
  severity: High | Medium | Low
  canAutoResolve: bool
}

type ConflictResolution =
  | KeepLocal
  | KeepRemote
  | Merge of newOps: List<PackageOp>
  | Manual of instructions: string

type ConflictResult = {
  conflicts: List<Conflict>
  autoResolved: List<Conflict * ConflictResolution>
  requiresManual: List<Conflict>
}

/// Extract entity IDs that a patch modifies
let getModifiedEntities (patch: Patch) : Set<uuid> =
  patch.ops
  |> List.fold Set.empty (fun acc op ->
    match op with
    | AddFunction(id, _, _, _) -> Set.add id acc
    | UpdateFunction(id, _, _) -> Set.add id acc
    | AddType(id, _, _) -> Set.add id acc
    | UpdateType(id, _, _) -> Set.add id acc
    | AddValue(id, _, _) -> Set.add id acc
    | MoveEntity(_, _, id) -> Set.add id acc
    | DeprecateEntity(id, _, _) -> Set.add id acc)

/// Extract entity names that a patch creates
let getCreatedNames (patch: Patch) : Set<string> =
  patch.ops
  |> List.fold Set.empty (fun acc op ->
    match op with
    | AddFunction(_, name, _, _) -> Set.add (name.ToString()) acc
    | AddType(_, name, _) -> Set.add (name.ToString()) acc
    | AddValue(_, name, _) -> Set.add (name.ToString()) acc
    | _ -> acc)

/// Detect conflicts between two patches
let detectConflicts (local: Patch) (remote: Patch) : List<Conflict> =
  let conflicts = []
  
  // Check for same entity modifications
  let localEntities = getModifiedEntities local
  let remoteEntities = getModifiedEntities remote
  let commonEntities = Set.intersect localEntities remoteEntities
  
  let entityConflicts =
    commonEntities
    |> Set.toList
    |> List.map (fun entityId ->
      { id = System.Guid.NewGuid()
        type_ = SameFunctionDifferentImpl(entityId, local.id, remote.id)
        description = $"Both patches modify entity {entityId}"
        severity = High
        canAutoResolve = false })
  
  // Check for name collisions
  let localNames = getCreatedNames local
  let remoteNames = getCreatedNames remote
  let commonNames = Set.intersect localNames remoteNames
  
  let nameConflicts =
    commonNames
    |> Set.toList
    |> List.map (fun name ->
      { id = System.Guid.NewGuid()
        type_ = NameCollision(name, local.id, remote.id)
        description = $"Both patches create entity with name '{name}'"
        severity = High
        canAutoResolve = false })
  
  // TODO: Check for dependency conflicts
  // TODO: Check for type compatibility conflicts
  
  List.append conflicts (List.append entityConflicts nameConflicts)

/// Attempt to auto-resolve simple conflicts
let autoResolveConflicts (conflicts: List<Conflict>) : List<Conflict * ConflictResolution> =
  conflicts
  |> List.filter (fun c -> c.canAutoResolve)
  |> List.map (fun c ->
    match c.type_ with
    | SameFunctionDifferentImpl(_, _, _) ->
      // For now, always prefer the remote version (could be configurable)
      (c, KeepRemote)
    | NameCollision(_, _, _) ->
      // Rename the local version to avoid collision
      (c, Manual "Rename the local entity to avoid name collision")
    | _ ->
      (c, Manual "Manual resolution required"))

/// Main conflict detection for a list of patches
let analyzeConflicts (patches: List<Patch>) : ConflictResult =
  let allConflicts = 
    patches
    |> List.fold [] (fun acc patch1 ->
      patches
      |> List.fold acc (fun innerAcc patch2 ->
        if patch1.id <> patch2.id then
          let conflicts = detectConflicts patch1 patch2
          List.append innerAcc conflicts
        else
          innerAcc))
  
  let autoResolved = autoResolveConflicts allConflicts
  let autoResolvedIds = autoResolved |> List.map (fun (c, _) -> c.id) |> Set.ofList
  
  let requiresManual = 
    allConflicts 
    |> List.filter (fun c -> not (Set.contains c.id autoResolvedIds))
  
  { conflicts = allConflicts
    autoResolved = autoResolved
    requiresManual = requiresManual }

/// Format conflict for display in CLI
let formatConflict (conflict: Conflict) : string =
  let severityIcon = match conflict.severity with
                     | High -> "🔴"
                     | Medium -> "🟡" 
                     | Low -> "🟢"
  
  let typeDescription = match conflict.type_ with
                        | SameFunctionDifferentImpl(_, p1, p2) -> 
                          $"Same function modified in patches {p1} and {p2}"
                        | NameCollision(name, p1, p2) -> 
                          $"Name '{name}' created in both patches {p1} and {p2}"
                        | DeletedDependency(_, _) -> 
                          "Dependency was deleted"
                        | TypeIncompatibility(_, p1, p2) -> 
                          $"Type incompatibility between patches {p1} and {p2}"
  
  $"{severityIcon} {conflict.description}\n  {typeDescription}"

/// Simple validation for a single patch
let validatePatch (patch: Patch) : ValidationResult =
  let errors = []
  
  // Check for empty patch
  let errors = 
    if List.isEmpty patch.ops then
      "Patch must contain at least one operation" :: errors
    else errors
  
  // Check for valid intent
  let errors =
    if System.String.IsNullOrWhiteSpace patch.intent then
      "Patch must have a non-empty intent description" :: errors
    else errors
  
  // TODO: Add type checking
  // TODO: Add dependency validation
  // TODO: Add name uniqueness checking
  
  match errors with
  | [] -> Valid
  | errs -> Invalid errs