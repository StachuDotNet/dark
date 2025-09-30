import * as vscode from "vscode";

export class ConflictResolutionPanel {
  public static currentPanel: ConflictResolutionPanel | undefined;
  private readonly _panel: vscode.WebviewPanel;
  private _disposables: vscode.Disposable[] = [];

  public static createOrShow(extensionUri: vscode.Uri, conflictId: string) {
    const column = vscode.window.activeTextEditor
      ? vscode.window.activeTextEditor.viewColumn
      : undefined;

    if (ConflictResolutionPanel.currentPanel) {
      ConflictResolutionPanel.currentPanel._panel.reveal(column);
      ConflictResolutionPanel.currentPanel.updateConflict(conflictId);
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      "conflictResolution",
      "Resolve Conflicts",
      column || vscode.ViewColumn.One,
      {
        enableScripts: true,
        localResourceRoots: [vscode.Uri.joinPath(extensionUri, "media")]
      }
    );

    ConflictResolutionPanel.currentPanel = new ConflictResolutionPanel(panel, extensionUri, conflictId);
  }

  private constructor(panel: vscode.WebviewPanel, extensionUri: vscode.Uri, conflictId: string) {
    this._panel = panel;
    this.updateConflict(conflictId);
    this._panel.onDidDispose(() => this.dispose(), null, this._disposables);
    this._panel.webview.onDidReceiveMessage(
      message => {
        switch (message.command) {
          case "keepLocal":
            vscode.window.showInformationMessage(`Kept local version for ${conflictId}`);
            this.dispose();
            return;
          case "keepRemote":
            vscode.window.showInformationMessage(`Kept remote version for ${conflictId}`);
            this.dispose();
            return;
          case "merge":
            vscode.window.showInformationMessage(`Merged changes for ${conflictId}`);
            this.dispose();
            return;
          case "rename":
            vscode.window.showInformationMessage(`Created new version for ${conflictId}`);
            this.dispose();
            return;
        }
      },
      null,
      this._disposables
    );
  }

  private updateConflict(conflictId: string) {
    this._panel.title = `Resolve Conflict: ${conflictId}`;
    this._panel.webview.html = this.getHtmlForWebview(conflictId);
  }

