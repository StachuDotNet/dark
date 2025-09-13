# Editor-Agnostic Darklang Collaboration

## 🎯 Overview

The Darklang collaboration system is designed to work with any LSP-compatible editor. Through the standard LSP initialize handshake, editors can discover and enable collaboration features based on their capabilities.

## 🤝 LSP Capability Negotiation

### Initialize Request (Client → Server)

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "capabilities": {
      "textDocument": { /* standard LSP capabilities */ },
      "workspace": { /* standard LSP capabilities */ },
      "darklangCollaboration": {
        "patchTreeView": true,           // Can display patch hierarchies
        "conflictResolutionUI": true,    // Can show conflict resolution UI
        "sessionManagementUI": false,    // Cannot display session UI
        "realtimeNotifications": true,   // Can receive live updates
        "webviewProvider": true          // Can display custom webviews
      }
    },
    "clientInfo": {
      "name": "Visual Studio Code",
      "version": "1.85.0"
    }
  }
}
```

### Initialize Response (Server → Client)

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "capabilities": {
      "textDocumentSync": { /* standard LSP capabilities */ },
      "completionProvider": { /* standard LSP capabilities */ },
      "darklangCollaboration": {
        "patchProvider": true,
        "sessionProvider": false,        // Disabled due to client limitation
        "conflictProvider": true,
        "syncProvider": true,
        "realtimeProvider": true,
        "executeCommandProvider": {
          "commands": [
            "darklang.patch.create",
            "darklang.patch.list",
            "darklang.patch.apply",
            "darklang.conflicts.auto",
            "darklang.sync.push",
            "darklang.sync.pull"
          ]
        }
      }
    },
    "serverInfo": {
      "name": "Darklang Language Server",
      "version": "1.0.0-collab"
    }
  }
}
```

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

## 🎛️ Editor-Specific Integrations

### VS Code Integration

**Capabilities:** Full-featured with rich UI
- Tree views for patches, sessions, conflicts
- Webview panels for conflict resolution  
- Status bar integration
- Real-time notifications
- Custom commands palette integration

**Example Configuration:**
```json
{
  "darklangCollaboration": {
    "patchTreeView": true,
    "conflictResolutionUI": true,
    "sessionManagementUI": true,
    "realtimeNotifications": true,
    "webviewProvider": true
  }
}
```

### Neovim/Vim Integration

**Capabilities:** Command-focused interface
- `:DarkPatchCreate` commands
- Floating windows for patch lists
- Quickfix lists for conflicts
- Status line integration

**Example Configuration:**
```json
{
  "darklangCollaboration": {
    "patchTreeView": false,
    "conflictResolutionUI": false,
    "sessionManagementUI": false,
    "realtimeNotifications": false,
    "webviewProvider": false
  }
}
```

**Vim Commands:**
```vim
:DarkPatchCreate "Fix validation logic"
:DarkPatchList
:DarkPatchApply patch-abc123
:DarkConflictsList
:DarkConflictsAuto
:DarkSync push
:DarkSync pull
```

### Emacs Integration

**Capabilities:** Buffer-based interface
- Special buffers for patch management
- Magit-style interface for conflicts
- Mode line integration
- Org-mode integration for session notes

**Example Configuration:**
```json
{
  "darklangCollaboration": {
    "patchTreeView": true,
    "conflictResolutionUI": true,
    "sessionManagementUI": false,
    "realtimeNotifications": true,
    "webviewProvider": false
  }
}
```

**Emacs Commands:**
```elisp
M-x darklang-patch-create
M-x darklang-patch-list
M-x darklang-conflicts-list
M-x darklang-sync-status
```

### Sublime Text Integration

**Capabilities:** Panel-based interface
- Quick panel for patch selection
- Console output for operations
- Basic status bar integration

**Example Configuration:**
```json
{
  "darklangCollaboration": {
    "patchTreeView": false,
    "conflictResolutionUI": false,
    "sessionManagementUI": false,
    "realtimeNotifications": false,
    "webviewProvider": false
  }
}
```

## 🔧 Implementation Examples

### Minimal LSP Client (Python)

