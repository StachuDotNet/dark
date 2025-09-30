
## Language Server Protocol Extensions

**Custom LSP Methods:**

```fsharp
// Package management
"package/search" : SearchQuery -> SearchResults
"package/get" : PackageLocation -> PackageItem
"package/findUsages" : PackageLocation -> List<Usage>

// Patch management
"patch/create" : CreatePatchRequest -> Patch
"patch/getReviewData" : PatchId -> PatchReviewData
"patch/apply" : ApplyPatchRequest -> ApplyResult

// Session management
"session/switch" : SessionId -> SwitchResult
"session/transfer" : TransferRequest -> TransferData
"session/getState" : unit -> SessionState

// Sync operations
"sync/pull" : PullRequest -> List<Patch>
"sync/push" : List<Patch> -> PushResult
"sync/conflicts" : unit -> List<Conflict>

// Virtual file system
"vfs/read" : Uri -> FileContent
"vfs/write" : WriteRequest -> WriteResult
"vfs/watch" : Uri -> FileChangeEvents
```






## 🔧 Implementation Components

### 1. Enhanced LSP Server (`collaborationExtensions.dark`)
```fsharp
// Server capabilities that adapt to client
type CollaborationServerCapabilities = {
  patchProvider: Bool
  sessionProvider: Bool  
  conflictProvider: Bool
  syncProvider: Bool
  realtimeProvider: Bool
  executeCommandProvider: ExecuteCommandOptions
}

// Discovers client capabilities and adapts
let parseClientCollaborationCapabilities (clientCapabilities: Json) : CollaborationClientCapabilities

// Handles all collaboration methods
let handleCollaborationMethod (state: LspState) (method: String) : LspState
```

**Methods Supported:**
- `darklang/patches/*` - Patch management
- `darklang/sessions/*` - Session management  
- `darklang/conflicts/*` - Conflict resolution
- `darklang/sync/*` - Server synchronization
- `darklang/notify/*` - Real-time notifications


# LSP Extensions for Darklang+VS Code

## 🎯 Overview

The Darklang collaboration system is designed to work with any LSP-compatible editor. Through the standard LSP initialize handshake, editors can discover and enable collaboration features based on their capabilities.

## 📋 LSP Method Reference

### Core Patch Management

#### `darklang/patches/list`
Lists all patches visible to the current user.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "darklang/patches/list",
  "params": {}
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "draftPatches": [
      {
        "id": "patch-abc123",
        "author": "alice",
        "intent": "Add List.filterMap function",
        "status": "draft",
        "createdAt": "2025-01-15T10:30:00Z",
        "functions": ["Darklang.Stdlib.List.filterMap"],
        "canApply": false,
        "canEdit": true
      }
    ],
    "readyPatches": [...],
    "incomingPatches": [...],
    "appliedPatches": [...]
  }
}
```

#### `darklang/patches/create`
Creates a new patch with the given intent.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "darklang/patches/create",
  "params": {
    "intent": "Fix edge case in String.split"
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "patchId": "patch-def456",
    "message": "Patch created successfully"
  }
}
```

#### `darklang/patches/view`
Gets detailed information about a specific patch.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "darklang/patches/view",
  "params": {
    "patchId": "patch-abc123"
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "result": {
    "patch": {
      "id": "patch-abc123",
      "intent": "Add List.filterMap function",
      "author": "alice",
      "status": "draft",
      "createdAt": "2025-01-15T10:30:00Z",
      "functions": ["Darklang.Stdlib.List.filterMap"]
    },
    "diffText": "// Function implementation diff...",
    "timeline": [
      {
        "timestamp": "2025-01-15T10:30:00Z",
        "event": "created",
        "author": "alice"
      }
    ]
  }
}
```

#### `darklang/patches/apply`
Applies an incoming patch to the local codebase.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "darklang/patches/apply",
  "params": {
    "patchId": "patch-ghi789"
  }
}
```

#### `darklang/patches/ready`
Marks a patch as ready for sharing with the team.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 6,
  "method": "darklang/patches/ready",
  "params": {
    "patchId": "patch-abc123"  // Optional - defaults to current patch
  }
}
```

### Conflict Resolution

#### `darklang/conflicts/list`
Lists all current conflicts that need resolution.

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "result": [
    {
      "id": "conflict-1",
      "type": "Same Function Different Implementation",
      "severity": "high",
      "description": "Function 'filterMap' modified in patches abc123 and def456",
      "patches": ["patch-abc123", "patch-def456"],
      "canAutoResolve": false,
      "resolutionOptions": [
        {
          "strategy": "keep-local",
          "label": "Keep Local Changes",
          "description": "Use your implementation",
          "isDestructive": true
        },
        {
          "strategy": "keep-remote", 
          "label": "Keep Remote Changes",
          "description": "Use incoming implementation",
          "isDestructive": true
        },
        {
          "strategy": "three-way",
          "label": "Three-Way Merge",
          "description": "Attempt intelligent merge",
          "isDestructive": false
        }
      ]
    }
  ]
}
```

#### `darklang/conflicts/resolve`
Resolves a specific conflict using the chosen strategy.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 8,
  "method": "darklang/conflicts/resolve",
  "params": {
    "conflictId": "conflict-1",
    "strategy": "three-way"
  }
}
```

#### `darklang/conflicts/auto`
Automatically resolves all conflicts that can be safely auto-resolved.

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 9,
  "result": {
    "resolved": 2,
    "remaining": 1,
    "message": "Auto-resolved 2 conflicts, 1 requires manual attention"
  }
}
```

### Sync Operations

#### `darklang/sync/status`
Gets the current synchronization status.

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "result": {
    "connected": true,
    "serverUrl": "https://collab.darklang.com",
    "outgoingPatches": 2,
    "incomingPatches": 1,
    "lastSync": "2025-01-15T11:00:00Z",
    "conflicts": 1
  }
}
```

#### `darklang/sync/push`
Pushes ready patches to the collaboration server.

#### `darklang/sync/pull`
Pulls new patches from the collaboration server.

### Real-time Notifications

These are sent as LSP notifications (no response expected):

#### `darklang/notify/patchCreated`
```json
{
  "jsonrpc": "2.0",
  "method": "darklang/notify/patchCreated",
  "params": {
    "type": "patch-created",
    "patchId": "patch-xyz789",
    "author": "bob",
    "intent": "Performance improvements",
    "affectedFunctions": ["Darklang.Stdlib.List.map"],
    "timestamp": "2025-01-15T11:15:00Z"
  }
}
```

#### `darklang/notify/conflictDetected`
```json
{
  "jsonrpc": "2.0",
  "method": "darklang/notify/conflictDetected",
  "params": {
    "type": "conflict-detected",
    "conflictId": "conflict-2",
    "severity": "medium",
    "description": "Name collision for type 'Result'",
    "canAutoResolve": true,
    "timestamp": "2025-01-15T11:20:00Z"
  }
}
```
