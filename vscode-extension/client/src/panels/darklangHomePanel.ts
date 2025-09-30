import * as vscode from 'vscode';
import { HomeContentProvider } from '../providers/content/homeContentProvider';

/**
 * WebView panel for the Darklang home page with real clickable links
 */
export class DarklangHomePanel {
  public static currentPanel: DarklangHomePanel | undefined;
  public static readonly viewType = 'darklangHome';

  private readonly _panel: vscode.WebviewPanel;
  private readonly _extensionUri: vscode.Uri;
  private _disposables: vscode.Disposable[] = [];

  public static createOrShow(extensionUri: vscode.Uri) {
    const column = vscode.window.activeTextEditor
      ? vscode.window.activeTextEditor.viewColumn
      : undefined;

    // If we already have a panel, show it
    if (DarklangHomePanel.currentPanel) {
      DarklangHomePanel.currentPanel._panel.reveal(column);
      return;
    }

    // Otherwise, create a new panel
    const panel = vscode.window.createWebviewPanel(
      DarklangHomePanel.viewType,
      '🏠 Darklang Home',
      column || vscode.ViewColumn.One,
      {
        enableScripts: true,
        retainContextWhenHidden: true,
        localResourceRoots: [extensionUri]
      }
    );

    DarklangHomePanel.currentPanel = new DarklangHomePanel(panel, extensionUri);
  }

  public static revive(panel: vscode.WebviewPanel, extensionUri: vscode.Uri) {
    DarklangHomePanel.currentPanel = new DarklangHomePanel(panel, extensionUri);
  }

  private constructor(panel: vscode.WebviewPanel, extensionUri: vscode.Uri) {
    this._panel = panel;
    this._extensionUri = extensionUri;

    // Set the webview's initial html content
    this._update();

    // Listen for when the panel is disposed
    this._panel.onDidDispose(() => this.dispose(), null, this._disposables);

    // Handle messages from the webview
    this._panel.webview.onDidReceiveMessage(
      message => {
        switch (message.command) {
          case 'openUrl':
            this._openDarklangUrl(message.url);
            return;
          case 'openHome':
            // Handle opening home page (though this is already the home page)
            return;
        }
      },
      null,
      this._disposables
    );
  }

  private async _openDarklangUrl(url: string) {
    try {
      const uri = vscode.Uri.parse(url);
      const doc = await vscode.workspace.openTextDocument(uri);
      await vscode.window.showTextDocument(doc, {
        preview: false,
        preserveFocus: false,
        viewColumn: vscode.ViewColumn.Beside // Open in new column
      });
    } catch (error) {
      vscode.window.showErrorMessage(`Failed to open ${url}: ${error}`);
    }
  }

  public dispose() {
    DarklangHomePanel.currentPanel = undefined;

    // Clean up our resources
    this._panel.dispose();

    while (this._disposables.length) {
      const x = this._disposables.pop();
      if (x) {
        x.dispose();
      }
    }
  }

  private _update() {
    const webview = this._panel.webview;
    this._panel.title = '🏠 Darklang Home';
    this._panel.webview.html = this._getHtmlForWebview(webview);
  }

  private _getHtmlForWebview(webview: vscode.Webview) {
    // Get all the demo URLs
    const demoUrls = HomeContentProvider.getAllDemoUrls();

    // Group URLs by category
    const groupedUrls = demoUrls.reduce((groups, item) => {
      if (!groups[item.category]) {
        groups[item.category] = [];
      }
      groups[item.category].push(item);
      return groups;
    }, {} as Record<string, typeof demoUrls>);

    const categories = {
      'Sessions': { icon: '🏢', color: '#007ACC' },
      'Patches': { icon: '🔧', color: '#28A745' },
      'Packages': { icon: '📦', color: '#6F42C1' },
      'Instances': { icon: '🖥️', color: '#FD7E14' },
      'Editing': { icon: '✏️', color: '#FFC107' },
      'History': { icon: '📜', color: '#6F42C1' },
      'Config': { icon: '⚙️', color: '#6C757D' }
    };

    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Darklang Home</title>
    <style>
        body {
            font-family: var(--vscode-font-family);
            font-size: var(--vscode-font-size);
            font-weight: var(--vscode-font-weight);
            line-height: 1.6;
            color: var(--vscode-foreground);
            background-color: var(--vscode-editor-background);
            padding: 20px;
            margin: 0;
        }

        .header {
            text-align: center;
            margin-bottom: 40px;
            padding: 20px;
            border-radius: 8px;
            background: linear-gradient(135deg,
                var(--vscode-button-background) 0%,
                var(--vscode-button-hoverBackground) 100%);
        }

        .header h1 {
            margin: 0;
            font-size: 2.5em;
            color: var(--vscode-button-foreground);
        }

        .subtitle {
            margin: 10px 0 0 0;
            opacity: 0.9;
            font-size: 1.1em;
            color: var(--vscode-button-foreground);
        }

        .categories {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(350px, 1fr));
            gap: 24px;
            margin-bottom: 40px;
        }

