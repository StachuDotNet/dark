/// Developer collaboration types for patches, sessions, and sync
module LibPackageManager.DevCollab

open Prelude
open LibExecution.ProgramTypes

type UserId = string // "stachu" or "ocean" for now

type OpId = System.Guid

/// Operations that can be performed on packages
type PackageOp =
  /// Add a new function to a package
  | AddFunction of 
      id: uuid * 
      name: FQFnName.Package * 
      impl: Expr * 
      signature: FnDeclaration.FnDeclaration
  
  /// Update an existing function
  | UpdateFunction of 
      id: uuid * 
      impl: Expr * 
      version: int
  
  /// Add a new type to a package  
  | AddType of
      id: uuid *
      name: FQTypeName.Package *
      definition: TypeDeclaration.TypeDeclaration
  
  /// Update an existing type
  | UpdateType of
      id: uuid *
      definition: TypeDeclaration.TypeDeclaration *
      version: int
  
  /// Add a new value to a package
  | AddValue of
      id: uuid *
      name: FQValueName.Package *
      value: Dval
  
  /// Move an entity to a different module path
  | MoveEntity of
      fromPath: List<string> *
      toPath: List<string> *
      entityId: uuid
  
  /// Mark an entity as deprecated (soft delete)
  | DeprecateEntity of
      id: uuid *
      replacementId: uuid option *
      reason: string

type PatchId = System.Guid

type PatchStatus =
  | Draft      // Being worked on
  | Ready      // Ready for review/merge  
  | Applied    // Successfully merged
  | Rejected   // Failed validation

/// A patch represents a logical set of changes
type Patch = {
  id: PatchId
  author: UserId
  intent: string                    // Human description of what this patch does
  ops: List<PackageOp>             // The actual changes
  dependencies: Set<PatchId>        // Other patches this depends on
  createdAt: System.DateTime
  updatedAt: System.DateTime
  status: PatchStatus
  todos: List<string>              // Outstanding work items
  validationErrors: List<string>   // Issues that need to be resolved
}

type SessionId = System.Guid

type SessionState =
  | Active      // Currently being worked on
  | Suspended   // Paused but can be resumed
  | Completed   // Work finished

/// A session represents a work context that persists across CLI restarts
type Session = {
  id: SessionId
  name: string
  intent: string                   // What you're working on
  owner: UserId
  patches: List<PatchId>          // Patches created in this session
  currentPatch: PatchId option    // Currently active patch
  startedAt: System.DateTime
  lastActiveAt: System.DateTime
  state: SessionState
  context: WorkContext            // Current location, cursor position, etc
}

/// Work context for resuming where you left off
and WorkContext = {
  currentLocation: string option   // Package path like "Darklang.Stdlib.List"
  openFiles: List<string>         // Files being edited
  notes: string                   // Free-form notes
}

type InstanceId = System.Guid

type InstanceType =
  | CLI of version: string
  | Server
  | Browser
  | VSCode

/// Represents a Darklang instance (CLI, server, etc)
type Instance = {
  id: InstanceId
  userId: UserId
  type_: InstanceType
  lastSeen: System.DateTime
  localPatches: Set<PatchId>      // Patches stored locally
}

type SyncDirection =
  | Push                          // Send local patches to server
  | Pull                          // Get remote patches from server

type SyncResult =
  | Success of patchCount: int
  | Conflicts of conflicts: List<PatchId * string>
  | NetworkError of message: string
  | ValidationError of errors: List<string>

/// Validation result for a patch
type ValidationResult =
  | Valid
  | Invalid of errors: List<string>

module Patch =
  let create (author: UserId) (intent: string) : Patch =
    { id = System.Guid.NewGuid()
      author = author
      intent = intent
      ops = []
      dependencies = Set.empty
      createdAt = System.DateTime.UtcNow
      updatedAt = System.DateTime.UtcNow
      status = Draft
      todos = []
      validationErrors = [] }
  
  let addOp (patch: Patch) (op: PackageOp) : Patch =
    { patch with 
        ops = patch.ops @ [op]
        updatedAt = System.DateTime.UtcNow }
  
  let markReady (patch: Patch) : Patch =
    { patch with 
        status = Ready
        updatedAt = System.DateTime.UtcNow }

module Session =
  let create (owner: UserId) (name: string) (intent: string) : Session =
    { id = System.Guid.NewGuid()
      name = name
      intent = intent
      owner = owner
      patches = []
      currentPatch = None
      startedAt = System.DateTime.UtcNow
      lastActiveAt = System.DateTime.UtcNow
      state = Active
      context = { currentLocation = None; openFiles = []; notes = "" } }
  
  let addPatch (session: Session) (patchId: PatchId) : Session =
    { session with 
        patches = session.patches @ [patchId]
        currentPatch = Some patchId
        lastActiveAt = System.DateTime.UtcNow }

/// Simple validation - check for obvious issues
module Validation =
  let validatePatch (patch: Patch) : ValidationResult =
    let errors = []
    
    // Check that patch has some ops
    let errors = 
      if List.isEmpty patch.ops then
        "Patch must contain at least one operation" :: errors
      else errors
    
    // Check that intent is not empty
    let errors =
      if System.String.IsNullOrWhiteSpace patch.intent then
        "Patch must have a non-empty intent description" :: errors
      else errors
    
    // TODO: Add more validation:
    // - Type checking
    // - Name conflicts
    // - Dependency validation
    
    match errors with
    | [] -> Valid
    | errs -> Invalid errs