```python
import json
from pylsp_jsonrpc import streams, dispatchers

class DarklangCollaborationClient:
    def __init__(self, lsp_client):
        self.lsp_client = lsp_client
        self.collaboration_enabled = False
    
    def initialize(self):
        """Initialize with collaboration capabilities"""
        init_params = {
            "capabilities": {
                "darklangCollaboration": {
                    "patchTreeView": False,
                    "conflictResolutionUI": False,
                    "realtimeNotifications": False,
                    "webviewProvider": False
                }
            },
            "clientInfo": {"name": "Minimal Client", "version": "1.0"}
        }
        
        response = self.lsp_client.initialize(init_params)
        collab_caps = response.get("capabilities", {}).get("darklangCollaboration", {})
        self.collaboration_enabled = collab_caps.get("patchProvider", False)
        
        return self.collaboration_enabled
    
    def list_patches(self):
        """List all patches using LSP request"""
        if not self.collaboration_enabled:
            return []
        
        response = self.lsp_client.send_request("darklang/patches/list", {})
        return response
    
    def create_patch(self, intent):
        """Create a new patch"""
        params = {"intent": intent}
        response = self.lsp_client.send_request("darklang/patches/create", params)
        return response.get("patchId")
    
    def list_conflicts(self):
        """List current conflicts"""
        response = self.lsp_client.send_request("darklang/conflicts/list", {})
        return response
    
    def auto_resolve_conflicts(self):
        """Auto-resolve simple conflicts"""
        response = self.lsp_client.send_request("darklang/conflicts/auto", {})
        return response

# Usage example
client = DarklangCollaborationClient(lsp_client)
if client.initialize():
    print("Collaboration features enabled!")
    
    # Create a patch
    patch_id = client.create_patch("Fix edge case in validation")
    print(f"Created patch: {patch_id}")
    
    # List patches
    patches = client.list_patches()
    for patch in patches.get("draftPatches", []):
        print(f"Draft: {patch['intent']}")
    
    # Handle conflicts
    conflicts = client.list_conflicts()
    if conflicts:
        print(f"Found {len(conflicts)} conflicts")
        result = client.auto_resolve_conflicts()
        print(f"Auto-resolved {result.get('resolved', 0)} conflicts")
```

### Basic Vim Plugin

```vim
" darklang-collab.vim - Basic Darklang collaboration for Vim

function! DarkPatchCreate()
    let intent = input('Patch intent: ')
    if empty(intent)
        return
    endif
    
    let params = {'intent': intent}
    let response = luaeval('vim.lsp.buf_request_sync(0, "darklang/patches/create", _A, 1000)', params)
    
    if !empty(response)
        let patch_id = response[0]['result']['patchId']
        echo 'Created patch: ' . patch_id
    else
        echo 'Failed to create patch'
    endif
endfunction

function! DarkPatchList()
    let response = luaeval('vim.lsp.buf_request_sync(0, "darklang/patches/list", {}, 1000)')
    
    if !empty(response)
        let patches = response[0]['result']
        
        " Create quickfix list
        let qf_list = []
        
        for patch in patches['draftPatches']
            call add(qf_list, {
                \ 'text': patch['intent'] . ' (draft)',
                \ 'valid': 1
            \ })
        endfor
        
        for patch in patches['incomingPatches']
            call add(qf_list, {
                \ 'text': patch['intent'] . ' (from ' . patch['author'] . ')',
                \ 'valid': 1
            \ })
        endfor
        
        call setqflist(qf_list)
        copen
    endif
endfunction

function! DarkConflictsList()
    let response = luaeval('vim.lsp.buf_request_sync(0, "darklang/conflicts/list", {}, 1000)')
    
    if !empty(response)
        let conflicts = response[0]['result']
        
        if empty(conflicts)
            echo 'No conflicts found!'
            return
        endif
        
        let qf_list = []
        for conflict in conflicts
            call add(qf_list, {
                \ 'text': '[' . conflict['severity'] . '] ' . conflict['description'],
                \ 'valid': 1
            \ })
        endfor
        
        call setqflist(qf_list)
        copen
        echo 'Found ' . len(conflicts) . ' conflicts'
    endif
endfunction

function! DarkSync(direction)
    let method = 'darklang/sync/' . a:direction
    let response = luaeval('vim.lsp.buf_request_sync(0, _A, {}, 5000)', method)
    
    if !empty(response)
        echo 'Sync ' . a:direction . ' completed'
    else
        echo 'Sync ' . a:direction . ' failed'
    endif
endfunction

" Commands
command! -nargs=0 DarkPatchCreate call DarkPatchCreate()
command! -nargs=0 DarkPatchList call DarkPatchList()
command! -nargs=0 DarkConflictsList call DarkConflictsList()
command! -nargs=1 DarkSync call DarkSync(<q-args>)

" Keymaps
nnoremap <leader>dpc :DarkPatchCreate<CR>
nnoremap <leader>dpl :DarkPatchList<CR>
nnoremap <leader>dcl :DarkConflictsList<CR>
nnoremap <leader>dsp :DarkSync push<CR>
nnoremap <leader>dsP :DarkSync pull<CR>
```

## 🎯 Benefits of This Approach

### 1. **Editor Agnostic**
- Works with any LSP-compatible editor
- Standard LSP protocol - no custom protocols
- Graceful degradation based on editor capabilities

### 2. **Feature Discovery**
- Clients advertise their UI capabilities
- Server adapts feature set accordingly
- No editor-specific code paths in server

### 3. **Backward Compatible**
- Standard LSP clients ignore collaboration extensions
- Existing workflows continue to work
- Optional enhancement, not requirement

### 4. **Scalable Implementation**
- All logic remains server-side in Darklang/F#
- Minimal client implementation required
- Rich editors get rich features, simple editors get core features

### 5. **Consistent Experience**
- Same underlying collaboration model across all editors
- Editor-appropriate UI metaphors
- Shared server state and conflict resolution

This design ensures that Darklang collaboration works beautifully in VS Code while remaining fully functional in Vim, Emacs, or any other LSP-compatible editor.