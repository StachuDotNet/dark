import * as vscode from 'vscode';

/**
 * Simple welcome view provider that shows a home button
 */
export class WelcomeViewProvider implements vscode.WebviewViewProvider {
  public static readonly viewType = 'darklangWelcome';

  constructor(private readonly _extensionUri: vscode.Uri) {}

  public resolveWebviewView(
    webviewView: vscode.WebviewView,
    context: vscode.WebviewViewResolveContext,
    _token: vscode.CancellationToken
  ) {
    webviewView.webview.options = {
      enableScripts: true,
      localResourceRoots: [this._extensionUri]
    };

    webviewView.webview.html = this._getHtmlForWebview(webviewView.webview);

    webviewView.webview.onDidReceiveMessage(async data => {
      switch (data.type) {
        case 'openHome':
          vscode.commands.executeCommand('darklang.openHome');
          break;
        case 'openUrl':
          try {
            const uri = vscode.Uri.parse(data.url);
            const doc = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(doc, {
              preview: false,
              preserveFocus: false
            });
          } catch (error) {
            vscode.window.showErrorMessage(`Failed to open ${data.url}: ${error}`);
          }
          break;
      }
    });
  }

  private _getHtmlForWebview(webview: vscode.Webview) {
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Darklang Welcome</title>
    <style>
        body {
            font-family: var(--vscode-font-family);
            font-size: var(--vscode-font-size);
            color: var(--vscode-foreground);
            background-color: var(--vscode-editor-background);
            padding: 8px;
            margin: 0;
            text-align: center;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
        }

        .welcome-content {
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 12px;
            flex: 1;
            justify-content: flex-start;
        }

        .logo {
            font-size: 2em;
            margin-bottom: 4px;
        }

        .title {
            font-size: 1em;
            font-weight: 600;
            color: var(--vscode-textPreformat-foreground);
            margin-bottom: 2px;
        }

        .subtitle {
            font-size: 0.8em;
            color: var(--vscode-descriptionForeground);
            margin-bottom: 12px;
        }

        .home-button {
            background: var(--vscode-button-background);
            color: var(--vscode-button-foreground);
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            font-size: 0.9em;
            font-weight: 500;
            cursor: pointer;
            transition: background 0.2s ease;
            width: 100%;
            max-width: 200px;
        }

        .home-button:hover {
            background: var(--vscode-button-hoverBackground);
        }

        .home-button:active {
            background: var(--vscode-button-background);
            transform: scale(0.98);
        }

        .quick-links {
            margin-top: 8px;
            text-align: left;
            width: 100%;
        }

        .quick-links-title {
            font-size: 0.8em;
            font-weight: 600;
            color: var(--vscode-textPreformat-foreground);
            margin-bottom: 4px;
        }

        .quick-link {
            display: block;
            color: var(--vscode-textLink-foreground);
            text-decoration: none;
            padding: 2px 0;
            font-size: 0.8em;
            cursor: pointer;
            line-height: 1.3;
        }

        .quick-link:hover {
            color: var(--vscode-textLink-activeForeground);
        }

        .shortcut {
            background: var(--vscode-badge-background);
            color: var(--vscode-badge-foreground);
            padding: 2px 6px;
            border-radius: 3px;
            font-size: 0.75em;
            margin-top: 4px;
        }
    </style>
</head>
<body>
    <div class="welcome-content">
        <div class="logo">🌑</div>
        <div class="title">Darklang</div>
        <div class="subtitle">Development Environment</div>

        <button class="home-button" onclick="openHome()">
            🏠 Open Home Page
        </button>

        <div class="shortcut">
            Ctrl+Shift+H (Cmd+Shift+H on Mac)
        </div>

        <div class="quick-links">
            <div class="quick-links-title">Quick Access URL Patterns:</div>
            <div class="quick-link" onclick="openDarklangUrl('dark:///session/feature-auth')">
                🏢 Sessions - dark:///session/{name}
            </div>
            <div class="quick-link" onclick="openDarklangUrl('dark:///patch/abc123')">
                🔧 Patches - dark:///patch/{id}
            </div>
            <div class="quick-link" onclick="openDarklangUrl('dark:///package/Darklang.Stdlib.List.map')">
                📦 Packages - dark:///package/{path}
            </div>
            <div class="quick-link" onclick="openDarklangUrl('dark:///instance/local')">
                🖥️ Instances - dark:///instance/{name}
            </div>
            <div class="quick-link" onclick="openDarklangUrl('dark:///config')">
                ⚙️ Config - dark:///config
            </div>
        </div>
    </div>

    <script>
        const vscode = acquireVsCodeApi();

        function openHome() {
            vscode.postMessage({ type: 'openHome' });
        }

        function openDarklangUrl(url) {
            vscode.postMessage({ type: 'openUrl', url: url });
        }

        // Legacy function name for compatibility
        function openUrl(url) {
            openDarklangUrl(url);
        }
    </script>
</body>
</html>`;
  }
}