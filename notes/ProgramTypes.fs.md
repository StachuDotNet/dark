# ProgramTypes.fs additions and changes

## PackageLocation
```fsharp
module PackageLocation =
  type T = {
    owner: string
    modules: List<string>
    name: string
  }

  let create (owner: string) (modules: List<string>) (name: string) : T =
    { owner = owner; modules = modules; name = name }

  let toString (location: T) : string =
    $"{location.owner}.{String.concat "." location.modules}.{location.name}"

  let parse (locationString: string) : T =
    let parts = locationString.Split('.')
    match List.ofArray parts with
    | owner :: nameAndModules when nameAndModules.Length > 0 ->
        let name = List.last nameAndModules
        let modules = nameAndModules |> List.take (nameAndModules.Length - 1)
        { owner = owner; modules = modules; name = name }
    | _ -> failwith $"Invalid package location: {locationString}"
```




```fsharp
type PackageOp =
  | AddFunction of id * name * impl * signature
  | AddType of id * name * definition
  | AddValue of id * name * value
  | MoveEntity of fromPath * toPath * entityId
  | DeprecateEntity of id * replacementId * reason

type Patch = {
  id: PatchId
  author: UserId
  intent: string                    // Human description!
  ops: List<PackageOp>
  dependencies: Set<PatchId>
  createdAt: DateTime
  status: PatchStatus              // Draft/Ready/Applied/Rejected
  todos: List<string>
  validationErrors: List<string>
}
```




## Op
```fsharp
/// Atomic operations that can be tracked, validated, and synced
type Op =
  // Content Operations - create new immutable content
  | AddFunctionContent of hash: string * content: PackageFn.PackageFn
  | AddTypeContent of hash: string * content: PackageType.PackageType
  | AddValueContent of hash: string * content: PackageValue.PackageValue

  // Name Operations - manage mutable name pointers
  | CreateName of location: PackageLocation.T * hash: string * visibility: Visibility
  | UpdateNamePointer of location: PackageLocation.T * oldHash: string * newHash: string
  | MoveName of oldLocation: PackageLocation.T * newLocation: PackageLocation.T
  | Deprecate of location: PackageLocation.T * reason: string * replacement: PackageLocation.T option

  // Meta Operations
  | CreatePatch of patchId: uuid * intent: string * ops: List<Op>
  | MergePatch of patchId: uuid * targetInstance: InstanceId


type PackageOp =
  | AddType of name: TypeName * definition: TypeDef * id: uuid
  | AddFunction of name: FnName * implementation: Expr * signature: FnSig * id: uuid
  | AddValue of name: ValueName * value: Dval * id: uuid
  | UpdateFunction of id: uuid * implementation: Expr * version: int
  | UpdateType of id: uuid * definition: TypeDef * version: int
  | DeprecateFunction of id: uuid * replacementId: uuid option
  | MoveEntity of fromPath: ModulePath * toPath: ModulePath * entityId: uuid
  | DeleteEntity of id: uuid  // Only for unpublished entities

type Op =
  | PackageOp of PackageOp
  | SessionOp of SessionOp  // Future: cursor moves, selections, etc
  | MetaOp of MetaOp      // Comments, TODOs, etc

type OpId = uuid
type Timestamp = DateTime

```




## Conflicts
```fsharp
/// Validation results for operations and patches
type ValidationResult = {
  isValid: bool
  conflicts: List<Conflict>
  dependencies: List<PackageLocation.T>
  warnings: List<string>
  suggestions: List<string>
  testsCovered: List<string>
}

/// Types of conflicts that can occur during sync/merge
type Conflict =
  | SameFunctionDifferentImpl of location: PackageLocation.T * hash1: string * hash2: string
  | NameCollision of name: PackageLocation.T * hash1: string * hash2: string
  | DeletedDependency of deleted: PackageLocation.T * dependent: PackageLocation.T
  | TypeIncompatibility of typeLocation: PackageLocation.T * oldHash: string * newHash: string
```

## Patch