  private getHtmlForWebview(conflictId: string): string {
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Conflict Resolution</title>
    <style>
        body {
            font-family: var(--vscode-font-family);
            font-size: var(--vscode-font-size);
            background-color: var(--vscode-editor-background);
            color: var(--vscode-editor-foreground);
            margin: 0;
            padding: 20px;
        }
        .header {
            border-bottom: 1px solid var(--vscode-panel-border);
            padding-bottom: 20px;
            margin-bottom: 20px;
        }
        .conflict-info {
            background-color: var(--vscode-inputValidation-warningBackground);
            border-left: 4px solid var(--vscode-inputValidation-warningBorder);
            padding: 15px;
            margin-bottom: 20px;
        }
        .versions {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 20px;
            margin-bottom: 30px;
        }
        .version {
            background-color: var(--vscode-list-hoverBackground);
            border-radius: 4px;
            padding: 15px;
        }
        .version-local {
            border-left: 4px solid var(--vscode-gitDecoration-modifiedResourceForeground);
        }
        .version-remote {
            border-left: 4px solid var(--vscode-gitDecoration-addedResourceForeground);
        }
        .code-block {
            background-color: var(--vscode-textCodeBlock-background);
            border: 1px solid var(--vscode-panel-border);
            border-radius: 4px;
            padding: 15px;
            margin: 10px 0;
            font-family: var(--vscode-editor-font-family);
            white-space: pre-wrap;
            font-size: 14px;
        }
        .resolution-options {
            background-color: var(--vscode-textBlockQuote-background);
            border-radius: 4px;
            padding: 20px;
            margin-bottom: 20px;
        }
        .option-btn {
            display: block;
            width: 100%;
            padding: 12px;
            margin: 8px 0;
            border: 1px solid var(--vscode-button-border);
            border-radius: 4px;
            background-color: var(--vscode-button-secondaryBackground);
            color: var(--vscode-button-secondaryForeground);
            cursor: pointer;
            font-family: var(--vscode-font-family);
            text-align: left;
        }
        .option-btn:hover {
            background-color: var(--vscode-button-hoverBackground);
        }
        .option-btn.primary {
            background-color: var(--vscode-button-background);
            color: var(--vscode-button-foreground);
        }
        .merge-preview {
            background-color: var(--vscode-diffEditor-insertedTextBackground);
            border-radius: 4px;
            padding: 15px;
            margin: 15px 0;
        }
    </style>
</head>
<body>
    <div class="header">
        <h1>⚔️ Resolve Conflict: MyApp.User.update</h1>
        <p>Function signature and implementation conflicts detected</p>
    </div>

    <div class="conflict-info">
        <h3>🚫 Conflict Details</h3>
        <p><strong>Local Change:</strong> Alice modified function signature for validation improvements</p>
        <p><strong>Remote Change:</strong> Bob added profile fields and update logic</p>
        <p><strong>Conflict:</strong> Both changes modify the same function but in incompatible ways</p>
    </div>

    <div class="versions">
        <div class="version version-local">
            <h3>🏠 Local Version (Alice's changes)</h3>
            <p><strong>Intent:</strong> Add validation to user updates</p>
            <div class="code-block">
let update (userId: Uuid) (updates: UserUpdates): Result<User, ValidationError> =
  // Validate updates before applying
  match MyApp.User.validateUpdates updates with
  | Error validationErrors -> Error validationErrors
  | Ok validatedUpdates ->
    match MyApp.Database.User.findById userId with
    | None -> Error (UserNotFound userId)
    | Some user ->
      let updatedUser = {
        user with
        email = validatedUpdates.email |> Option.defaultValue user.email
        updatedAt = Stdlib.DateTime.now ()
      }
      MyApp.Database.User.save updatedUser
            </div>
        </div>

        <div class="version version-remote">
            <h3>🌐 Remote Version (Bob's changes)</h3>
            <p><strong>Intent:</strong> Add profile fields support</p>
            <div class="code-block">
let update (userId: Uuid) (updates: UserUpdates): Result<User, String> =
  match MyApp.Database.User.findById userId with
  | None -> Error ("User not found: " ++ Stdlib.Uuid.toString userId)
  | Some user ->
    let updatedUser = {
      user with
      email = updates.email |> Option.defaultValue user.email
      profile = updates.profile |> Option.defaultValue user.profile
      preferences = updates.preferences |> Option.defaultValue user.preferences
      updatedAt = Stdlib.DateTime.now ()
    }
    MyApp.Database.User.save updatedUser
            </div>
        </div>
    </div>

    <div class="resolution-options">
        <h3>🔧 Resolution Options</h3>

        <button class="option-btn" onclick="keepLocal()">
            <strong>Keep Local Version</strong><br>
            Use Alice's validation-focused approach. Profile fields will be lost.
        </button>

        <button class="option-btn" onclick="keepRemote()">
            <strong>Keep Remote Version</strong><br>
            Use Bob's profile fields approach. Validation improvements will be lost.
        </button>

        <button class="option-btn primary" onclick="merge()">
            <strong>🎯 Smart Merge (Recommended)</strong><br>
            Combine both approaches: validation + profile fields support.
        </button>

        <button class="option-btn" onclick="rename()">
            <strong>Create New Function</strong><br>
            Keep both versions by creating updateWithValidation and updateProfile functions.
        </button>
    </div>

    <div class="merge-preview">
        <h3>📋 Merge Preview</h3>
        <p>Smart merge will combine both changes:</p>
        <div class="code-block">
let update (userId: Uuid) (updates: UserUpdates): Result<User, ValidationError> =
  // Validate updates before applying (from Alice)
  match MyApp.User.validateUpdates updates with
  | Error validationErrors -> Error validationErrors
  | Ok validatedUpdates ->
    match MyApp.Database.User.findById userId with
    | None -> Error (UserNotFound userId)
    | Some user ->
      let updatedUser = {
        user with
        email = validatedUpdates.email |> Option.defaultValue user.email
        profile = validatedUpdates.profile |> Option.defaultValue user.profile      // from Bob
        preferences = validatedUpdates.preferences |> Option.defaultValue user.preferences  // from Bob
        updatedAt = Stdlib.DateTime.now ()
      }
      MyApp.Database.User.save updatedUser
        </div>
    </div>

    <script>
        const vscode = acquireVsCodeApi();

        function keepLocal() {
            vscode.postMessage({
                command: 'keepLocal'
            });
        }

        function keepRemote() {
            vscode.postMessage({
                command: 'keepRemote'
            });
        }

        function merge() {
            vscode.postMessage({
                command: 'merge'
            });
        }

        function rename() {
            vscode.postMessage({
                command: 'rename'
            });
        }
    </script>
</body>
</html>`;
  }

  public dispose() {
    ConflictResolutionPanel.currentPanel = undefined;
    this._panel.dispose();
    while (this._disposables.length) {
      const x = this._disposables.pop();
      if (x) {
        x.dispose();
      }
    }
  }
}