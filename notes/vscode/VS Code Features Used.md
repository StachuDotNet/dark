# VS Code Features Used: ViewContainer-Based Architecture

## 4. Custom ViewContainer for Darklang

### VS Code Feature
`ViewContainer` API for creating custom activity bar sections with multiple tree views.

### How We Use It
**Custom ViewContainer Implementation:**
```typescript
class DarklangViewContainer {
  constructor(private context: vscode.ExtensionContext, private lspClient: LanguageClient) {
    this.createViewContainer();
  }

  private createViewContainer(): void {
    // Create custom activity bar icon for Darklang
    const darklangContainer = vscode.window.createViewContainer('darklang', {
      title: 'Darklang',
      icon: new vscode.ThemeIcon('symbol-class') // Use appropriate icon
    });

    // Register tree views within the container
    this.registerPackageTreeView();
    this.registerPatchTreeView();
    this.registerSessionTreeView();
  }

  private registerPackageTreeView(): void {
    const packagesProvider = new DarklangPackageTreeProvider(this.lspClient);
    vscode.window.createTreeView('darklang.packages', {
      treeDataProvider: packagesProvider,
      showCollapseAll: true,
      canSelectMany: false
    });
  }

  private registerPatchTreeView(): void {
    const patchProvider = new DarklangPatchTreeProvider(this.lspClient);
    vscode.window.createTreeView('darklang.patches', {
      treeDataProvider: patchProvider,
      showCollapseAll: true,
      canSelectMany: false
    });
  }

  private registerSessionTreeView(): void {
    const sessionProvider = new DarklangSessionTreeProvider(this.lspClient);
    vscode.window.createTreeView('darklang.sessions', {
      treeDataProvider: sessionProvider,
      showCollapseAll: true,
      canSelectMany: false
    });
  }
}
```

### Custom Patch TreeView (NOT SCM)
```typescript
class DarklangPatchTreeProvider implements vscode.TreeDataProvider<PatchTreeItem> {
  constructor(private lspClient: LanguageClient) {}

  async getChildren(element?: PatchTreeItem): Promise<PatchTreeItem[]> {
    if (!element) {
      // Root level: current patch, drafts, incoming, applied
      const patchStatus = await this.lspClient.sendRequest('patch/getStatus');
      return [
        new PatchTreeItem('current', patchStatus.current),
        new PatchTreeItem('drafts', patchStatus.drafts),
        new PatchTreeItem('incoming', patchStatus.incoming),
        new PatchTreeItem('applied', patchStatus.applied)
      ];
    } else {
      // Show patches within each category
      return element.getChildPatches();
    }
  }

  getTreeItem(element: PatchTreeItem): vscode.TreeItem {
    return {
      label: element.label,
      iconPath: element.getIcon(),
      contextValue: element.contextValue,
      command: element.getCommand(),
      collapsibleState: element.hasChildren() ?
        vscode.TreeItemCollapsibleState.Collapsed :
        vscode.TreeItemCollapsibleState.None
    };
  }
}

class PatchTreeItem {
  constructor(public type: string, public data: any) {}

  get label(): string {
    switch (this.type) {
      case 'current':
        return this.data ? `Current: ${this.data.intent}` : 'No active patch';
      case 'drafts':
        return `Drafts (${this.data.length})`;
      default:
        return this.data.intent || this.type;
    }
  }

  getCommand(): vscode.Command | undefined {
    if (this.type === 'patch') {
      return {
        command: 'darklang.showPatchDetails',
        title: 'Show Patch Details',
        arguments: [this.data.id]
      };
    }
    return undefined;
  }

  getIcon(): vscode.ThemeIcon {
    switch (this.type) {
      case 'current': return new vscode.ThemeIcon('target');
      case 'drafts': return new vscode.ThemeIcon('draft');
      case 'incoming': return new vscode.ThemeIcon('mail-read');
      case 'applied': return new vscode.ThemeIcon('check');
      default: return new vscode.ThemeIcon('note');
    }
  }
}
```