```fsharp
  type Patch = {
    id: Uuid
    intent: String
    ops: List<Op>
    status: PatchStatus
    author: String
    createdAt: DateTime
    metadata: PatchMetadata
  }


type PatchStatus =
  | Draft          // Being worked on
  | Ready          // Ready for review/merge
  | Applied        // Successfully merged
  | Rejected       // Failed validation

type Patch = {
  id: uuid
  author: UserId
  intent: string           // "Add List.filterMap function"
  ops: List<Op>
  dependencies: Set<PatchId>  // Other patches this depends on
  createdAt: Timestamp
  updatedAt: Timestamp
  status: PatchStatus
  todos: List<string>      // Outstanding work items
  validationErrors: List<ValidationError>
}

    /// Patch status workflow
    type PatchStatus =
    | Draft       // Work in progress, can be modified
    | Ready       // Complete and ready for review/merge
    | Applied     // Successfully applied to target instance
    | Rejected    // Rejected during validation or review


  let create (intent: String) (author: String) : Patch =
    let patch = {
      id = Uuid.generateV4()
      intent = intent
      ops = []
      status = PatchStatus.Draft
      author = author
      createdAt = DateTime.now()
      metadata = PatchMetadata.empty
    }

    PatchDB.save patch
    SessionManager.setCurrentPatch (Some patch.id)
    patch

  let addOperation (patchId: Uuid) (op: Op) : Result<Unit, String> =
    match PatchDB.get patchId with
    | Some patch when patch.status == PatchStatus.Draft ->
      let updatedPatch = { patch with ops = patch.ops ++ [op] }
      PatchDB.save updatedPatch
      Ok ()
    | Some _ -> Error "Cannot modify non-draft patch"
    | None -> Error "Patch not found"

  let validate (patchId: Uuid) : ValidationResult =
    match PatchDB.get patchId with
    | Some patch ->
      ConflictDetector.analyzeOps patch.ops
    | None -> ValidationResult.notFound

type ValidationError = {
  op: OpId
  message: string
  severity: Error | Warning
}

/// Patch metadata for collaboration
type PatchMetadata = {
  todos: List<string>
  tags: List<string>
  estimatedImpact: ImpactLevel
  testsCovered: List<string>
  views: List<PatchView>
}

/// A logical collection of operations
type Patch = {
  id: uuid
  intent: string                          // Human-readable description
  ops: List<Op>
  parentPatches: List<uuid>               // Patches this depends on
  status: PatchStatus
  author: string
  createdAt: DateTime
  validationResult: ValidationResult option
  metadata: PatchMetadata
}
```


## Session

```fsharp

  type Session = {
    id: Uuid
    name: String
    currentPatch: (Option Uuid)
    workspaceState: WorkspaceState
    lastActivity: DateTime
  }


type SessionState =
  | Active
  | Suspended
  | Completed

type Session = {
  id: uuid
  name: string
  intent: string          // What you're working on
  owner: UserId
  patches: List<PatchId>  // Patches created in this session
  currentPatch: PatchId option
  startedAt: Timestamp
  lastActiveAt: Timestamp
  state: SessionState
  context: WorkContext    // Current location, open files, etc
}

  let create (name: String) : Session =
    let session = {
      id = Uuid.generateV4()
      name = name
      currentPatch = None
      workspaceState = WorkspaceState.empty
      lastActivity = DateTime.now()
    }

    SessionDB.save session
    session

  let switchTo (sessionId: Uuid) : Result<Session, String> =
    match SessionDB.get sessionId with
    | Some session ->
      let updatedSession = { session with lastActivity = DateTime.now() }
      SessionDB.save updatedSession
      SessionDB.setCurrent sessionId
      Ok updatedSession
    | None -> Error "Session not found"

  let getCurrentPatch () : (Option Uuid) =
    match SessionDB.getCurrent () with
    | Some session -> session.currentPatch
    | None -> None

    /// Session identifier
type SessionId = uuid

/// Working context that can be transferred between environments
type Session = {
  id: SessionId
  name: string
  owner: string
  currentPatch: uuid option
  pinnedPatches: List<uuid>
  workspaceState: WorkspaceState
  environmentVars: Map<string, string>
  createdAt: DateTime
  lastActivity: DateTime
  transferable: bool
}
```

## Instance

```fsharp


/// Instance types in the Darklang ecosystem
type InstanceType =
  | LocalCLI        // Individual developer machine
  | HttpServer   // Main Darklang package repository

/// Authentication methods for remote instances
type AuthMethod =
  | None            // No authentication required
  | APIKey of string
  | OAuth of token: string

/// Location where an instance can be found
type InstanceLocation =
  | Local of path: string
  | Remote of url: string * auth: AuthMethod

/// Capabilities that an instance supports
type InstanceCapability =
  | Read            // Can read packages
  | Write           // Can create new packages
  | Sync            // Can sync with other instances
  | Validate        // Can validate patches
  | Merge           // Can merge patches

/// Instance identifier
type InstanceId = uuid

/// Darklang instance definition
type Instance = {
  id: InstanceId
  type_: InstanceType
  location: InstanceLocation
  capabilities: Set<InstanceCapability>
  lastSyncAt: DateTime option
  syncMode: SyncMode
}
```


