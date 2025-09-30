# Complete Darklang Developer Collaboration System


## 🏗️ Architecture

### Core Components

1. **F# Backend Types** (`LibPackageManager/`)
   - `DevCollab.fs` - Core types (Ops, Patches, Sessions)
   - `DevCollabDb.fs` - SQLite database operations
   - `DevCollabConflicts.fs` - Conflict detection algorithms
   - `DevCollabResolution.fs` - Advanced conflict resolution strategies

2. **CLI Interface** (`packages/darklang/cli/`)
   - `patch.dark` - Patch management commands
   - `session.dark` - Work session management
   - `sync.dark` - Server synchronization
   - `auth.dark` - Authentication system
   - `conflicts.dark` - Conflict resolution UI
   - `database.dark` - Database abstraction layer

3. **HTTP Sync Server** (`DevCollabServer/`)
   - `Server.fs` - ASP.NET Core server for patch sharing
   - REST API for push/pull operations
   - Conflict detection and resolution coordination

4. **Builtin Functions** (`BuiltinCliHost/`)
   - `DevCollab.fs` - Database operation builtins
   - `DevCollabHttp.fs` - HTTP client operations

## 📊 Data Model

### Core Types
```fsharp
type PackageOp = 
  | AddFunction | UpdateFunction | AddType | UpdateType
  | AddValue | MoveEntity | DeprecateEntity

type Patch = {
  id: PatchId
  author: UserId  
  intent: string
  ops: List<PackageOp>
  status: Draft | Ready | Applied | Rejected
  dependencies: Set<PatchId>
  // ... metadata
}

type Session = {
  id: SessionId
  name: string
  intent: string
  owner: UserId
  patches: List<PatchId>
  context: WorkContext
  // ... state management
}
```

### Database Schema (SQLite)
```sql
-- User management
CREATE TABLE collab_users (
  id TEXT PRIMARY KEY,
  username TEXT UNIQUE NOT NULL
);

-- Patch storage
CREATE TABLE collab_patches (
  id TEXT PRIMARY KEY,
  author_id TEXT REFERENCES collab_users(id),
  intent TEXT NOT NULL,
  ops_json TEXT NOT NULL,
  status TEXT CHECK(status IN ('draft', 'ready', 'applied', 'rejected')),
  -- ... timestamps and metadata
);

-- Session management  
CREATE TABLE collab_sessions (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  owner_id TEXT REFERENCES collab_users(id),
  context_json TEXT,
  -- ... session state
);

-- Sync state tracking
CREATE TABLE collab_sync_state (
  instance_id TEXT PRIMARY KEY,
  user_id TEXT REFERENCES collab_users(id),
  server_url TEXT NOT NULL
);
```

## 🔄 Developer Workflows

### 1. Basic Collaboration Flow
```bash
# Developer A creates and shares a function
$ dark auth login stachu
$ dark session new --intent "Add List.filterMap"
$ dark patch create "Add filtering and mapping function"
# [Edit code in VS Code]
$ dark patch ready
$ dark sync push {instanceId}

# Developer B receives and uses the function  
$ dark auth login ocean
$ dark sync pull
$ dark patch list
$ dark patch view abc123
$ dark patch apply abc123
```

### 2. Conflict Resolution Flow
```bash
# When conflicts are detected
$ dark sync pull
⚠️ Conflicts detected with incoming patches

$ dark conflicts list
🔴 c1: Same function modified
🟡 c2: Name collision

$ dark conflicts plan
# Review resolution recommendations

$ dark conflicts auto
✅ Auto-resolved 1 conflict

$ dark conflicts show c1
# Review complex conflict details

$ dark conflicts resolve c1 keep-local
✅ Conflict resolved
```

### 3. Session Management Flow
```bash
# Start work session
$ dark session new --intent "Fix String module bugs"
$ dark patch create "Handle edge cases in String.split"
# [Work on changes]

# Suspend work
$ dark session suspend
✅ Session state saved

# Resume later
$ dark session continue string-fixes
✅ Restored to: /Darklang.Stdlib.String
✅ Current patch: draft-xyz789

# Complete work
$ dark patch ready
$ dark sync push
$ dark session end
```

## 🛠️ Command Reference

### Patch Commands
- `patch create [intent]` - Create new patch
- `patch list` - List all patches  
- `patch view <id>` - Show patch details
- `patch apply <id>` - Apply patch to local code
- `patch ready` - Mark patch ready for sharing
- `patch status` - Show current patch status

### Session Commands
- `session new --intent "description"` - Start new session
- `session list` - List all sessions
- `session continue <name>` - Resume session
- `session suspend` - Pause current session
- `session current` - Show current session info
- `session end` - Complete current session

### Sync Commands
- `sync status` - Show connection and patch status
- `sync push` - Send ready patches to server
- `sync pull` - Get patches from server
- `sync config` - Configure sync settings

### Auth Commands
- `auth login <username>` - Authenticate user
- `auth logout` - Sign out
- `auth whoami` - Show current user
- `auth status` - Show authentication status

### Conflict Commands
- `conflicts list` - Show all detected conflicts
- `conflicts show <id>` - Detailed conflict info
- `conflicts resolve <id> <strategy>` - Apply resolution
- `conflicts plan` - Get resolution recommendations
- `conflicts auto` - Auto-resolve simple conflicts
- `conflicts report` - Generate resolution report

## 🔧 Configuration

### Server Configuration
```bash
# Start development server
$ dotnet run --project backend/src/DevCollabServer -- 3000

# Server provides REST API:
# POST /patches/push - Upload patches
# GET /patches/pull - Download patches  
# GET /patches/{id} - Get specific patch
```

### CLI Configuration
```bash
# Set sync preferences
$ dark sync config set auto-sync false
$ dark sync config set auto-apply false  # Safe: manual review required
$ dark sync config set conflict-resolution prompt
```
