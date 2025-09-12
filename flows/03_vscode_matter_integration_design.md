# VS Code Integration Design: Working with Matter (Non-File-Based Development)

## The Fundamental Challenge

**Traditional Development Model (what VS Code expects):**
```
Files on disk → Git commits → Push/pull → Build tools → Deploy
```

**Darklang Model (what we actually have):**
```
Content hashes → Matter sessions → Sync operations → Live deployment
```

**The Gap:** VS Code is built around file manipulation, but Darklang development happens in an abstract content-addressable space managed by Matter.

---

## Core Design Principle: Virtual File System Bridge

Instead of forcing Darklang into files, we create a **virtual file system** that maps Matter concepts to file-like interfaces VS Code can understand.

### Session-Based Virtual Workspace

```
VS Code Workspace ←→ Matter Session
├── src/
│   ├── handlers/
│   │   ├── auth.dark          ←→ Matter: Fn(hash_abc123) at MyApp.Auth.login
│   │   └── api.dark           ←→ Matter: Fn(hash_def456) at MyApp.API.users
│   ├── models/
│   │   ├── user.dark          ←→ Matter: Type(hash_ghi789) at MyApp.Models.User
│   │   └── transaction.dark   ←→ Matter: Type(hash_jkl012) at MyApp.Models.Transaction
│   └── tests/
│       └── auth_test.dark     ←→ Matter: Fn(hash_mno345) at MyApp.Tests.authTests
├── .darklang/
│   ├── session.json           ←→ Current Matter session metadata
│   ├── matter_state.json      ←→ Local state cache
│   └── package_mappings.json  ←→ Virtual file ↔ Package location mappings
└── package.json               ←→ Matter session dependencies
```

---

## Development Flow Designs

### Flow 1: Starting a New Project in VS Code

**User Experience:**
1. `Ctrl+Shift+P` → "Darklang: New Project"
2. Choose template (webapp, cli, lib)
3. VS Code creates virtual workspace structure
4. Files appear that are actually Matter content projections

**Technical Implementation:**
```typescript
// VS Code Extension
async function createDarklangProject(template: string, name: string) {
  // Create Matter session
  const session = await matter.createSession(name, template);
  
  // Create virtual workspace
  const workspace = createVirtualWorkspace(session);
  
  // Register virtual file system
  vscode.workspace.registerFileSystemProvider('darklang', new DarklangFS(session));
  
  // Open workspace
  vscode.commands.executeCommand('vscode.openFolder', 
    vscode.Uri.parse(`darklang://session/${session.id}`));
}
```

### Flow 2: Editing Code (File Simulation)

**User Experience:**
1. Developer opens `src/handlers/auth.dark` in VS Code
2. Sees normal-looking Darklang code
3. Makes changes and saves (`Ctrl+S`)
4. Changes are immediately reflected in Matter

**Technical Implementation:**
```typescript
class DarklangFS implements vscode.FileSystemProvider {
  async readFile(uri: vscode.Uri): Promise<Uint8Array> {
    const location = parseVirtualPath(uri.path);
    const content = await matter.getContent(location);
    return encoder.encode(content.source);
  }
  
  async writeFile(uri: vscode.Uri, content: Uint8Array): Promise<void> {
    const location = parseVirtualPath(uri.path);
    const source = decoder.decode(content);
    
    // Generate new content hash
    const newHash = await matter.hashContent(source);
    
    // Create Matter operations
    const ops = [
      { type: 'AddFunctionContent', hash: newHash, content: source },
      { type: 'UpdateNamePointer', location, newHash }
    ];
    
    // Apply to current session
    await matter.applyOpsToSession(currentSession.id, ops);
    
    // Notify other systems
    this._onDidChangeFile.fire([{ type: vscode.FileChangeType.Changed, uri }]);
  }
}
```

### Flow 3: Matter Session Management in VS Code

**User Experience:**
1. Status bar shows current session: `Session: feature-auth-system`
2. Click to switch sessions or create new ones
3. VS Code workspace updates to reflect different session state

**VS Code Interface:**
```
Status Bar: [Session: feature-auth-system ▼] [📊 5 changes] [🔄 Sync]
```

**Implementation:**
```typescript
// Session management
class SessionStatusBar {
  private statusBarItem: vscode.StatusBarItem;
  
  updateSession(session: MatterSession) {
    this.statusBarItem.text = `Session: ${session.name}`;
    this.statusBarItem.tooltip = `Active session: ${session.name}\nChanges: ${session.changeCount}`;
    this.statusBarItem.command = 'darklang.selectSession';
  }
}

