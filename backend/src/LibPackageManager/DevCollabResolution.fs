/// Advanced conflict resolution strategies and paths
module LibPackageManager.DevCollabResolution

open Prelude
open LibPackageManager.DevCollab
open LibPackageManager.DevCollabConflicts

type ResolutionStrategy =
  | AlwaysKeepLocal        // Prefer local changes
  | AlwaysKeepRemote       // Prefer remote changes  
  | PromptUser             // Ask user to decide
  | ThreeWayMerge          // Attempt intelligent merge
  | CreateBranch           // Create alternative branch
  | RenameAndKeepBoth      // Rename one to avoid collision

type ResolutionPath =
  | AutoResolution of strategy: ResolutionStrategy * confidence: float
  | InteractiveResolution of options: List<ResolutionStrategy>
  | ManualResolution of instructions: string * examples: List<string>

type ResolutionOutcome =
  | Resolved of newOps: List<PackageOp> * strategy: ResolutionStrategy
  | Deferred of reason: string * suggestedActions: List<string>
  | Failed of error: string * fallbackOptions: List<ResolutionStrategy>

type ConflictResolutionPlan = {
  conflictId: uuid
  originalConflict: Conflict
  recommendedPath: ResolutionPath
  alternativePaths: List<ResolutionPath>
  estimatedComplexity: Simple | Medium | Complex
  userGuidance: string
}

/// Determine the best resolution path for a conflict
let planResolution (conflict: Conflict) (userPreferences: ResolutionStrategy) : ConflictResolutionPlan =
  match conflict.type_ with
  
  | SameFunctionDifferentImpl(fnId, patch1, patch2) ->
    let recommendedPath = 
      match userPreferences with
      | AlwaysKeepLocal -> AutoResolution(AlwaysKeepLocal, 0.9)
      | AlwaysKeepRemote -> AutoResolution(AlwaysKeepRemote, 0.9)
      | PromptUser -> InteractiveResolution([KeepLocal; KeepRemote; ThreeWayMerge])
      | ThreeWayMerge -> AutoResolution(ThreeWayMerge, 0.7)
      | _ -> InteractiveResolution([KeepLocal; KeepRemote; ThreeWayMerge])
    
    let alternatives = [
      AutoResolution(KeepLocal, 0.8)
      AutoResolution(KeepRemote, 0.8)
      InteractiveResolution([ThreeWayMerge; CreateBranch])
      ManualResolution("Review both implementations and choose", 
                      ["Compare function signatures", "Test both implementations", "Consider performance"])
    ]
    
    { conflictId = conflict.id
      originalConflict = conflict
      recommendedPath = recommendedPath
      alternativePaths = alternatives
      estimatedComplexity = Medium
      userGuidance = $"Function {fnId} was modified differently in both patches. Consider the intent of each change." }
  
  | NameCollision(name, patch1, patch2) ->
    let recommendedPath = AutoResolution(RenameAndKeepBoth, 0.9)
    
    let alternatives = [
      InteractiveResolution([RenameAndKeepBoth; KeepLocal; KeepRemote])
      ManualResolution("Manually rename one of the entities", 
                      [$"Rename to {name}_v1 and {name}_v2", $"Use more descriptive names"])
    ]
    
    { conflictId = conflict.id
      originalConflict = conflict
      recommendedPath = recommendedPath
      alternativePaths = alternatives
      estimatedComplexity = Simple
      userGuidance = $"Both patches create '{name}'. Usually safe to rename one." }
  
  | DeletedDependency(deletedId, dependentId) ->
    let recommendedPath = ManualResolution("Review dependency deletion impact", 
                                          ["Check if dependent still needs this", "Update dependent to use alternative"])
    
    let alternatives = [
      AutoResolution(KeepLocal, 0.3)  // Low confidence - risky
      InteractiveResolution([KeepLocal; KeepRemote])
    ]
    
    { conflictId = conflict.id
      originalConflict = conflict
      recommendedPath = recommendedPath
      alternativePaths = alternatives
      estimatedComplexity = Complex
      userGuidance = $"Entity {deletedId} was deleted but {dependentId} depends on it. This requires careful review." }
  
  | TypeIncompatibility(typeId, patch1, patch2) ->
    let recommendedPath = ManualResolution("Type changes require expert review", 
                                          ["Check backward compatibility", "Review type usage", "Consider migration path"])
    
    let alternatives = [
      InteractiveResolution([CreateBranch; KeepLocal; KeepRemote])
      AutoResolution(KeepLocal, 0.4)  // Low confidence
    ]
    
    { conflictId = conflict.id
      originalConflict = conflict
      recommendedPath = recommendedPath
      alternativePaths = alternatives
      estimatedComplexity = Complex
      userGuidance = $"Type {typeId} has incompatible changes. This may break existing code." }