### Custom Patch Management (NOT SCM)
```typescript
class DarklangPatchManager {
  constructor(private lspClient: LanguageClient) {
    this.registerCommands();
  }

  private registerCommands(): void {
    // Custom patch commands - NOT using SCM API
    vscode.commands.registerCommand('darklang.showPatchDetails', (patchId: string) => {
      this.showPatchDetailsPanel(patchId);
    });

    vscode.commands.registerCommand('darklang.applyPatch', (patchId: string) => {
      this.applyPatch(patchId);
    });

    vscode.commands.registerCommand('darklang.validatePatch', (patchId: string) => {
      this.validatePatch(patchId);
    });
  }

  private async showPatchDetailsPanel(patchId: string): Promise<void> {
    // Custom webview for patch details - NOT diff view
    const panel = vscode.window.createWebviewPanel(
      'darklangPatch',
      'Patch Details',
      vscode.ViewColumn.Beside,
      { enableScripts: true }
    );

    const patchData = await this.lspClient.sendRequest('patch/getDetails', { patchId });
    panel.webview.html = this.generatePatchHTML(patchData);
  }

  private generatePatchHTML(patchData: any): string {
    return `
      <!DOCTYPE html>
      <html>
      <head>
        <style>
          .patch-details { padding: 20px; }
          .operation { margin: 10px 0; padding: 10px; border: 1px solid #ccc; }
          .operation-type { font-weight: bold; }
        </style>
      </head>
      <body>
        <div class="patch-details">
          <h2>${patchData.intent}</h2>
          <p>Author: ${patchData.author}</p>
          <p>Status: ${patchData.status}</p>

          <h3>Operations (${patchData.operations.length})</h3>
          ${patchData.operations.map(op => `
            <div class="operation">
              <div class="operation-type">${op.type}</div>
              <div>${op.description}</div>
            </div>
          `).join('')}

          <div class="actions">
            <button onclick="applyPatch('${patchData.id}')">Apply Patch</button>
            <button onclick="validatePatch('${patchData.id}')">Validate</button>
          </div>
        </div>
      </body>
      </html>
    `;
  }
}
```

### Benefits of Custom ViewContainer Approach
- **Clear Separation**: Files vs packages get appropriate UIs
- **Package-Aware**: Shows package operations, not file diffs
- **Custom Validation**: Patch-specific validation and conflict resolution
- **No SCM Confusion**: Patches don't interfere with git/file-based version control
- **Rich UI**: Custom webviews for complex patch operations

---

## 5. Webview API

### VS Code Feature
`WebviewPanel` for rendering custom HTML/CSS/JavaScript interfaces.

### How We Use It
**Minimal JavaScript Implementation:**
```typescript
class PatchReviewPanel {
  private panel?: vscode.WebviewPanel;

  constructor(private lspClient: LanguageClient) {}

  async show(patchId: string): Promise<void> {
    // Get review data from Darklang - equivalent to dark://patch/abc123 content
    const reviewData = await this.lspClient.sendRequest('patch/getReviewData', { patchId });

    this.panel = vscode.window.createWebviewPanel(
      'patchReview',
      `Review: ${reviewData.intent}`,
      vscode.ViewColumn.Beside,
      {
        enableScripts: true,
        retainContextWhenHidden: true
      }
    );

    // Generate HTML from Darklang data
    this.panel.webview.html = this.generateHTML(reviewData);

    // Handle messages from webview
    this.panel.webview.onDidReceiveMessage(message => {
      this.handleWebviewMessage(message);
    });
  }

  private generateHTML(reviewData: any): string {
    // Static HTML template with data injection
    // No complex logic here - all data pre-computed by Darklang
    return `
      <!DOCTYPE html>
      <html>
      <head>
        <style>${this.getCSS()}</style>
      </head>
      <body>
        <div class="patch-review">
          <h1>${reviewData.intent}</h1>
          <div class="metadata">
            <span>Author: ${reviewData.author}</span>
            <span>Created: ${reviewData.createdAt}</span>
            <span>Status: ${reviewData.status}</span>
          </div>
          <div class="changes">
            ${reviewData.changes.map(change => this.renderChange(change)).join('')}
          </div>
          <div class="actions">
            <button onclick="approve()">Approve</button>
            <button onclick="requestChanges()">Request Changes</button>
          </div>
        </div>
        <script>${this.getJavaScript()}</script>
      </body>
      </html>
    `;
  }

  private async handleWebviewMessage(message: any): Promise<void> {
    // Delegate all actions to Darklang
    await this.lspClient.sendRequest('patch/handleReviewAction', {
      action: message.action,
      patchId: message.patchId,
      data: message.data
    });
  }
}
```

### Darklang-Powered Logic
Webview content generation in F#:

```fsharp
module LibLanguageServer.WebViews

type PatchReviewData = {
  patchId: uuid
  intent: string
  author: string
  createdAt: DateTime
  status: PatchStatus
  changes: List<ChangeDisplay>
  validation: ValidationResult
  comments: List<ReviewComment>
}

let generatePatchReviewData (patchId: uuid) : Task<PatchReviewData> = task {
  let! patch = PatchManager.getPatch patchId
  let! validation = PatchValidator.validatePatch patch
  let! comments = ReviewManager.getComments patchId
  let! changes = generateChangeDisplays patch.ops

  return {
    patchId = patchId
    intent = patch.intent
    author = patch.author
    createdAt = patch.createdAt
    status = patch.status
    changes = changes
    validation = validation
    comments = comments
  }
}

let generateChangeDisplays (ops: List<Op>) : Task<List<ChangeDisplay>> = task {
  return! ops |> List.map (fun op -> task {
    match op with
    | AddFunctionContent(hash, fn) ->
      return {
        type_ = "addition"
        title = $"Added function {fn.name}"
        diff = generateFunctionDiff None (Some fn)
        impact = analyzeImpact op
      }
    | UpdateNamePointer(location, oldHash, newHash) ->
      let! oldContent = ContentStore.getByHash oldHash
      let! newContent = ContentStore.getByHash newHash
      return {
        type_ = "modification"
        title = $"Updated {PackageLocation.toString location}"
        diff = generateContentDiff oldContent newContent
        impact = analyzeImpact op
      }
  }) |> Task.WhenAll
}

let handleReviewAction (action: string) (patchId: uuid) (data: obj) : Task<ActionResult> = task {
  match action with
  | "approve" ->
    do! ReviewManager.approvePatch patchId
    return { success = true; message = "Patch approved" }
  | "requestChanges" ->
    let comment = data :?> string
    do! ReviewManager.requestChanges patchId comment
    return { success = true; message = "Changes requested" }
  | _ ->
    return { success = false; message = "Unknown action" }
}
```


---

## 6. Command API

### VS Code Feature
`commands.registerCommand` for adding custom commands to the command palette and context menus.

### How We Use It
**Minimal JavaScript Implementation:**
```typescript
function registerCommands(context: vscode.ExtensionContext, lspClient: LanguageClient) {
  // Package management commands
  context.subscriptions.push(
    vscode.commands.registerCommand('darklang.openPackageItem', async (uri: string) => {
      // URI is already formatted as dark://package/Name.Space.item
      const doc = await vscode.workspace.openTextDocument(vscode.Uri.parse(uri));
      await vscode.window.showTextDocument(doc);
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('darklang.editPackageItem', async (location: string) => {
      const uri = vscode.Uri.parse(`dark://edit/current-patch/${location}`);
      const doc = await vscode.workspace.openTextDocument(uri);
      await vscode.window.showTextDocument(doc);
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('darklang.searchPackages', async () => {
      const query = await vscode.window.showInputBox({
        prompt: 'Search packages',
        placeHolder: 'Enter search terms...'
      });

      if (query) {
        const results = await lspClient.sendRequest('package/search', { query });
        // Show results in quick pick
        const selected = await vscode.window.showQuickPick(
          results.map(item => ({
            label: item.name,
            description: item.location,
            detail: item.description
          }))
        );

        if (selected) {
          vscode.commands.executeCommand('darklang.openPackageItem', `dark://package/${selected.description}`);
        }
      }
    })
  );

  // Patch management commands
  context.subscriptions.push(
    vscode.commands.registerCommand('darklang.createPatch', async () => {
      const intent = await vscode.window.showInputBox({
        prompt: 'Describe what this patch will do',
        placeHolder: 'Add user authentication system...'
      });

      if (intent) {
        const result = await lspClient.sendRequest('patch/create', { intent });
        vscode.window.showInformationMessage(`Created patch: ${result.patchId}`);
      }
    })
  );

  // Session management commands
  context.subscriptions.push(
    vscode.commands.registerCommand('darklang.switchSession', async () => {
      const sessions = await lspClient.sendRequest('session/list');
      const selected = await vscode.window.showQuickPick(
        sessions.map(session => ({
          label: session.name,
          description: session.id,
          detail: `Last activity: ${session.lastActivity}`
        }))
      );

      if (selected) {
        await lspClient.sendRequest('session/switch', { sessionId: selected.description });
        vscode.window.showInformationMessage(`Switched to session: ${selected.label}`);
      }
    })
  );
}
```

### Darklang-Powered Logic
Command implementations in F#:

```fsharp
module LibLanguageServer.Commands

