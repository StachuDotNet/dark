/// LSP extensions for Darklang collaboration features
module LibLanguageServer.CollaborationProtocol

open Prelude
open LibPackageManager.DevCollab
open LibPackageManager.DevCollabDb
open LibPackageManager.DevCollabConflicts
open Newtonsoft.Json

type PatchPanelData = {
  draftPatches: List<PatchInfo>
  readyPatches: List<PatchInfo>  
  incomingPatches: List<PatchInfo>
  appliedPatches: List<PatchInfo>
}

type ConflictResolutionUI = {
  conflict: Conflict
  resolutionOptions: List<ResolutionOption>
  recommendedStrategy: string
  affectedFiles: List<string>
  diffData: string option
}

type ResolutionOption = {
  strategy: string
  label: string
  description: string
  isDestructive: bool
  confidence: float
}

type SessionPanelData = {
  currentSession: Session option
  availableSessions: List<Session>
  recentSessions: List<Session>
}

type ActivityInfo = {
  timestamp: System.DateTime
  activity: ActivityType
  location: string option
  description: string
}

type ActivityType =
  | EditingFunction of fnName: string
  | CreatingPatch of intent: string
  | ResolvingConflict of conflictId: string
  | ApplyingPatch of patchId: string

/// Generate UI data for patch panel
let generatePatchPanelData (userId: UserId) : Task<PatchPanelData> = task {
  let! allPatches = loadPatches ()
  let currentUser = userId
  
  let draftPatches = allPatches |> List.filter (fun p -> p.status = Draft && p.author = currentUser)
  let readyPatches = allPatches |> List.filter (fun p -> p.status = Ready && p.author = currentUser)
  let incomingPatches = allPatches |> List.filter (fun p -> p.status = Ready && p.author <> currentUser)
  let appliedPatches = allPatches |> List.filter (fun p -> p.status = Applied)
  
  return {
    draftPatches = draftPatches |> List.map patchToInfo
    readyPatches = readyPatches |> List.map patchToInfo
    incomingPatches = incomingPatches |> List.map patchToInfo
    appliedPatches = appliedPatches |> List.map patchToInfo
  }
}

/// Generate conflict resolution UI data
let generateConflictResolutionUI (conflictId: uuid) : Task<ConflictResolutionUI option> = task {
  let! conflicts = detectConflicts []
  
  match conflicts |> List.find (fun c -> c.id = conflictId) with
  | Some conflict ->
    let resolutionOptions = getResolutionOptionsForConflict conflict
    let recommendedStrategy = getRecommendedStrategy conflict
    let affectedFiles = getAffectedFiles conflict
    
    return Some {
      conflict = conflict
      resolutionOptions = resolutionOptions
      recommendedStrategy = recommendedStrategy
      affectedFiles = affectedFiles
      diffData = None // Would generate diff HTML here
    }
  | None -> return None
}

/// Generate session panel data
let generateSessionPanelData (userId: UserId) : Task<SessionPanelData> = task {
  let! currentSession = getCurrentSession userId
  let! allSessions = loadSessions ()
  
  let userSessions = allSessions |> List.filter (fun s -> s.owner = userId)
  let recentSessions = userSessions |> List.sortByDescending (fun s -> s.lastActivity) |> List.take 5
  
  return {
    currentSession = currentSession
    availableSessions = userSessions
    recentSessions = recentSessions
  }
}

/// Convert internal types to LSP-friendly info types
let patchToInfo (patch: Patch) : PatchInfo = {
  id = patch.id.ToString()
  author = patch.author
  intent = patch.intent
  status = patch.status.ToString().ToLowerInvariant()
  createdAt = patch.createdAt.ToString("O")
  functions = patch.ops |> List.choose (function
    | AddFunction(_, name, _, _) -> Some (FQFnName.Package.toString name)
    | UpdateFunction(_, _, _) -> None // Would need to resolve from ID
    | _ -> None)
}