/// Execute a resolution strategy
let executeResolution (conflict: Conflict) (strategy: ResolutionStrategy) (localPatch: Patch) (remotePatch: Patch) : ResolutionOutcome =
  match strategy with
  
  | AlwaysKeepLocal ->
    Resolved(localPatch.ops, AlwaysKeepLocal)
  
  | AlwaysKeepRemote ->
    Resolved(remotePatch.ops, AlwaysKeepRemote)
  
  | RenameAndKeepBoth ->
    match conflict.type_ with
    | NameCollision(name, _, _) ->
      // Create new ops with renamed entities
      let renameOp op =
        match op with
        | AddFunction(id, _, impl, sig_) ->
          // Would need to modify the name in the function name
          AddFunction(id, id, impl, sig_) // Placeholder - real implementation would rename
        | AddType(id, _, def_) ->
          AddType(id, id, def_) // Placeholder
        | AddValue(id, _, value) ->
          AddValue(id, id, value) // Placeholder
        | other -> other
      
      let localOpsRenamed = localPatch.ops |> List.map renameOp
      let remoteOpsRenamed = remotePatch.ops |> List.map renameOp
      let mergedOps = List.append localOpsRenamed remoteOpsRenamed
      
      Resolved(mergedOps, RenameAndKeepBoth)
    | _ ->
      Failed("RenameAndKeepBoth only applies to name collisions", [KeepLocal; KeepRemote])
  
  | ThreeWayMerge ->
    // Attempt intelligent merge (simplified version)
    match conflict.type_ with
    | SameFunctionDifferentImpl(fnId, _, _) ->
      // In a real implementation, this would:
      // 1. Find the common ancestor
      // 2. Compute three-way diff
      // 3. Attempt to merge non-conflicting changes
      // 4. Flag remaining conflicts for manual resolution
      Deferred("Three-way merge not yet implemented", 
              ["Use manual merge tools", "Keep one version and manually apply changes from other"])
    | _ ->
      Failed("Three-way merge not applicable to this conflict type", [KeepLocal; KeepRemote])
  
  | CreateBranch ->
    Deferred("Branch creation not yet implemented", 
            ["Create separate namespace for conflicting changes", "Merge branches later"])
  
  | PromptUser ->
    Deferred("User prompt required", 
            ["Display conflict details to user", "Await user selection"])

/// Generate user-friendly resolution suggestions
let getResolutionSuggestions (conflicts: List<Conflict>) : List<string> =
  let suggestions = []
  
  let suggestions = 
    if conflicts |> List.exists (fun c -> 
      match c.type_ with 
      | SameFunctionDifferentImpl _ -> true 
      | _ -> false) then
      "Consider using 'diff' tools to compare function implementations" :: suggestions
    else suggestions
  
  let suggestions =
    if conflicts |> List.exists (fun c -> 
      match c.type_ with 
      | NameCollision _ -> true 
      | _ -> false) then
      "Use descriptive names to avoid future collisions" :: suggestions
    else suggestions
  
  let suggestions =
    if conflicts |> List.exists (fun c -> c.severity = High) then
      "High-severity conflicts detected - careful review recommended" :: suggestions
    else suggestions
  
  let suggestions =
    if conflicts |> List.length > 5 then
      "Many conflicts detected - consider breaking changes into smaller patches" :: suggestions
    else suggestions
  
  if List.isEmpty suggestions then
    ["All conflicts appear manageable"]
  else suggestions