let searchPackages (query: string) : Task<List<PackageSearchResult>> = task {
  let! results = PackageManager.search {
    text = query
    exactMatch = false
    entityTypes = [Fn; Type; Value]
    currentModule = []
  }

  return results.entities |> List.map (fun entity -> {
    name = entity.name
    location = PackageLocation.toString entity.location
    description = entity.description
    type_ = entity.type_
    relevanceScore = entity.relevanceScore
  })
}

let createPatch (intent: string) : Task<CreatePatchResult> = task {
  let patchId = System.Guid.NewGuid()
  let patch = {
    id = patchId
    intent = intent
    ops = []
    status = Draft
    author = getCurrentUser()
    createdAt = DateTime.UtcNow
    parentPatches = []
    validationResult = None
    metadata = createDefaultMetadata()
  }

  do! PatchManager.savePatch patch
  do! SessionManager.setCurrentPatch (Some patchId)

  return {
    patchId = patchId
    success = true
    message = $"Created patch: {intent}"
  }
}

let switchSession (sessionId: SessionId) : Task<SwitchSessionResult> = task {
  let! session = SessionManager.getSession sessionId
  match session with
  | Some session ->
    do! SessionManager.setCurrentSession session
    do! notifyVSCode "session/changed" {|
      sessionId = sessionId
      workspaceState = session.workspaceState
    |}
    return { success = true; sessionName = session.name }
  | None ->
    return { success = false; error = "Session not found" }
}

