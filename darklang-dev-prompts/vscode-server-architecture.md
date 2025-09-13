# Server-First VS Code Collaboration Architecture

## 🎯 Design Principles

1. **Minimal JavaScript** - VS Code extension is a thin UI layer
2. **Server-Side Logic** - All collaboration logic in F#/Darklang
3. **LSP Extension** - Extend existing Language Server Protocol with collaboration methods
4. **Real-time Updates** - WebSocket connection for live updates
5. **Native Integration** - Use VS Code's built-in UI components where possible

## 🏗️ Architecture Overview

```
┌─────────────────┐    LSP + Custom Protocol    ┌─────────────────────┐
│   VS Code       │ ←─────────────────────────→ │  Darklang LSP++     │
│   Extension     │                              │  Server             │
│   (minimal JS)  │    WebSocket (real-time)    │  (F# + Darklang)    │
└─────────────────┘ ←─────────────────────────→ └─────────────────────┘
         ↕                                                ↕
┌─────────────────┐                              ┌─────────────────────┐
│  VS Code UI     │                              │  SQLite Database    │
│  - TreeViews    │                              │  - Patches          │
│  - Webviews     │                              │  - Sessions         │
│  - SCM Provider │                              │  - Conflicts        │
│  - Status Bar   │                              │  - Users            │
└─────────────────┘                              └─────────────────────┘
```

## 📡 Extended LSP Protocol

### Custom LSP Methods

```typescript
// Patch Management
darklang/patches/list -> PatchPanelData
darklang/patches/create -> {intent: string} -> {patchId: string}
darklang/patches/view -> {patchId: string} -> PatchDetails
darklang/patches/apply -> {patchId: string} -> ApplyResult
darklang/patches/ready -> {patchId?: string} -> StatusResult

// Session Management  
darklang/sessions/list -> SessionPanelData
darklang/sessions/create -> {intent: string} -> {sessionId: string}
darklang/sessions/switch -> {sessionId: string} -> SwitchResult
darklang/sessions/suspend -> StatusResult
darklang/sessions/end -> StatusResult

// Conflict Resolution
darklang/conflicts/list -> List<ConflictInfo>
darklang/conflicts/resolve -> {conflictId: string, strategy: string} -> ResolutionResult
darklang/conflicts/auto -> AutoResolveResult
darklang/conflicts/ui -> {conflictId: string} -> ConflictResolutionUI

// Sync Operations
darklang/sync/status -> SyncStatus
darklang/sync/push -> SyncResult
darklang/sync/pull -> SyncResult

// Real-time Notifications (WebSocket)
darklang/notify/patch-created -> PatchCreatedEvent
darklang/notify/conflict-detected -> ConflictDetectedEvent
darklang/notify/patch-applied -> PatchAppliedEvent
darklang/notify/session-changed -> SessionChangedEvent
```

### WebSocket Events

```typescript
interface PatchCreatedEvent {
  type: 'patch-created'
  patchId: string
  author: string
  intent: string
  affectedFunctions: string[]
}

interface ConflictDetectedEvent {
  type: 'conflict-detected'
  conflictId: string
  severity: 'low' | 'medium' | 'high'
  description: string
  canAutoResolve: boolean
}

interface SessionChangedEvent {
  type: 'session-changed'
  userId: string
  sessionId: string
  action: 'started' | 'suspended' | 'ended'
  location?: string
}
```

## 🔧 F# Server Implementation

### LSP Extension Module

```fsharp
/// backend/src/LibLanguageServer/CollaborationExtensions.fs
module LibLanguageServer.CollaborationExtensions

open LibLanguageServer.Types
open LibPackageManager.DevCollab
open LibPackageManager.DevCollabDb

let private collaborationMethods = Map [
  ("darklang/patches/list", handlePatchesList)
  ("darklang/patches/create", handlePatchCreate)
  ("darklang/patches/view", handlePatchView)
  ("darklang/patches/apply", handlePatchApply)
  ("darklang/conflicts/list", handleConflictsList)
  ("darklang/conflicts/resolve", handleConflictsResolve)
  ("darklang/sessions/list", handleSessionsList)
  ("darklang/sync/status", handleSyncStatus)
]

let handleCollaborationMethod (method: string) (params: JsonValue) (context: LSPContext) : Task<JsonValue> = task {
  match collaborationMethods.TryFind(method) with
  | Some handler -> return! handler params context
  | None -> return! failwith $"Unknown collaboration method: {method}"
}

// Integration with existing LSP server
let extendLSPServer (server: LanguageServer) : LanguageServer =
  server.addCustomMethods collaborationMethods
```

