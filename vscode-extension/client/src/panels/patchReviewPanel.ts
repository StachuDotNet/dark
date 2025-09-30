import * as vscode from "vscode";

export class PatchReviewPanel {
  public static currentPanel: PatchReviewPanel | undefined;
  private readonly _panel: vscode.WebviewPanel;
  private _disposables: vscode.Disposable[] = [];

  public static createOrShow(extensionUri: vscode.Uri, patchId: string) {
    const column = vscode.window.activeTextEditor
      ? vscode.window.activeTextEditor.viewColumn
      : undefined;

    if (PatchReviewPanel.currentPanel) {
      PatchReviewPanel.currentPanel._panel.reveal(column);
      PatchReviewPanel.currentPanel.updatePatch(patchId);
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      "patchReview",
      "Patch Review",
      column || vscode.ViewColumn.One,
      {
        enableScripts: true,
        localResourceRoots: [vscode.Uri.joinPath(extensionUri, "media")]
      }
    );

    PatchReviewPanel.currentPanel = new PatchReviewPanel(panel, extensionUri, patchId);
  }

  private constructor(panel: vscode.WebviewPanel, extensionUri: vscode.Uri, patchId: string) {
    this._panel = panel;
    this.updatePatch(patchId);
    this._panel.onDidDispose(() => this.dispose(), null, this._disposables);
    this._panel.webview.onDidReceiveMessage(
      message => {
        switch (message.command) {
          case "approve":
            vscode.window.showInformationMessage(`Patch ${patchId} approved!`);
            return;
          case "reject":
            vscode.window.showWarningMessage(`Patch ${patchId} rejected.`);
            return;
          case "requestChanges":
            vscode.window.showInformationMessage(`Changes requested for patch ${patchId}.`);
            return;
        }
      },
      null,
      this._disposables
    );
  }

  private updatePatch(patchId: string) {
    this._panel.title = `Patch Review: ${patchId}`;
    this._panel.webview.html = this.getHtmlForWebview(patchId);
  }

