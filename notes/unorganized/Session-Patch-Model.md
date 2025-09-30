# Session-Patch Model Clarification


### Session Definition
```fsharp
type Session =
  { id: SessionId
    name: string
    description: string
    createdAt: DateTime
    createdBy: UserId
    closedAt: DateTime option
    patches: List<PatchId>                // Multiple patches in session
    activeEditors: List<EditorContext>    // Multiple open editors
    collaborators: List<UserId>
    organization: OrganizationId option }

type SessionId = SessionId of string

type EditorContext =
  { editorId: string
    targetName: FullyQualifiedName
    patchId: PatchId option              // Which patch this editor contributes to
    isModified: bool
    lastSavedAt: DateTime option }
```

### Patch Definition
```fsharp
type Patch =
  { id: PatchId
    sessionId: SessionId                 // Belongs to a session
    name: string
    description: string
    operations: List<Op>
    status: PatchStatus
    createdAt: DateTime
    createdBy: UserId
    conflicts: List<ConflictId>
    dependencies: List<PatchId>          // Other patches this depends on
    readyToMerge: bool }

type PatchStatus =
  | Draft                              // Work in progress
  | Ready                              // Ready for review/merge
  | Conflicts                          // Has unresolved conflicts
  | Merged                             // Successfully merged
  | Abandoned                          // Discarded
```



## Session Workflow Examples

### 1. **Starting a New Session**
```fsharp
// User starts a development session
let startSession (userId: UserId) (name: string) (description: string) : SessionId =
  let sessionId = generateSessionId()

  let session = {
    id = sessionId
    name = name
    description = description
    createdAt = DateTime.Now
    createdBy = userId
    closedAt = None
    patches = []                         // Starts empty
    activeEditors = []                   // No editors yet
    collaborators = [userId]
    organization = getUserOrganization userId
  }

  saveSession session
  sessionId

// Example usage
let sessionId = startSession alice "user-improvements" "Enhancing user validation and profile features"
```


### 3. **Managing Patches Within Session**
```fsharp
// User can see all patches in current session
let sessionPatches = getPatchesInSession sessionId
// Returns: [validation-enhancements (3 ops), performance-optimizations (1 op)]

// User can merge individual patches
let mergeResult = mergePatch patch1.id
// Only patch1 is merged, patch2 continues in session

// User can create dependencies between patches
let addDependency = addPatchDependency patch2.id patch1.id
// patch2 cannot be merged until patch1 is merged

// User can abandon a patch
let abandonPatch = setPatchStatus patch2.id Abandoned
// patch2 is marked abandoned but session continues
```



## Editor Assignment to Patches

### **Dynamic Patch Assignment**
```fsharp
// When user edits a function, determine which patch it should belong to
let assignEditorToPatch (sessionId: SessionId) (editorId: string) (targetName: FullyQualifiedName) : PatchId option =
  let sessionPatches = getPatchesInSession sessionId

  // Check if any existing patch already modifies this item
  let existingPatch =
    sessionPatches
    |> List.tryFind (fun patch ->
        patch.operations
        |> List.exists (fun op -> getAffectedNames op |> List.contains targetName))

  match existingPatch with
  | Some patch ->
      // Add to existing patch
      Some patch.id
  | None ->
      // User choice: create new patch or add to existing one
      promptUserForPatchAssignment sessionId targetName

let promptUserForPatchAssignment (sessionId: SessionId) (targetName: FullyQualifiedName) : PatchId option =
  let sessionPatches = getPatchesInSession sessionId

  let options = [
    yield! sessionPatches |> List.map (fun p -> CreatePatchOption.AddToExisting(p.id, p.name))
    yield CreatePatchOption.CreateNew("Create new patch")
  ]

  // Show user choice dialog
  showPatchAssignmentDialog options
```


### **Multi-Editor Session UI**
```
🔄 Session: user-improvements

📝 Active Editors:
├── Editor 1: MyApp.User.validate (validation-enhancements patch)
├── Editor 2: MyApp.Database.User.findByEmail (performance-optimizations patch)
└── Editor 3: MyApp.User.Profile.render (unassigned)

📦 Patches in Session:
├── ✅ validation-enhancements (3 ops, ready to merge)
├── 📝 performance-optimizations (1 op, draft)
└── 🆕 Create new patch for Editor 3...

🔧 Actions:
├── Switch to Editor 1, 2, or 3
├── Merge ready patches individually
├── Create new patch for unassigned work
├── Close session (merges all ready patches)
└── Abandon session (discards all unmerged patches)
```