### WebSocket Real-time Server

```fsharp
/// backend/src/LibLanguageServer/RealtimeCollaboration.fs
module LibLanguageServer.RealtimeCollaboration

open System.Net.WebSockets
open LibPackageManager.DevCollab

type CollaborationHub = {
  clients: Map<UserId, WebSocket>
  activeUsers: Map<UserId, UserActivity>
}

type UserActivity = {
  currentFunction: string option
  currentSession: SessionId option
  lastSeen: System.DateTime
}

let broadcastEvent (hub: CollaborationHub) (event: CollaborationEvent) : Task<unit> = task {
  let message = JsonConvert.SerializeObject(event)
  let buffer = System.Text.Encoding.UTF8.GetBytes(message)
  
  for KeyValue(userId, socket) in hub.clients do
    if socket.State = WebSocketState.Open then
      do! socket.SendAsync(ArraySegment(buffer), WebSocketMessageType.Text, true, CancellationToken.None)
}

// Event handlers that trigger broadcasts
let onPatchCreated (hub: CollaborationHub) (patch: Patch) : Task<unit> = task {
  let event = {
    type_ = "patch-created"
    patchId = patch.id.ToString()
    author = patch.author
    intent = patch.intent
    affectedFunctions = extractFunctionNames patch.ops
  }
  do! broadcastEvent hub event
}

let onConflictDetected (hub: CollaborationHub) (conflict: Conflict) : Task<unit> = task {
  let event = {
    type_ = "conflict-detected"
    conflictId = conflict.id.ToString()
    severity = conflict.severity.ToString().ToLower()
    description = conflict.description
    canAutoResolve = conflict.canAutoResolve
  }
  do! broadcastEvent hub event
}
```

## 📱 Minimal VS Code Extension

### Main Extension (TypeScript)

```typescript
// vscode-extension/src/extension.ts (minimal)
import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';
import { CollaborationUI } from './collaborationUI';
import { RealtimeConnection } from './realtime';

export function activate(context: vscode.ExtensionContext) {
    // Reuse existing Darklang LSP client
    const client = getExistingLanguageClient();
    
    // Add collaboration UI
    const collaborationUI = new CollaborationUI(client, context);
    const realtime = new RealtimeConnection(client, collaborationUI);
    
    // Register minimal commands that delegate to LSP
    registerCollaborationCommands(context, client);
    
    // Start real-time connection
    realtime.connect();
}

function registerCollaborationCommands(context: vscode.ExtensionContext, client: LanguageClient) {
    // Patch commands
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.patch.create', async () => {
            const intent = await vscode.window.showInputBox({
                prompt: 'Describe your patch intent'
            });
            if (intent) {
                await client.sendRequest('darklang/patches/create', { intent });
            }
        })
    );
    
    // Other commands follow same pattern - just delegate to LSP
}
```

### Collaboration UI (TypeScript)

```typescript
// vscode-extension/src/collaborationUI.ts
export class CollaborationUI {
    private patchProvider: PatchTreeProvider;
    private conflictProvider: ConflictTreeProvider;
    
    constructor(private client: LanguageClient, private context: vscode.ExtensionContext) {
        this.setupTreeProviders();
        this.setupWebviews();
        this.setupStatusBar();
    }
    
    private setupTreeProviders() {
        // Tree providers that request data from LSP server
        this.patchProvider = new PatchTreeProvider(this.client);
        vscode.window.createTreeView('darklangPatches', {
            treeDataProvider: this.patchProvider
        });
    }
    
    async refreshPatchData() {
        const data = await this.client.sendRequest('darklang/patches/list', {});
        this.patchProvider.updateData(data);
    }
    
    async showConflictResolution(conflictId: string) {
        const uiData = await this.client.sendRequest('darklang/conflicts/ui', { conflictId });
        this.createConflictWebview(uiData);
    }
    
    private createConflictWebview(uiData: any) {
        const panel = vscode.window.createWebviewPanel(
            'conflictResolution',
            'Resolve Conflict',
            vscode.ViewColumn.One,
            { enableScripts: true }
        );
        
        panel.webview.html = this.generateConflictHtml(uiData);
        
        panel.webview.onDidReceiveMessage(async message => {
            if (message.type === 'resolve') {
                await this.client.sendRequest('darklang/conflicts/resolve', {
                    conflictId: message.conflictId,
                    strategy: message.strategy
                });
                panel.dispose();
            }
        });
    }
}
```