let listSessions () : Task<List<SessionInfo>> = task {
  let! sessions = SessionManager.getAllSessions()
  return sessions |> List.map (fun session -> {
    id = session.id
    name = session.name
    lastActivity = session.lastActivity
    currentPatch = session.currentPatch
    isCurrent = SessionManager.isCurrentSession session.id
  })
}
```

### Benefits
- **Simple JavaScript**: Commands just collect input and delegate to Darklang
- **Rich functionality**: Complex operations handled in strongly-typed F#
- **Consistent behavior**: Same command logic available in CLI and other interfaces
- **Easy testing**: Command logic can be unit tested in F#

---

## 7. Status Bar API

### VS Code Feature
`window.createStatusBarItem` for showing information in the status bar.

### How We Use It
**Minimal JavaScript Implementation:**
```typescript
class DarklangStatusBar {
  private items: Map<string, vscode.StatusBarItem> = new Map();

  constructor(private lspClient: LanguageClient) {
    this.createStatusItems();
    this.startUpdateLoop();
  }

  private createStatusItems(): void {
    // Instance status
    const instanceItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
    instanceItem.command = 'darklang.switchInstance';
    this.items.set('instance', instanceItem);

    // Session status
    const sessionItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 99);
    sessionItem.command = 'darklang.switchSession';
    this.items.set('session', sessionItem);