// Session switching updates entire workspace
async function switchSession(sessionId: string) {
  const newSession = await matter.getSession(sessionId);
  
  // Update virtual file system to reflect new session
  await darklangFS.switchSession(newSession);
  
  // Refresh all open editors
  await vscode.commands.executeCommand('workbench.action.reloadWindow');
}
```

### Flow 4: Package Import and Discovery

**User Experience:**
1. `Ctrl+Shift+P` → "Darklang: Add Package"
2. Search interface appears with AI-assisted suggestions
3. Preview package functions inline
4. Import adds virtual files to workspace

**VS Code Interface:**
```
┌─ Add Package ──────────────────────────────┐
│ Search: █ email sending                    │
│                                            │
│ 📦 Darklang.Email                         │
│   └── 📧 send - Send email via SMTP       │
│                                            │
│ 📦 Community.Notifications                │
│   └── 📧 email - Multi-channel notifications │
│                                            │
│ [Preview] [Import] [Try in REPL]          │
└────────────────────────────────────────────┘
```

**Implementation:**
```typescript
// Package import creates virtual files
async function importPackage(packageName: string) {
  const pkg = await matter.getPackage(packageName);
  
  // Add to session dependencies
  await matter.addDependency(currentSession.id, packageName);
  
  // Create virtual files for imported functions
  for (const fn of pkg.functions) {
    const virtualPath = `node_modules/${packageName}/${fn.name}.dark`;
    await darklangFS.createVirtualFile(virtualPath, fn);
  }
  
  // Refresh workspace
  await vscode.commands.executeCommand('workbench.files.action.refreshFilesExplorer');
}
```

### Flow 5: Real-Time Collaboration (Multiple Sessions)

**User Experience:**
1. Teammate shares session link
2. VS Code shows notification: "Alice shared session 'user-auth-feature'"
3. Click to open collaborative workspace
4. See live changes from multiple developers

**VS Code Interface:**
```
Explorer:
├── 📁 src/
├── 👥 Collaborators
│   ├── 👤 Alice (active) - editing auth.dark
│   └── 👤 Bob (away) - last seen 5m ago
└── 🔄 Changes
    ├── ✏️ Alice: Updated login validation
    └── ⏳ You: Draft auth improvements
```

### Flow 6: Testing and Debugging

**User Experience:**
1. Developer sets "breakpoint" on a function
2. Actually creates a trace collection rule in Matter
3. When function executes, VS Code shows trace data
4. Can step through function execution using traces

**Implementation:**
```typescript
// Transform breakpoints into Matter trace rules
class DarklangDebugAdapter implements vscode.DebugAdapter {
  async setBreakpoints(args: vscode.DebugProtocol.SetBreakpointsArguments) {
    const location = parseVirtualPath(args.source.path);
    
    // Create trace collection rule in Matter
    await matter.createTraceRule({
      location,
      condition: 'always',
      sessionId: currentSession.id
    });
  }
  
  // When traces arrive, show as debug session
  async onTraceReceived(trace: MatterTrace) {
    this.sendEvent(new vscode.DebugProtocol.StoppedEvent('breakpoint', 1));
  }
}
```

---

## Technical Implementation Requirements

### 1. Virtual File System Provider
```typescript
interface DarklangFS extends vscode.FileSystemProvider {
  // Map virtual paths to Matter locations
  readFile(uri: vscode.Uri): Promise<Uint8Array>;
  writeFile(uri: vscode.Uri, content: Uint8Array): Promise<void>;
  
  // Session management
  switchSession(session: MatterSession): Promise<void>;
  syncSession(): Promise<void>;
  
  // Package management
  addPackageDependency(packageName: string): Promise<void>;
  removePackageDependency(packageName: string): Promise<void>;
}
```

### 2. Matter Integration Layer
```typescript
interface MatterIntegration {
  // Session operations
  createSession(name: string, template?: string): Promise<MatterSession>;
  switchSession(sessionId: string): Promise<void>;
  syncSession(sessionId: string): Promise<void>;
  
  // Content operations
  getContent(location: PackageLocation): Promise<ContentWithHash>;
  updateContent(location: PackageLocation, newContent: string): Promise<string>;
  
  // Package operations
  searchPackages(query: string): Promise<Package[]>;
  importPackage(packageName: string): Promise<void>;
  
  // Collaboration
  shareSession(sessionId: string): Promise<string>;
  joinSharedSession(shareLink: string): Promise<MatterSession>;
}
```

### 3. Language Server Enhancements
```typescript
// Extend LSP to understand Matter concepts
interface DarklangLanguageServer {
  // Matter-aware completion
  getCompletions(location: PackageLocation): Promise<CompletionItem[]>;
  
  // Cross-session navigation
  gotoDefinition(location: PackageLocation): Promise<Location>;
  findReferences(location: PackageLocation): Promise<Location[]>;
  
  // Session-aware diagnostics
  getDiagnostics(sessionId: string): Promise<Diagnostic[]>;
}
```

### 4. Custom VS Code Views
```typescript
// Session management view
class SessionTreeProvider implements vscode.TreeDataProvider<SessionItem> {
  getChildren(element?: SessionItem): SessionItem[] {
    // Show current session, available sessions, shared sessions
  }
}

// Package explorer view  
class PackageTreeProvider implements vscode.TreeDataProvider<PackageItem> {
  getChildren(element?: PackageItem): PackageItem[] {
    // Show project packages, imported packages, available packages
  }
}

// Changes view (Matter-aware)
class ChangesTreeProvider implements vscode.TreeDataProvider<ChangeItem> {
  getChildren(element?: ChangeItem): ChangeItem[] {
    // Show session changes, not git changes
  }
}
```

---

## Key Insights

1. **Don't fight VS Code's file model** - embrace it with a virtual file system that maps cleanly to Matter concepts

2. **Session as workspace** - Each Matter session becomes a VS Code workspace, allowing natural switching between different development contexts

3. **Real-time sync** - Changes in VS Code immediately create Matter operations, keeping the abstract and concrete views synchronized

4. **Package imports as dependencies** - Treat Matter packages like node_modules, creating virtual files for imported functions

5. **Traces as debugging** - Transform Matter's trace system into VS Code's debugging interface

6. **Collaboration through session sharing** - Multiple developers can work in the same Matter session, visible in VS Code

This approach lets developers use familiar VS Code workflows while working in Darklang's content-addressable, session-based development model.