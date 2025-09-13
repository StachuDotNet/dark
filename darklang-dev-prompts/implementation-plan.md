# Implementation Plan

## Phase 1: Minimal Viable (Tomorrow's Meeting)

### 1.1 Core Types (2 hours)

**New file: backend/src/LibPackageManager/DevCollab.fs**
```fsharp
module LibPackageManager.DevCollab

type UserId = string  // "stachu" or "ocean" for now

type OpId = System.Guid

type PackageOp =
  | AddFunction of 
      id: uuid * 
      name: PT.FQFnName.Package * 
      impl: PT.Expr * 
      signature: PT.FnDeclaration.FnDeclaration
  | UpdateFunction of 
      id: uuid * 
      impl: PT.Expr * 
      version: int

type PatchId = System.Guid

type Patch = {
  id: PatchId
  author: UserId
  intent: string
  ops: List<PackageOp>
  createdAt: System.DateTime
  status: Draft | Ready | Applied
}

type Session = {
  id: System.Guid
  name: string
  userId: UserId
  currentPatch: PatchId option
  createdAt: System.DateTime
}
```

### 1.2 CLI Commands (3 hours)

**Extend packages/darklang/cli/core.dark**
```darklang
// Add new commands to Registry
("patch", "Manage code patches", ["p"], Patch.execute, Patch.help)
("session", "Work sessions", ["s"], Session.execute, Session.help)
("sync", "Sync with server", [], Sync.execute, Sync.help)
("auth", "Authentication", [], Auth.execute, Auth.help)
```

**New file: packages/darklang/cli/patch.dark**
```darklang
module Darklang.Cli.Patch

let execute (state: AppState) (args: List<String>) : AppState =
  match args with
  | ["create"] ->
    let patchId = Stdlib.Uuid.generate()
    Stdlib.printLine $"Created patch: {patchId}"
    state
  | ["list"] ->
    // Query local DB for patches
    let patches = LocalDB.getPatches()
    patches |> Stdlib.List.iter (fun p ->
      Stdlib.printLine $"{p.id}: {p.intent} by {p.author}")
    state
  | ["apply"; patchId] ->
    // Apply patch to local state
    LocalDB.applyPatch patchId
    Stdlib.printLine $"Applied patch: {patchId}"
    state
  | _ ->
    Stdlib.printLine "Usage: patch [create|list|apply]"
    state
```

### 1.3 Local Database (2 hours)

**SQLite schema: ~/.darklang/dev.db**
```sql
CREATE TABLE users (
  id TEXT PRIMARY KEY,
  username TEXT UNIQUE NOT NULL
);

-- Hardcoded initial data
INSERT INTO users VALUES ('1', 'stachu'), ('2', 'ocean');

CREATE TABLE patches (
  id TEXT PRIMARY KEY,
  author_id TEXT REFERENCES users(id),
  intent TEXT,
  ops_json TEXT,  -- Serialized ops
  created_at TIMESTAMP,
  status TEXT CHECK(status IN ('draft', 'ready', 'applied'))
);

CREATE TABLE sessions (
  id TEXT PRIMARY KEY,
  name TEXT,
  user_id TEXT REFERENCES users(id),
  current_patch_id TEXT,
  created_at TIMESTAMP
);

CREATE TABLE sync_state (
  instance_id TEXT PRIMARY KEY,
  last_sync TIMESTAMP,
  pending_patches TEXT  -- JSON array of patch IDs
);
```

### 1.4 Simple Sync Protocol (2 hours)

**HTTP endpoints on dev server**
```
POST /patches/push
  Body: { patches: [...] }
  Response: { success: true, conflicts: [] }

GET /patches/pull?since=<timestamp>
  Response: { patches: [...] }

GET /patches/{id}
  Response: { patch details }
```

**Basic implementation using existing BuiltinCliHost HTTP client**

## Phase 2: Core Mechanics (Sunday Meeting)

### 2.1 Patch Validation