let getResolutionOptionsForConflict (conflict: Conflict) : List<ResolutionOption> =
  match conflict.type_ with
  | SameFunctionDifferentImpl(_, _, _) ->
    [
      { strategy = "keep-local"; label = "Keep Local Changes"; description = "Use your implementation"; isDestructive = true; confidence = 0.8 }
      { strategy = "keep-remote"; label = "Keep Remote Changes"; description = "Use incoming implementation"; isDestructive = true; confidence = 0.8 }
      { strategy = "three-way"; label = "Three-Way Merge"; description = "Attempt automatic merge"; isDestructive = false; confidence = 0.6 }
      { strategy = "manual"; label = "Manual Resolution"; description = "Resolve in editor"; isDestructive = false; confidence = 1.0 }
    ]
  | NameCollision(name, _, _) ->
    [
      { strategy = "rename-both"; label = "Rename Both"; description = $"Keep both with different names"; isDestructive = false; confidence = 0.95 }
      { strategy = "keep-local"; label = "Keep Local Only"; description = $"Keep your '{name}'"; isDestructive = true; confidence = 0.7 }
      { strategy = "keep-remote"; label = "Keep Remote Only"; description = $"Keep incoming '{name}'"; isDestructive = true; confidence = 0.7 }
    ]
  | DeletedDependency(deleted, dependent) ->
    [
      { strategy = "manual"; label = "Manual Review"; description = "Review dependency usage"; isDestructive = false; confidence = 1.0 }
      { strategy = "keep-local"; label = "Restore Dependency"; description = $"Restore '{deleted}'"; isDestructive = true; confidence = 0.5 }
      { strategy = "keep-remote"; label = "Update Dependents"; description = $"Update '{dependent}'"; isDestructive = true; confidence = 0.4 }
    ]
  | TypeIncompatibility(typeId, _, _) ->
    [
      { strategy = "manual"; label = "Manual Review"; description = "Review type changes"; isDestructive = false; confidence = 1.0 }
      { strategy = "keep-local"; label = "Keep Local Type"; description = "Use your type definition"; isDestructive = true; confidence = 0.3 }
      { strategy = "keep-remote"; label = "Keep Remote Type"; description = "Use incoming type"; isDestructive = true; confidence = 0.3 }
    ]

let getRecommendedStrategy (conflict: Conflict) : string =
  match conflict.type_ with
  | SameFunctionDifferentImpl _ -> "three-way"
  | NameCollision _ -> "rename-both"
  | DeletedDependency _ -> "manual"
  | TypeIncompatibility _ -> "manual"

let getAffectedFiles (conflict: Conflict) : List<string> =
  // Would analyze conflict and return list of affected .dark files
  [] // Placeholder

/// LSP method handlers
let handlePatchesList (userId: UserId) : Task<PatchPanelData> = task {
  return! generatePatchPanelData userId
}

let handlePatchCreate (userId: UserId) (intent: string) : Task<string> = task {
  let! patchId = createPatch userId intent []
  return patchId.ToString()
}

let handlePatchView (patchId: string) : Task<PatchDetails option> = task {
  match System.Guid.TryParse(patchId) with
  | true, id ->
    let! patch = loadPatchById id
    return patch |> Option.map (fun p -> {
      patch = patchToInfo p
      diffHtml = generatePatchDiffHtml p
      timeline = generatePatchTimeline p
    })
  | false, _ -> return None
}

let handleConflictsList (userId: UserId) : Task<List<ConflictInfo>> = task {
  let! conflicts = detectConflicts []
  return conflicts |> List.map conflictToInfo
}

let handleConflictsResolve (conflictId: string) (strategy: string) : Task<ResolutionResult> = task {
  match System.Guid.TryParse(conflictId) with
  | true, id ->
    // Find conflict and apply resolution
    let! conflicts = detectConflicts []
    match conflicts |> List.find (fun c -> c.id = id) with
    | Some conflict ->
      let! result = applyResolutionStrategy conflict strategy
      return { success = true; message = $"Resolved using {strategy}"; affectedFiles = [] }
    | None ->
      return { success = false; message = "Conflict not found"; affectedFiles = [] }
  | false, _ ->
    return { success = false; message = "Invalid conflict ID"; affectedFiles = [] }
}

// Additional types needed
type PatchDetails = {
  patch: PatchInfo
  diffHtml: string
  timeline: List<TimelineEvent>
}

type TimelineEvent = {
  timestamp: System.DateTime
  event: string
  author: string
  description: string
}

type ConflictInfo = {
  id: string
  type_: string
  severity: string
  description: string
  patches: List<string>
  canAutoResolve: bool
}

type ResolutionResult = {
  success: bool
  message: string
  affectedFiles: List<string>
}

let conflictToInfo (conflict: Conflict) : ConflictInfo = {
  id = conflict.id.ToString()
  type_ = match conflict.type_ with
           | SameFunctionDifferentImpl _ -> "Same Function Different Implementation"
           | NameCollision _ -> "Name Collision" 
           | DeletedDependency _ -> "Deleted Dependency"
           | TypeIncompatibility _ -> "Type Incompatibility"
  severity = conflict.severity.ToString().ToLowerInvariant()
  description = conflict.description
  patches = [] // Would extract from conflict
  canAutoResolve = conflict.canAutoResolve
}

// Placeholder implementations
let generatePatchDiffHtml (patch: Patch) : string = 
  "<div>Patch diff HTML would be generated here</div>"

let generatePatchTimeline (patch: Patch) : List<TimelineEvent> = []

let applyResolutionStrategy (conflict: Conflict) (strategy: string) : Task<ResolutionResult> = task {
  return { success = true; message = "Applied"; affectedFiles = [] }
}