        .category {
            background: var(--vscode-editor-widget-background);
            border: 1px solid var(--vscode-widget-border);
            border-radius: 8px;
            padding: 20px;
            transition: all 0.2s ease;
        }

        .category:hover {
            border-color: var(--vscode-focusBorder);
            box-shadow: 0 2px 8px rgba(0,0,0,0.1);
        }

        .category-header {
            display: flex;
            align-items: center;
            margin-bottom: 16px;
            font-size: 1.3em;
            font-weight: 600;
        }

        .category-icon {
            margin-right: 12px;
            font-size: 1.4em;
        }

        .category-links {
            display: flex;
            flex-direction: column;
            gap: 8px;
        }

        .link-item {
            display: flex;
            align-items: center;
            padding: 8px 12px;
            border-radius: 4px;
            text-decoration: none;
            color: var(--vscode-textLink-foreground);
            background: var(--vscode-input-background);
            border: 1px solid var(--vscode-input-border);
            transition: all 0.2s ease;
            cursor: pointer;
        }

        .link-item:hover {
            background: var(--vscode-list-hoverBackground);
            border-color: var(--vscode-focusBorder);
            transform: translateX(4px);
        }

        .link-icon {
            margin-right: 10px;
            font-size: 1.1em;
            min-width: 20px;
        }

        .link-content {
            flex: 1;
        }

        .link-name {
            font-weight: 500;
            margin-bottom: 2px;
        }

        .link-description {
            font-size: 0.9em;
            opacity: 0.8;
            color: var(--vscode-descriptionForeground);
        }

        .quick-actions {
            margin-top: 40px;
            padding: 20px;
            background: var(--vscode-textBlockQuote-background);
            border-left: 4px solid var(--vscode-textBlockQuote-border);
            border-radius: 4px;
        }

        .quick-actions h3 {
            margin-top: 0;
            color: var(--vscode-textPreformat-foreground);
        }

        .workflow {
            margin: 16px 0;
            padding: 12px;
            background: var(--vscode-editor-background);
            border-radius: 4px;
        }

        .workflow-title {
            font-weight: 600;
            margin-bottom: 8px;
            color: var(--vscode-textPreformat-foreground);
        }

        .workflow-steps {
            display: flex;
            flex-wrap: wrap;
            gap: 8px;
            align-items: center;
        }

        .workflow-step {
            background: var(--vscode-button-secondaryBackground);
            color: var(--vscode-button-secondaryForeground);
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 0.9em;
            cursor: pointer;
            transition: background 0.2s ease;
        }

        .workflow-step:hover {
            background: var(--vscode-button-secondaryHoverBackground);
        }

        .arrow {
            color: var(--vscode-descriptionForeground);
            font-weight: bold;
        }

        .tips {
            margin-top: 30px;
            padding: 16px;
            background: var(--vscode-editor-widget-background);
            border-radius: 4px;
            border: 1px solid var(--vscode-widget-border);
        }

        .tips-title {
            font-weight: 600;
            margin-bottom: 8px;
            color: var(--vscode-textPreformat-foreground);
        }

        .tip {
            margin: 8px 0;
            color: var(--vscode-descriptionForeground);
        }
    </style>
</head>
<body>
    <div class="header">
        <h1>🌑 Darklang VS Code Extension</h1>
        <p class="subtitle">Your central hub for Darklang development</p>
    </div>