```fsharp
module Validation =
  type ValidationError =
    | NameConflict of name: string
    | TypeCheckFailed of fn: string * error: string
    | MissingDependency of id: uuid
    
  let validatePatch (patch: Patch) : Result<unit, List<ValidationError>> =
    // Check each op for:
    // - Name uniqueness
    // - Type safety
    // - Dependency availability
    uply {
      let! names = checkNameConflicts patch.ops
      let! types = checkTypes patch.ops
      let! deps = checkDependencies patch.ops
      return Ok ()
    }
```

### 2.2 Conflict Detection

```fsharp
type Conflict =
  | SameFunctionDifferentImpl of fnId: uuid * patch1: PatchId * patch2: PatchId
  | DeletedDependency of deleted: uuid * dependent: uuid
  | TypeIncompatibility of typeId: uuid * change1: TypeDef * change2: TypeDef

let detectConflicts (local: Patch) (remote: Patch) : List<Conflict> =
  // Compare ops to find conflicts
  []  // Start simple
```

### 2.3 Session State Persistence

```darklang
// Extend CLI AppState
type AppState =
  { ...existing fields...
    currentSession: Session option
    currentPatch: Patch option
    authenticatedUser: String option }

// Load on startup
let loadSessionState () : AppState =
  let state = initState()
  match LocalDB.getCurrentSession() with
  | Some session ->
    { state with 
        currentSession = Some session
        authenticatedUser = Some session.userId }
  | None -> state
```

## Phase 3: Developer Experience

### 3.1 VS Code Extension Updates

**package.json additions**
```json
{
  "contributes": {
    "commands": [
      {
        "command": "darklang.patch.create",
        "title": "Darklang: Create Patch"
      },
      {
        "command": "darklang.sync.status", 
        "title": "Darklang: Sync Status"
      }
    ],
    "statusBarItems": [
      {
        "id": "darklang.patch",
        "alignment": "left",
        "priority": 100
      }
    ]
  }
}
```

### 3.2 Real-time Updates

- WebSocket connection to dev server
- Push notifications for new patches
- Auto-refresh package tree on changes

## Implementation Roadmap

### Day 1 (Before tomorrow's meeting)
- [x] Research and planning
- [ ] Core types in F#
- [ ] Basic CLI commands
- [ ] SQLite setup
- [ ] Manual patch file exchange

### Day 2 (Before Sunday)
- [ ] HTTP sync protocol
- [ ] Patch validation
- [ ] Conflict detection basics
- [ ] Demo preparation

### Week 1
- [ ] VS Code integration
- [ ] Session persistence
- [ ] Automated testing
- [ ] Documentation

### Week 2+
- [ ] Performance optimization
- [ ] Advanced conflict resolution
- [ ] AI integration hooks
- [ ] Community features

## Technical Risks & Mitigation

### Risk 1: Type System Complexity
**Risk**: Patch ops might not capture all type system changes
**Mitigation**: Start with function-only patches, add types later

### Risk 2: Sync Protocol Failures
**Risk**: Network issues cause inconsistent state
**Mitigation**: Local-first with explicit sync commands

### Risk 3: Database Corruption
**Risk**: SQLite corruption loses work
**Mitigation**: Regular backups, export to patch files

### Risk 4: Performance with Large Patches
**Risk**: Large patches slow down CLI
**Mitigation**: Pagination, lazy loading, background processing

## Testing Strategy

### Unit Tests
- Patch validation logic
- Conflict detection algorithms
- Op serialization/deserialization

### Integration Tests
- Full flow: create → validate → sync → apply
- Multi-user scenarios
- Network failure handling

### Manual Testing Checklist
- [ ] Create function, share with coworker
- [ ] Handle conflicting edits
- [ ] Work offline, sync later
- [ ] Session persistence across restarts

## Next Steps

1. **Immediate** (Today):
   - Create DevCollab.fs with core types
   - Add patch commands to CLI
   - Set up SQLite database

2. **Tomorrow Morning**:
   - Test basic flow with coworker
   - Prepare demo script
   - Document any issues

3. **For Sunday**:
   - Polish sync protocol
   - Add conflict resolution
   - Prepare presentation materials

This implementation plan provides the minimal foundation for developer collaboration while being extensible for future features.