/// Create a comprehensive resolution plan for multiple conflicts
let createResolutionPlan (conflicts: List<Conflict>) (userPreferences: ResolutionStrategy) : List<ConflictResolutionPlan> =
  conflicts
  |> List.map (fun conflict -> planResolution conflict userPreferences)
  |> List.sortBy (fun plan -> 
    match plan.estimatedComplexity with
    | Simple -> 1
    | Medium -> 2  
    | Complex -> 3)

/// Format resolution plan for CLI display
let formatResolutionPlan (plan: ConflictResolutionPlan) : string =
  let complexityIcon = match plan.estimatedComplexity with
                       | Simple -> "🟢"
                       | Medium -> "🟡"
                       | Complex -> "🔴"
  
  let pathDescription = match plan.recommendedPath with
                        | AutoResolution(strategy, confidence) -> 
                          $"Auto: {strategy} ({confidence:P0} confidence)"
                        | InteractiveResolution(options) -> 
                          $"Interactive: {options.Length} options available"
                        | ManualResolution(instructions, _) -> 
                          $"Manual: {instructions}"
  
  $"{complexityIcon} Conflict {plan.conflictId}\n" +
  $"  Issue: {plan.originalConflict.description}\n" +
  $"  Recommended: {pathDescription}\n" +
  $"  Guidance: {plan.userGuidance}\n" +
  $"  Alternatives: {plan.alternativePaths.Length} other options"

/// Interactive conflict resolution workflow
let interactiveResolution (conflicts: List<Conflict>) : List<ResolutionOutcome> =
  // This would be called from the CLI to present options to the user
  // For now, return deferred outcomes that indicate user interaction needed
  conflicts
  |> List.map (fun conflict ->
    Deferred($"User input required for conflict {conflict.id}", 
            ["Review conflict details", "Choose resolution strategy", "Apply resolution"]))

/// Batch resolution for simple conflicts
let batchResolveSimple (conflicts: List<Conflict>) (strategy: ResolutionStrategy) : List<ResolutionOutcome> =
  conflicts
  |> List.filter (fun c -> c.canAutoResolve && c.severity <> High)
  |> List.map (fun conflict ->
    // For simple conflicts, we can often auto-resolve
    match conflict.type_ with
    | NameCollision _ when strategy = RenameAndKeepBoth ->
      Resolved([], RenameAndKeepBoth) // Simplified - would contain actual rename ops
    | _ ->
      Deferred("Requires individual attention", ["Review manually"]))

/// Generate conflict resolution report
let generateResolutionReport (plans: List<ConflictResolutionPlan>) (outcomes: List<ResolutionOutcome>) : string =
  let totalConflicts = plans.Length
  let autoResolved = outcomes |> List.filter (function | Resolved _ -> true | _ -> false) |> List.length
  let deferred = outcomes |> List.filter (function | Deferred _ -> true | _ -> false) |> List.length
  let failed = outcomes |> List.filter (function | Failed _ -> true | _ -> false) |> List.length
  
  $"Conflict Resolution Report\n" +
  $"========================\n" +
  $"Total conflicts: {totalConflicts}\n" +
  $"Auto-resolved: {autoResolved}\n" +
  $"Require attention: {deferred}\n" +
  $"Failed resolution: {failed}\n\n" +
  $"Recommendations:\n" +
  String.concat "\n" (getResolutionSuggestions (plans |> List.map (fun p -> p.originalConflict)))