## Sync
```fsharp


/// Sync operation modes
type SyncMode =
  | Manual          // User initiates all sync operations
  | Automatic       // Background sync with conflict detection
  | Hybrid          // Auto-pull, manual-push

/// Sync protocol request types
type SyncRequest =
  | PullPatches of since: DateTime option
  | PushPatches of patches: List<Patch>
  | CheckConflicts of patches: List<Patch>
  | RequestMerge of patchId: uuid * strategy: MergeStrategy

/// Sync protocol response types
type SyncResponse =
  | PatchesList of patches: List<Patch>
  | ConflictsDetected of conflicts: List<Conflict>
  | MergeResult of result: MergeResult
  | SyncError of error: string

/// Merge strategies for conflict resolution
type MergeStrategy =
  | Automatic       // Apply if no conflicts
  | Manual          // Require human review
  | Force           // Apply regardless of conflicts (destructive)

/// Result of a merge operation
type MergeResult = {
  success: bool
  appliedOps: List<Op>
  remainingConflicts: List<Conflict>
  rollbackInfo: RollbackInfo option
}

/// Information needed to rollback a merge
type RollbackInfo = {
  originalState: Map<PackageLocation.T, string>  // location -> hash
  timestamp: DateTime
}
```




---


## Matter

We likely need some _projection_ of all this package stuff in-memory.
So far we've been querying directly against the DB, and maybe that's fine going forward, but it may be better (or useful for _other_ scenarios) to have some in-mem modeling of things.

Like, when you switch a session, we either have to refresh the projected state, or kill-and-fill a bunch of stuff in the DB used for package search and other stuff.
Actually, yeah, maybe we just need to aggressively update things in the DB, quickly, as Ops are applied, etc.

Patch.validateOp doesn't do anything, but Patch.applyOp does everything it needs to make sure the results have been applied.
During 'initial load' or some refresh (like when switching sessions), we likely need to purge a bunch of stuff, then call Patch.applyOp for each of the ops relevant.

Can/should we break down the _existing_ package matter into a series of Patches that folks can choose to apply or not? Hmm. Let's design the session/patch tables asap so we can help guide the answer to that q.

May be useful to have .applyOpUnsafe for stuff that's been previously verified.

Still not sure whether where _exactly_ 'content' lives - in package tables or otherwise.



## Misc

```fsharp

/// Different ways to view patch content
type PatchView =
  | SourceDiff  // Traditional source code diff
  | OpList      // List of operations
  | ImpactTree  // Tree showing affected packages
  | Timeline    // Chronological view of changes


/// View state for a specific package location
type ViewState = {
  cursorPosition: int * int
  scrollPosition: int
  foldedRegions: List<int * int>
  representation: ViewRepresentation
}

/// Different ways to represent code
type ViewRepresentation =
  | Source                  // Normal Darklang source
  | AST                     // Abstract syntax tree view
  | RuntimeInstructions     // Compiled runtime instructions
  | PrettyPrinted          // Formatted with custom styling

/// Workspace state within a session
type WorkspaceState = {
  openFiles: List<PackageLocation.T>
  currentFile: PackageLocation.T option
  viewState: Map<PackageLocation.T, ViewState>
  breakpoints: List<PackageLocation.T * int>
  bookmarks: List<PackageLocation.T * string>
}


```









```fsharp
type ConflictType =
  | DirectNameConflict of name: FullyQualifiedName * patches: List<PatchId>
  | MutableValueConflict of name: FullyQualifiedName * patches: List<PatchId>
  | MoveEditConflict of originalName: FullyQualifiedName * patches: List<PatchId>
  | DependencyBreakingConflict of dependency: FullyQualifiedName * dependents: List<FullyQualifiedName>
  | DeprecationConflict of itemHash: PackageHash * conflictingOps: List<Op>
  | TypeIncompatibilityConflict of name: FullyQualifiedName * expectedType: TypeReference * actualType: TypeReference

type ConflictDetails =
  { conflictType: ConflictType
    basePatch: PatchId option  // Common ancestor
    conflictingPatches: List<PatchId>
    affectedNames: List<FullyQualifiedName>
    description: string
    autoResolvable: bool }
```





## Essential Operations (Minimal Ops Design)

### Three Core Ops
```darklang
type Op =
  | AddContent of hash: String * content: PackageItem
  | RepointName of location: PackageLocation * newHash: String
  | DeprecateName of location: PackageLocation * reason: String
```

**Content-Addressable Storage**: Content is immutable (hash-addressed), names are mutable pointers.