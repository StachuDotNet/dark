import * as vscode from 'vscode';
import { ScenarioManager } from '../../data/scenarioManager';

/**
 * Current view provider that shows current instance and session status
 */
export class CurrentViewProvider implements vscode.WebviewViewProvider {
  public static readonly viewType = 'darklangCurrent';
  private _view?: vscode.WebviewView;

  constructor(private readonly _extensionUri: vscode.Uri) {
    // Listen for scenario changes to update view
    ScenarioManager.getInstance().onScenarioChanged(() => {
      this._updateView();
    });
  }

  public resolveWebviewView(
    webviewView: vscode.WebviewView,
    context: vscode.WebviewViewResolveContext,
    _token: vscode.CancellationToken
  ) {
    this._view = webviewView;

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
        case 'switchInstance':
          vscode.commands.executeCommand('darklang.instance.switch', data.instanceId);
          break;
        case 'switchSession':
          vscode.commands.executeCommand('darklang.session.switch', data.sessionId);
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

  private _updateView() {
    if (this._view) {
      this._view.webview.html = this._getHtmlForWebview(this._view.webview);
    }
  }

  private _getHtmlForWebview(webview: vscode.Webview) {
    const scenarioManager = ScenarioManager.getInstance();
    const currentScenario = scenarioManager.currentScenario;

    // Get current instance and session data
    const currentInstance = this._getCurrentInstance();
    const currentSession = this._getCurrentSession(currentScenario);
    const availableInstances = this._getAvailableInstances();
    const availableSessions = this._getAvailableSessions(currentScenario);

    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Current Context</title>
    <style>
        body {
            font-family: var(--vscode-font-family);
            font-size: var(--vscode-font-size);
            color: var(--vscode-foreground);
            background-color: var(--vscode-editor-background);
            padding: 8px;
            margin: 0;
        }

        .section {
            margin-bottom: 16px;
            border: 1px solid var(--vscode-panel-border);
            border-radius: 4px;
            padding: 8px;
        }

        .section-header {
            font-weight: 600;
            font-size: 0.9em;
            color: var(--vscode-textPreformat-foreground);
            margin-bottom: 8px;
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .current-item {
            display: flex;
            align-items: center;
            gap: 8px;
            padding: 4px 8px;
            background: var(--vscode-editor-selectionBackground);
            border-radius: 3px;
            margin-bottom: 4px;
        }

        .current-label {
            flex: 1;
            font-weight: 500;
        }

        .current-status {
            font-size: 0.8em;
            padding: 2px 6px;
            background: var(--vscode-badge-background);
            color: var(--vscode-badge-foreground);
            border-radius: 3px;
        }

        .selector {
            margin-top: 8px;
        }

        .selector-label {
            font-size: 0.85em;
            color: var(--vscode-descriptionForeground);
            margin-bottom: 4px;
        }

        .selector-dropdown {
            width: 100%;
            padding: 4px;
            background: var(--vscode-input-background);
            color: var(--vscode-input-foreground);
            border: 1px solid var(--vscode-input-border);
            border-radius: 3px;
            font-size: 0.9em;
        }

        .action-buttons {
            display: flex;
            gap: 8px;
            margin-top: 12px;
        }

        .action-button {
            flex: 1;
            background: var(--vscode-button-background);
            color: var(--vscode-button-foreground);
            border: none;
            padding: 6px 12px;
            border-radius: 3px;
            font-size: 0.85em;
            cursor: pointer;
            transition: background 0.2s ease;
        }

        .action-button:hover {
            background: var(--vscode-button-hoverBackground);
        }

        .info-grid {
            display: grid;
            grid-template-columns: auto 1fr;
            gap: 4px;
            font-size: 0.85em;
            margin-top: 8px;
        }

        .info-label {
            color: var(--vscode-descriptionForeground);
        }

        .info-value {
            font-weight: 500;
        }
    </style>
</head>
<body>
    <!-- Current Instance Section -->
    <div class="section">
        <div class="section-header">
            <span>📍</span>
            <span>Current Instance</span>
        </div>
        <div class="current-item">
            <span class="current-label">${currentInstance.label}</span>
            <span class="current-status">${currentInstance.status}</span>
        </div>
        <div class="info-grid">
            <span class="info-label">Type:</span>
            <span class="info-value">${currentInstance.type}</span>
            <span class="info-label">Packages:</span>
            <span class="info-value">${currentInstance.packageCount}</span>
            <span class="info-label">Path:</span>
            <span class="info-value">${currentInstance.path || 'N/A'}</span>
        </div>
        <div class="selector">
            <div class="selector-label">Switch to:</div>
            <select class="selector-dropdown" onchange="switchInstance(this.value)">
                <option value="">-- Select Instance --</option>
                ${availableInstances.map(inst =>
                  `<option value="${inst.id}">${inst.label}</option>`
                ).join('')}
            </select>
        </div>
    </div>

    <!-- Current Session Section -->
    <div class="section">
        <div class="section-header">
            <span>🎯</span>
            <span>Current Session</span>
        </div>
        <div class="current-item">
            <span class="current-label">${currentSession.label}</span>
            <span class="current-status">${currentSession.patchCount} patches</span>
        </div>
        <div class="info-grid">
            <span class="info-label">Intent:</span>
            <span class="info-value">${currentSession.intent}</span>
            <span class="info-label">Status:</span>
            <span class="info-value">${currentSession.status}</span>
        </div>
        <div class="selector">
            <div class="selector-label">Switch to:</div>
            <select class="selector-dropdown" onchange="switchSession(this.value)">
                <option value="">-- Select Session --</option>
                ${availableSessions.map(sess =>
                  `<option value="${sess.id}">${sess.label}</option>`
                ).join('')}
            </select>
        </div>
    </div>

    <!-- Quick Actions -->
    <div class="action-buttons">
        <button class="action-button" onclick="openHome()">
            🏠 Home
        </button>
        <button class="action-button" onclick="openUrl('dark:///patch/current')">
            🔧 Patches
        </button>
        <button class="action-button" onclick="openUrl('dark:///package/Darklang.Stdlib')">
            📦 Packages
        </button>
    </div>

    <script>
        const vscode = acquireVsCodeApi();

        function openHome() {
            vscode.postMessage({ type: 'openHome' });
        }

        function switchInstance(instanceId) {
            if (instanceId) {
                vscode.postMessage({ type: 'switchInstance', instanceId: instanceId });
            }
        }

        function switchSession(sessionId) {
            if (sessionId) {
                vscode.postMessage({ type: 'switchSession', sessionId: sessionId });
            }
        }

        function openUrl(url) {
            vscode.postMessage({ type: 'openUrl', url: url });
        }
    </script>
</body>
</html>`;
  }

  private _getCurrentInstance() {
    // Mock data - in real implementation, get from extension state
    return {
      id: 'local',
      label: 'Local (/home/stachu/code/dark)',
      type: 'Local Development',
      status: 'connected',
      packageCount: 47,
      path: '/home/stachu/code/dark'
    };
  }

  private _getCurrentSession(scenario: string) {
    // Mock data based on scenario
    const sessionMap: Record<string, any> = {
      'clean-start': {
        id: 'clean-start',
        label: 'clean-start',
        intent: 'New project setup',
        status: 'active',
        patchCount: 0
      },
      'active-development': {
        id: 'stdlib-dev',
        label: 'stdlib-dev',
        intent: 'Add List.filterMap function',
        status: 'active',
        patchCount: 1
      },
      'ready-for-review': {
        id: 'multi-layer',
        label: 'multi-layer',
        intent: 'Database and API refactor',
        status: 'active',
        patchCount: 2
      },
      'conflict-resolution': {
        id: 'conflict-fix',
        label: 'conflict-fix',
        intent: 'Resolve validation conflicts',
        status: 'conflicts',
        patchCount: 1
      },
      'team-collaboration': {
        id: 'team-collab',
        label: 'team-collab',
        intent: 'Feature integration sprint',
        status: 'active',
        patchCount: 1
      }
    };

    return sessionMap[scenario] || sessionMap['clean-start'];
  }

  private _getAvailableInstances() {
    return [
      { id: 'local', label: 'Local (/home/stachu/code/dark)' },
      { id: 'matter', label: 'matter.darklang.com' },
      { id: 'private', label: 'team.company.com' }
    ];
  }

  private _getAvailableSessions(scenario: string) {
    // Return sessions other than current
    const allSessions = [
      { id: 'clean-start', label: 'clean-start: New project setup' },
      { id: 'stdlib-dev', label: 'stdlib-dev: Add List.filterMap function' },
      { id: 'multi-layer', label: 'multi-layer: Database and API refactor' },
      { id: 'conflict-fix', label: 'conflict-fix: Resolve validation conflicts' },
      { id: 'team-collab', label: 'team-collab: Feature integration sprint' }
    ];

    return allSessions.filter(s => !s.label.includes(scenario));
  }
}