  private getHtmlForWebview(patchId: string): string {
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Patch Review</title>
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
        .patch-info {
            background-color: var(--vscode-textBlockQuote-background);
            border-left: 4px solid var(--vscode-textBlockQuote-border);
            padding: 15px;
            margin-bottom: 20px;
        }
        .operations {
            margin-bottom: 30px;
        }
        .operation {
            background-color: var(--vscode-list-hoverBackground);
            border-radius: 4px;
            padding: 15px;
            margin-bottom: 10px;
            border-left: 4px solid var(--vscode-button-background);
        }
        .diff-viewer {
            background-color: var(--vscode-diffEditor-insertedTextBackground);
            border: 1px solid var(--vscode-panel-border);
            border-radius: 4px;
            padding: 15px;
            margin: 10px 0;
            font-family: var(--vscode-editor-font-family);
            white-space: pre-wrap;
        }
        .actions {
            position: fixed;
            bottom: 20px;
            right: 20px;
            display: flex;
            gap: 10px;
        }
        .btn {
            padding: 8px 16px;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            font-family: var(--vscode-font-family);
        }
        .btn-approve {
            background-color: var(--vscode-button-background);
            color: var(--vscode-button-foreground);
        }
        .btn-changes {
            background-color: var(--vscode-button-secondaryBackground);
            color: var(--vscode-button-secondaryForeground);
        }
        .btn-reject {
            background-color: var(--vscode-errorButton-background);
            color: var(--vscode-errorButton-foreground);
        }
        .test-results {
            background-color: var(--vscode-terminal-background);
            border: 1px solid var(--vscode-panel-border);
            border-radius: 4px;
            padding: 15px;
            margin: 10px 0;
        }
        .status-badge {
            display: inline-block;
            padding: 2px 8px;
            border-radius: 12px;
            font-size: 12px;
            font-weight: bold;
        }
        .status-pass {
            background-color: var(--vscode-testing-iconPassed);
            color: white;
        }
        .status-draft {
            background-color: var(--vscode-button-background);
            color: var(--vscode-button-foreground);
        }
    </style>
</head>
<body>
    <div class="header">
        <h1>🔍 Patch Review: ${patchId}</h1>
        <span class="status-badge status-draft">DRAFT</span>
        <span class="status-badge status-pass">TESTS PASSING</span>
    </div>

    <div class="patch-info">
        <h3>📝 Patch Information</h3>
        <p><strong>Intent:</strong> Add user validation to authentication system</p>
        <p><strong>Author:</strong> stachu</p>
        <p><strong>Created:</strong> 2024-01-15 14:30:00</p>
        <p><strong>Operations:</strong> 3 (1 create, 1 modify, 1 new type)</p>
        <p><strong>Impact:</strong> Authentication module, User management</p>
    </div>

    <div class="operations">
        <h3>🔧 Operations</h3>

        <div class="operation">
            <h4>CREATE: MyApp.User.validate</h4>
            <p>New validation function with comprehensive checks</p>
            <div class="diff-viewer">
+ let validate (user: MyApp.User.UserInput): Result<MyApp.User.User, List<ValidationError>> =
+   let errors = []
+
+   // Email validation
+   let emailErrors =
+     if Stdlib.String.isEmpty user.email then
+       [MissingField "email"]
+     elif not (Stdlib.String.contains user.email "@") then
+       [InvalidEmail]
+     else
+       []
+
+   // Password validation
+   let passwordErrors =
+     if Stdlib.String.isEmpty user.password then
+       [MissingField "password"]
+     elif Stdlib.String.length user.password < 8L then
+       [WeakPassword]
+     else
+       []
            </div>
        </div>

        <div class="operation">
            <h4>CREATE: MyApp.User.ValidationError</h4>
            <p>New error type for validation failures</p>
            <div class="diff-viewer">
+ type ValidationError =
+   | InvalidEmail
+   | WeakPassword
+   | MissingField of String
            </div>
        </div>

        <div class="operation">
            <h4>MODIFY: MyApp.User.create</h4>
            <p>Updated to use new validation function</p>
            <div class="diff-viewer">
  let create (input: UserInput): Result<User, String> =
-   // TODO: Add validation
-   Ok {
-     id = Stdlib.Uuid.generate ()
-     email = input.email
-     hashedPassword = MyApp.Auth.hashPassword input.password
-     createdAt = Stdlib.DateTime.now ()
-   }
+   match MyApp.User.validate input with
+   | Ok user -> Ok user
+   | Error errors ->
+     let errorMsg =
+       errors
+       |> Stdlib.List.map (fun err ->
+         match err with
+         | InvalidEmail -> "Invalid email format"
+         | WeakPassword -> "Password too weak"
+         | MissingField field -> field ++ " is required"
+       )
+       |> Stdlib.String.join ", "
+     Error errorMsg
            </div>
        </div>
    </div>

    <div class="test-results">
        <h3>🧪 Test Results</h3>
        <p><strong>Total Tests:</strong> 12</p>
        <p><strong>Passing:</strong> 12 ✅</p>
        <p><strong>Failing:</strong> 0 ❌</p>
        <p><strong>Coverage:</strong> 95%</p>

        <details>
            <summary>Test Details</summary>
            <ul>
                <li>✅ validate_empty_email_returns_error</li>
                <li>✅ validate_invalid_email_returns_error</li>
                <li>✅ validate_weak_password_returns_error</li>
                <li>✅ validate_missing_fields_returns_errors</li>
                <li>✅ validate_valid_user_returns_ok</li>
                <li>✅ create_with_valid_input_succeeds</li>
                <li>✅ create_with_invalid_input_fails</li>
                <li>✅ validation_error_messages_are_clear</li>
                <li>✅ multiple_validation_errors_combined</li>
                <li>✅ password_strength_requirements</li>
                <li>✅ email_format_edge_cases</li>
                <li>✅ integration_with_auth_system</li>
            </ul>
        </details>
    </div>

    <div class="actions">
        <button class="btn btn-approve" onclick="approve()">✅ Approve</button>
        <button class="btn btn-changes" onclick="requestChanges()">💭 Request Changes</button>
        <button class="btn btn-reject" onclick="reject()">❌ Reject</button>
    </div>

    <script>
        const vscode = acquireVsCodeApi();

        function approve() {
            vscode.postMessage({
                command: 'approve'
            });
        }

        function requestChanges() {
            vscode.postMessage({
                command: 'requestChanges'
            });
        }

        function reject() {
            vscode.postMessage({
                command: 'reject'
            });
        }
    </script>
</body>
</html>`;
  }

  public dispose() {
    PatchReviewPanel.currentPanel = undefined;
    this._panel.dispose();
    while (this._disposables.length) {
      const x = this._disposables.pop();
      if (x) {
        x.dispose();
      }
    }
  }
}