# Core Concepts for Darklang Developer Collaboration

## 1. Operations (Ops)

### Type Definitions
```fsharp
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

### Key Design Decisions
- Each op has a unique ID for tracking
- Ops are immutable once created
- Version numbers auto-increment on updates
- Delete only allowed for unpublished entities (safety)

## 2. Patches

### Type Definitions  
```fsharp
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

type ValidationError = {
  op: OpId
  message: string
  severity: Error | Warning
}
```

### Validation Requirements
1. **Type Safety**: Functions must type-check against current types
2. **Name Uniqueness**: No duplicate names in same module
3. **Dependency Order**: Ops must be in dependency order
4. **Version Consistency**: Can't update version N+2 without N+1
5. **No Breaking Changes**: Can't delete/change types with dependents

## 3. Sessions

### Type Definitions
```fsharp
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

## 6. Database Schema

### SQLite Tables
```sql
-- Local instance state
CREATE TABLE user (
  id TEXT PRIMARY KEY,
  username TEXT NOT NULL,
  auth_token TEXT
);

CREATE TABLE sessions (
  id TEXT PRIMARY KEY,
  name TEXT,
  intent TEXT,
  state TEXT,
  current_patch_id TEXT,
  context_json TEXT,
  created_at TIMESTAMP,
  last_active_at TIMESTAMP
);

CREATE TABLE patches (
  id TEXT PRIMARY KEY,
  author_id TEXT,
  intent TEXT,
  ops_json TEXT,        -- Serialized ops list
  dependencies_json TEXT,
  status TEXT,
  todos_json TEXT,
  created_at TIMESTAMP,
  updated_at TIMESTAMP
);

CREATE TABLE sync_log (
  id INTEGER PRIMARY KEY,
  direction TEXT,       -- 'push' or 'pull'
  patch_ids TEXT,       -- JSON array
  timestamp TIMESTAMP
);
```

## Conflict Resolution

### Scenarios
1. **Same function, different implementations**: Last-write-wins with warning
2. **Conflicting type changes**: Reject both, require manual resolution  
3. **Deleted dependency**: Reject dependent patch
4. **Moved entity**: Track moves, update references

### Merge Strategy
- Automatic for non-conflicting ops
- Manual review for conflicts
- Always preserve both versions in history

## Open Questions for Discussion

1. **Should patches be mutable or immutable?** Currently mutable (can add ops)
2. **How granular should ops be?** One op per keystroke vs logical changes?
3. **Should we version the op format itself?** For future compatibility
4. **How do we handle large binary data?** (images, datasets)
5. **Branch model or patch-based only?** Currently patch-based

## Next Steps
This minimal design enables:
- Creating and sharing individual functions
- Basic conflict detection
- Session management for context
- Simple CLI-based workflow

Ready to implement for tomorrow's demo.