### Tree Data Providers (TypeScript)

```typescript
// vscode-extension/src/providers/patchTreeProvider.ts
export class PatchTreeProvider implements vscode.TreeDataProvider<PatchTreeItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<PatchTreeItem | undefined>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;
    
    private data: PatchPanelData | null = null;
    
    constructor(private client: LanguageClient) {}
    
    async updateData(data: PatchPanelData) {
        this.data = data;
        this._onDidChangeTreeData.fire(undefined);
    }
    
    getTreeItem(element: PatchTreeItem): vscode.TreeItem {
        return element;
    }
    
    getChildren(element?: PatchTreeItem): PatchTreeItem[] {
        if (!this.data) return [];
        
        if (!element) {
            // Root level categories
            return [
                new PatchTreeItem('Draft Patches', 'category', this.data.draftPatches.length),
                new PatchTreeItem('Ready Patches', 'category', this.data.readyPatches.length),
                new PatchTreeItem('Incoming Patches', 'category', this.data.incomingPatches.length),
            ];
        } else {
            // Patch items under categories
            return this.getPatchesForCategory(element.label);
        }
    }
}
```

## 🔄 Development Workflow

### 1. Starting Development Session
```typescript
// VS Code: User clicks "New Session"
// → LSP: darklang/sessions/create {intent: "Fix List module"}
// → F#: Creates session, saves to SQLite
// → WebSocket: Broadcasts session-started event
// → UI: Updates session panel, status bar
```

### 2. Creating Patch
```typescript
// VS Code: User edits function, runs "Create Patch"  
// → LSP: darklang/patches/create {intent: "Add error handling"}
// → F#: Analyzes changes, creates patch, saves to DB
// → WebSocket: Broadcasts patch-created event
// → UI: Updates patch tree, shows in draft patches
```

### 3. Conflict Resolution
```typescript
// Background: F# detects conflict during sync
// → WebSocket: Broadcasts conflict-detected event
// → UI: Shows notification, updates conflict tree
// → User: Clicks "Resolve"
// → LSP: darklang/conflicts/ui {conflictId: "c1"}
// → F#: Returns resolution UI data with options
// → UI: Shows webview with resolution strategies
// → User: Selects strategy
// → LSP: darklang/conflicts/resolve {conflictId: "c1", strategy: "rename-both"}
// → F#: Applies resolution, updates database
// → WebSocket: Broadcasts conflict-resolved event
```

## 🎯 Benefits of This Architecture

### 1. **Minimal JavaScript Maintenance**
- VS Code extension is mostly UI glue code
- All business logic in F# where it belongs
- Easier to test and maintain

### 2. **Reuse Existing Infrastructure**
- Extends current LSP server instead of separate service
- Uses existing DB connections and types
- Leverages current build/deploy process

### 3. **Rich Server-Side Capabilities**
- Complex conflict analysis in F#
- Direct database access
- Integration with existing Darklang systems

### 4. **Real-time Collaboration**
- WebSocket for live updates
- Team awareness features
- Immediate conflict notification

### 5. **Native VS Code Integration**
- Uses built-in TreeViews, SCM providers
- Follows VS Code patterns and conventions
- Minimal custom UI components

## 📋 Implementation Order

1. **Extend LSP Server** - Add collaboration methods to existing server
2. **Database Integration** - Connect LSP methods to SQLite operations
3. **WebSocket Layer** - Add real-time event broadcasting
4. **Minimal VS Code Client** - Create thin UI layer
5. **Tree Providers** - Implement patch/session/conflict trees
6. **Conflict Resolution UI** - WebView for interactive resolution
7. **Status Bar Integration** - Show collaboration status
8. **Real-time Updates** - Live UI updates from WebSocket events

This architecture keeps 90% of the logic in F#/Darklang while providing a rich VS Code experience through minimal JavaScript glue code.