## Session State Management

### **Session Operations**
```fsharp
module SessionManager =

  let getCurrentSession (userId: UserId) : SessionId option =
    // User can only have one active session at a time (in VS Code, that is)
    getUserActiveSession userId

  let createPatchInSession (sessionId: SessionId) (name: string) (description: string) : PatchId =
    let patchId = generatePatchId()

    let patch = {
      id = patchId
      sessionId = sessionId
      name = name
      description = description
      operations = []
      status = Draft
      createdAt = DateTime.Now
      createdBy = getSessionOwner sessionId
      conflicts = []
      dependencies = []
      readyToMerge = false
    }

    savePatch patch
    addPatchToSession sessionId patchId
    patchId

  let addOperationToPatch (patchId: PatchId) (operation: Op) : unit =
    let patch = getPatch patchId
    let updatedPatch = { patch with operations = operation :: patch.operations }
    savePatch updatedPatch

    // Check if patch is now ready to merge
    checkPatchReadiness patchId

  let mergePatch (patchId: PatchId) : Result<unit, MergeError> =
    let patch = getPatch patchId

    // Validate patch can be merged
    match validatePatchForMerge patch with
    | Ok() ->
        // Apply all operations in patch
        applyPatchOperations patch.operations

        // Update patch status
        let mergedPatch = { patch with status = Merged; mergedAt = Some DateTime.Now }
        savePatch mergedPatch

        Ok()
    | Error(e) -> Error(e)

  let closeSession (sessionId: SessionId) : SessionCloseResult =
    let session = getSession sessionId
    let sessionPatches = getPatchesInSession sessionId

    // Try to merge all ready patches
    let mergeResults =
      sessionPatches
      |> List.filter (fun p -> p.readyToMerge)
      |> List.map mergePatch

    // Close session
    let closedSession = { session with closedAt = Some DateTime.Now }
    saveSession closedSession

    {
      sessionId = sessionId
      patchesMerged = mergeResults |> List.choose (function Ok() -> Some() | Error(_) -> None) |> List.length
      patchesFailed = mergeResults |> List.choose (function Error(e) -> Some(e) | Ok() -> None)
      patchesAbandoned = sessionPatches |> List.filter (fun p -> not p.readyToMerge) |> List.length
    }
```

## Key Differences from Patch-Based Model

### **Old Model (Patch-Based)**
```
❌ User works on one patch at a time
❌ Switch between patches = switch contexts entirely
❌ URLs: dark://edit/current-patch/Name.Space.item
❌ Limited ability to work on related changes
❌ Difficult to coordinate multiple related improvements
```

### **New Model (Session-Based)**
```
✅ User works in one session with multiple patches
✅ Switch between patches = switch within same context
✅ URLs: dark://edit/session-id/editor-id
✅ Natural workflow for related changes
✅ Easy coordination of multiple improvements
✅ Patches can depend on each other within session
✅ Individual patches can be merged when ready
✅ Session provides natural collaboration boundary
```

## Collaboration in Sessions

### **Multi-User Sessions**
```fsharp
// Alice invites Bob to her session
let inviteToSession (sessionId: SessionId) (invitedUserId: UserId) (role: CollaborationRole) : unit =
  addSessionCollaborator sessionId invitedUserId role

  // Bob can now see Alice's patches and contribute
  // Bob can create his own patches in the same session
  // Alice and Bob coordinate patch dependencies

// Example collaborative workflow:
// 1. Alice creates session "user-improvements"
// 2. Alice creates patch "validation-enhancements"
// 3. Alice invites Bob to session
// 4. Bob creates patch "validation-tests" that depends on Alice's patch
// 5. Alice merges her patch when ready
// 6. Bob's patch becomes mergeable after Alice's is merged
```

This session-patch model enables natural development workflows where developers can work on multiple related changes simultaneously while maintaining clear organization and merge control.