    // Patch status
    const patchItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 98);
    patchItem.command = 'darklang.managePatch';
    this.items.set('patch', patchItem);

    // Sync status
    const syncItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 97);
    syncItem.command = 'darklang.sync';
    this.items.set('sync', syncItem);

    // Show all items
    this.items.forEach(item => item.show());
  }

  private async startUpdateLoop(): Promise<void> {
    while (true) {
      try {
        await this.updateStatus();
        await new Promise(resolve => setTimeout(resolve, 5000)); // Update every 5 seconds
      } catch (error) {
        console.error('Status update failed:', error);
      }
    }
  }

  private async updateStatus(): Promise<void> {
    const status = await this.lspClient.sendRequest('status/getAll');

    // Update each status item with data from Darklang
    this.items.get('instance')!.text = `$(server) ${status.instance.name}`;
    this.items.get('instance')!.tooltip = `Instance: ${status.instance.type} at ${status.instance.location}`;

    this.items.get('session')!.text = `$(target) ${status.session.name}`;
    this.items.get('session')!.tooltip = `Session: ${status.session.name}\nLast activity: ${status.session.lastActivity}`;

    if (status.patch) {
      this.items.get('patch')!.text = `$(git-branch) ${status.patch.intent}`;
      this.items.get('patch')!.tooltip = `Active patch: ${status.patch.intent}\nOps: ${status.patch.opCount}`;
    } else {
      this.items.get('patch')!.text = `$(git-branch) No active patch`;
      this.items.get('patch')!.tooltip = 'No active patch - create one to start making changes';
    }

    const syncIcon = status.sync.hasConflicts ? '$(warning)' : '$(sync)';
    this.items.get('sync')!.text = `${syncIcon} ${status.sync.summary}`;
    this.items.get('sync')!.tooltip = status.sync.details;
  }
}
```

### Darklang-Powered Logic
Status information computed in F#:

```fsharp
module LibLanguageServer.Status

type StatusInfo = {
  instance: InstanceStatus
  session: SessionStatus
  patch: PatchStatus option
  sync: SyncStatus
}

type InstanceStatus = {
  name: string
  type_: string
  location: string
  isConnected: bool
}

type SessionStatus = {
  name: string
  lastActivity: DateTime
  openFileCount: int
}

type SyncStatus = {
  summary: string
  details: string
  hasConflicts: bool
  lastSync: DateTime option
  pendingPushCount: int
  availablePullCount: int
}

let getAllStatus () : Task<StatusInfo> = task {
  let! instanceStatus = getInstanceStatus()
  let! sessionStatus = getSessionStatus()
  let! patchStatus = getCurrentPatchStatus()
  let! syncStatus = getSyncStatus()

  return {
    instance = instanceStatus
    session = sessionStatus
    patch = patchStatus
    sync = syncStatus
  }
}

let getInstanceStatus () : Task<InstanceStatus> = task {
  let! instance = InstanceManager.getCurrentInstance()
  return {
    name = instance.name
    type_ = instance.type_.ToString()
    location = InstanceLocation.toString instance.location
    isConnected = instance.isConnected
  }
}

let getSyncStatus () : Task<SyncStatus> = task {
  let! conflicts = ConflictDetector.getCurrentConflicts()
  let! pendingPushes = SyncManager.getPendingPushes()
  let! availablePulls = SyncManager.getAvailablePulls()
  let! lastSync = SyncManager.getLastSyncTime()

  let summary =
    match conflicts.Length, pendingPushes.Length, availablePulls.Length with
    | 0, 0, 0 -> "Up to date"
    | conflictCount, _, _ when conflictCount > 0 -> $"{conflictCount} conflicts"
    | 0, pushCount, 0 -> $"{pushCount} to push"
    | 0, 0, pullCount -> $"{pullCount} to pull"
    | 0, pushCount, pullCount -> $"{pushCount} to push, {pullCount} to pull"

  return {
    summary = summary
    details = generateSyncDetails conflicts pendingPushes availablePulls lastSync
    hasConflicts = conflicts.Length > 0
    lastSync = lastSync
    pendingPushCount = pendingPushes.Length
    availablePullCount = availablePulls.Length
  }
}
```

### Benefits
- **Real-time awareness**: Always shows current state of Darklang system
- **Clickable actions**: Each item leads to relevant management interface
- **Conflict visibility**: Immediate notification of sync conflicts
- **Minimal UI code**: Status computation handled in F#