    <div class="categories">
        ${Object.entries(groupedUrls).map(([categoryName, items]) => {
          const categoryInfo = categories[categoryName as keyof typeof categories] || { icon: '📄', color: '#666' };
          return `
            <div class="category">
                <div class="category-header">
                    <span class="category-icon">${categoryInfo.icon}</span>
                    ${categoryName}
                </div>
                <div class="category-links">
                    ${items.map(item => {
                      // Extract badge from name or use category icon
                      const badge = item.name.match(/^(🏢|⚡|⚠️|🧪|🔬|ℹ️|✏️|📦|📁|🌐|🏪|📂|🔗|📋|📝|📜|🔍|⚙️)/)?.[0] || categoryInfo.icon;
                      const cleanName = item.name.replace(/^(🏢|⚡|⚠️|🧪|🔬|ℹ️|✏️|📦|📁|🌐|🏪|📂|🔗|📋|📝|📜|🔍|⚙️)\s*/, '');

                      return `
                        <div class="link-item" onclick="openDarklangUrl('${item.url}')">
                            <span class="link-icon">${badge}</span>
                            <div class="link-content">
                                <div class="link-name">${cleanName}</div>
                                <div class="link-description">${item.description}</div>
                            </div>
                        </div>
                      `;
                    }).join('')}
                </div>
            </div>
          `;
        }).join('')}
    </div>

    <div class="quick-actions">
        <h3>🎯 Quick Development Workflows</h3>

        <div class="workflow">
            <div class="workflow-title">📝 Feature Development</div>
            <div class="workflow-steps">
                <span class="workflow-step" onclick="openDarklangUrl('dark:///session/new-feature')">New Session</span>
                <span class="arrow">→</span>
                <span class="workflow-step" onclick="openDarklangUrl('dark:///patch/new')">Create Patch</span>
                <span class="arrow">→</span>
                <span class="workflow-step" onclick="openDarklangUrl('dark:///edit/current-patch/MyApp.NewFeature')">Edit Code</span>
                <span class="arrow">→</span>
                <span class="workflow-step" onclick="openDarklangUrl('dark:///patch/current/tests')">Run Tests</span>
            </div>
        </div>

        <div class="workflow">
            <div class="workflow-title">🔍 Code Review</div>
            <div class="workflow-steps">
                <span class="workflow-step" onclick="openDarklangUrl('dark:///patch/abc123/operations')">View Operations</span>
                <span class="arrow">→</span>
                <span class="workflow-step" onclick="openDarklangUrl('dark:///patch/abc123/conflicts')">Check Conflicts</span>
                <span class="arrow">→</span>
                <span class="workflow-step" onclick="openDarklangUrl('dark:///compare/current/main')">Compare Changes</span>
            </div>
        </div>

        <div class="workflow">
            <div class="workflow-title">📦 Package Exploration</div>
            <div class="workflow-steps">
                <span class="workflow-step" onclick="openDarklangUrl('dark:///package/Darklang.Stdlib?view=graph')">Browse Stdlib</span>
                <span class="arrow">→</span>
                <span class="workflow-step" onclick="openDarklangUrl('dark:///instance/registry')">Package Registry</span>
                <span class="arrow">→</span>
                <span class="workflow-step" onclick="openDarklangUrl('dark:///draft/MyPackage.NewModule')">Create Package</span>
            </div>
        </div>
    </div>

    <div class="tips">
        <div class="tips-title">💡 Pro Tips</div>
        <div class="tip">🖱️ Links open in new editor tabs beside this home page</div>
        <div class="tip">🎨 Each URL type has its own color theme and badge for easy identification</div>
        <div class="tip">🏠 Access this home page anytime via Command Palette → "Open Darklang Home Page"</div>
        <div class="tip">🔗 Home page URL: <code>dark:///home</code></div>
    </div>

    <script>
        const vscode = acquireVsCodeApi();

        function openDarklangUrl(url) {
            // Send message to VS Code extension
            vscode.postMessage({
                command: 'openUrl',
                url: url
            });
        }

        // Legacy function name for any remaining references
        function openUrl(url) {
            openDarklangUrl(url);
        }
    </script>
</body>
</html>`;
  }
}