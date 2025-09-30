## Sessions

### Type Definitions
```fsharp

type WorkContext = {
  currentLocation: PackageLocation  // Where in package tree
  openEntities: List<EntityId>      // What's being edited
  cursorPosition: CursorPos option  // For resuming editing
}
```

### Session Commands
```bash
dark session new --intent "Add List functions"
dark session continue my-list-work
dark session list
dark session suspend
dark session share ocean  # Share read-only view
```

## 4. Instances & Sync

### Type Definitions
```fsharp
type InstanceType =
  | CLI of version: string
  | Server
  | Browser
  | VSCode

type Instance = {
  id: uuid
  userId: UserId
  type: InstanceType
  lastSeen: Timestamp
  localPatches: Set<PatchId>   // Patches stored locally
  syncState: SyncState
}

type SyncState =
  | Synced
  | Behind of count: int        // How many patches behind
  | Ahead of count: int         // How many local patches
  | Diverged                    // Has both local and remote changes

type SyncOp =
  | Push of patches: List<Patch>
  | Pull of since: Timestamp
  | Merge of localPatch: PatchId * remotePatch: PatchId
```

## 5. Complete Flow: "Add a Function and Share It"

### Developer A (Stachu) adds a function:
```bash
# 1. Start a new session
$ dark session new --intent "Add List.filterMap"
Created session: helpful-owl-42

# 2. Navigate to target module  
$ dark nav Darklang.Stdlib.List

# 3. Create the function (opens editor)
$ dark add function filterMap
# ... edits in VS Code with LSP ...

# 4. Test the function
$ dark eval "List.filterMap [1;2;3] (fn x -> if x > 1 then Some(x*2) else None)"
[4; 6]

# 5. Mark patch ready
$ dark patch ready
Patch validated and marked ready

# 6. Push to server
$ dark sync push
Pushed 1 patch to server
```

### Developer B (Ocean) gets the function:
```bash
# 1. Check for updates
$ dark sync status
3 new patches available

# 2. Pull changes
$ dark sync pull
Pulled 3 patches from server

# 3. View what changed
$ dark patch list --new
- helpful-owl-42: "Add List.filterMap" by stachu (2 mins ago)
- ...

# 4. Apply patches
$ dark patch apply helpful-owl-42
Applied: Added Darklang.Stdlib.List.filterMap

# 5. Use the new function
$ dark eval "List.filterMap [1;2;3] (fn x -> if x > 1 then Some(x*2) else None)"
[4; 6]
```
