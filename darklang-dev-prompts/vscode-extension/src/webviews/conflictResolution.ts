import * as vscode from 'vscode';
import { DarkCLI, ConflictInfo } from '../utils/darkCli';

export class ConflictResolutionPanel {
    public static currentPanel: ConflictResolutionPanel | undefined;
    private readonly _panel: vscode.WebviewPanel;
    private _disposables: vscode.Disposable[] = [];
    private conflicts: ConflictInfo[] = [];

    public static createOrShow(extensionUri: vscode.Uri, darkCli: DarkCLI, conflicts: ConflictInfo[]) {
        const column = vscode.window.activeTextEditor
            ? vscode.window.activeTextEditor.viewColumn
            : undefined;

        if (ConflictResolutionPanel.currentPanel) {
            ConflictResolutionPanel.currentPanel._panel.reveal(column);
            ConflictResolutionPanel.currentPanel.updateConflicts(conflicts);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'conflictResolution',
            'Resolve Conflicts',
            column || vscode.ViewColumn.One,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [
                    vscode.Uri.joinPath(extensionUri, 'media')
                ]
            }
        );

        ConflictResolutionPanel.currentPanel = new ConflictResolutionPanel(panel, extensionUri, darkCli, conflicts);
    }

    private constructor(
        panel: vscode.WebviewPanel,
        private readonly _extensionUri: vscode.Uri,
        private readonly darkCli: DarkCLI,
        conflicts: ConflictInfo[]
    ) {
        this._panel = panel;
        this.conflicts = conflicts;

        this._update();
        this._panel.onDidDispose(() => this.dispose(), null, this._disposables);
        
        this._panel.webview.onDidReceiveMessage(
            async message => {
                switch (message.type) {
                    case 'resolveConflict':
                        await this.handleResolveConflict(message.conflictId, message.strategy);
                        break;
                    case 'autoResolve':
                        await this.handleAutoResolve();
                        break;
                    case 'refreshConflicts':
                        await this.refreshConflicts();
                        break;
                    case 'showConflictDetails':
                        await this.showConflictDetails(message.conflictId);
                        break;
                }
            },
            null,
            this._disposables
        );
    }

    public updateConflicts(conflicts: ConflictInfo[]) {
        this.conflicts = conflicts;
        this._update();
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

    private async handleResolveConflict(conflictId: string, strategy: string) {
        try {
            await this.darkCli.resolveConflict(conflictId, strategy);
            
            // Remove resolved conflict from list
            this.conflicts = this.conflicts.filter(c => c.id !== conflictId);
            
            // Update UI
            this._update();
            
            // Show success message
            this._panel.webview.postMessage({
                type: 'conflictResolved',
                conflictId: conflictId,
                strategy: strategy
            });
            
            // If no more conflicts, show completion message
            if (this.conflicts.length === 0) {
                vscode.window.showInformationMessage('🎉 All conflicts resolved!');
                this.dispose();
            }
            
        } catch (error) {
            this._panel.webview.postMessage({
                type: 'error',
                message: `Failed to resolve conflict: ${error}`
            });
        }
    }

    private async handleAutoResolve() {
        try {
            await this.darkCli.autoResolveConflicts();
            
            // Refresh conflicts list
            await this.refreshConflicts();
            
            this._panel.webview.postMessage({
                type: 'autoResolveComplete'
            });
            
        } catch (error) {
            this._panel.webview.postMessage({
                type: 'error',
                message: `Auto-resolve failed: ${error}`
            });
        }
    }

    private async refreshConflicts() {
        try {
            this.conflicts = await this.darkCli.getConflicts();
            this._update();
        } catch (error) {
            this._panel.webview.postMessage({
                type: 'error',
                message: `Failed to refresh conflicts: ${error}`
            });
        }
    }

    private async showConflictDetails(conflictId: string) {
        const conflict = this.conflicts.find(c => c.id === conflictId);
        if (conflict) {
            // Create a separate details panel or update current one
            this._panel.webview.postMessage({
                type: 'showDetails',
                conflict: conflict
            });
        }
    }

    private _update() {
        this._panel.title = `Resolve Conflicts (${this.conflicts.length})`;
        this._panel.webview.html = this._getHtmlForWebview(this._panel.webview);
    }

    private _getHtmlForWebview(webview: vscode.Webview) {
        const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'conflict-resolution.js'));
        const styleUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'conflict-resolution.css'));

        return `<!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <link href="${styleUri}" rel="stylesheet">
            <title>Conflict Resolution</title>
            <style>
                body {
                    font-family: var(--vscode-font-family);
                    font-size: var(--vscode-font-size);
                    color: var(--vscode-foreground);
                    background-color: var(--vscode-editor-background);
                    padding: 20px;
                    margin: 0;
                }
                .header {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    padding-bottom: 20px;
                    border-bottom: 1px solid var(--vscode-panel-border);
                    margin-bottom: 20px;
                }
                .conflict-item {
                    background: var(--vscode-editor-background);
                    border: 1px solid var(--vscode-panel-border);
                    border-radius: 6px;
                    margin-bottom: 16px;
                    overflow: hidden;
                }
                .conflict-header {
                    background: var(--vscode-textCodeBlock-background);
                    padding: 16px;
                    border-bottom: 1px solid var(--vscode-panel-border);
                    cursor: pointer;
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                }
                .conflict-header:hover {
                    background: var(--vscode-list-hoverBackground);
                }
                .conflict-details {
                    padding: 16px;
                    display: none;
                }
                .conflict-details.expanded {
                    display: block;
                }
                .severity-high { border-left: 4px solid #e74c3c; }
                .severity-medium { border-left: 4px solid #f39c12; }
                .severity-low { border-left: 4px solid #27ae60; }
                .severity-badge {
                    padding: 2px 8px;
                    border-radius: 12px;
                    font-size: 0.8em;
                    font-weight: bold;
                    text-transform: uppercase;
                }
                .severity-high .severity-badge { background: #e74c3c; color: white; }
                .severity-medium .severity-badge { background: #f39c12; color: white; }
                .severity-low .severity-badge { background: #27ae60; color: white; }
                .resolution-options {
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
                    gap: 12px;
                    margin-top: 16px;
                }
                .resolution-btn {
                    padding: 12px 16px;
                    border: 1px solid var(--vscode-button-border);
                    background: var(--vscode-button-background);
                    color: var(--vscode-button-foreground);
                    cursor: pointer;
                    border-radius: 4px;
                    text-align: center;
                    transition: background-color 0.2s;
                }
                .resolution-btn:hover {
                    background: var(--vscode-button-hoverBackground);
                }
                .resolution-btn.primary {
                    background: var(--vscode-button-background);
                }
                .resolution-btn.dangerous {
                    border-color: #e74c3c;
                    color: #e74c3c;
                }
                .action-bar {
                    display: flex;
                    gap: 12px;
                    margin-bottom: 20px;
                }
                .btn {
                    padding: 8px 16px;
                    border: 1px solid var(--vscode-button-border);
                    background: var(--vscode-button-background);
                    color: var(--vscode-button-foreground);
                    cursor: pointer;
                    border-radius: 4px;
                }
                .btn:hover {
                    background: var(--vscode-button-hoverBackground);
                }
                .btn.primary {
                    background: var(--vscode-button-background);
                }
                .empty-state {
                    text-align: center;
                    padding: 40px;
                    color: var(--vscode-descriptionForeground);
                }
                .patches-affected {
                    background: var(--vscode-textCodeBlock-background);
                    padding: 12px;
                    border-radius: 4px;
                    margin: 12px 0;
                }
                .expand-icon {
                    transition: transform 0.2s;
                }
                .expanded .expand-icon {
                    transform: rotate(90deg);
                }
                .loading {
                    text-align: center;
                    padding: 20px;
                }
            </style>
        </head>
        <body>
            <div class="header">
                <h1>Conflict Resolution</h1>
                <div class="action-bar">
                    <button class="btn" onclick="refreshConflicts()">🔄 Refresh</button>
                    <button class="btn primary" onclick="autoResolve()">✨ Auto Resolve</button>
                </div>
            </div>

            <div id="conflicts-container">
                ${this.conflicts.length === 0 ? this._getEmptyStateHtml() : this._getConflictsHtml()}
            </div>

            <div id="loading" class="loading" style="display: none;">
                <div>Resolving conflicts...</div>
            </div>

            <script>
                const vscode = acquireVsCodeApi();
                
                function toggleConflictDetails(conflictId) {
                    const details = document.getElementById('details-' + conflictId);
                    const header = document.getElementById('header-' + conflictId);
                    
                    if (details.classList.contains('expanded')) {
                        details.classList.remove('expanded');
                        header.classList.remove('expanded');
                    } else {
                        details.classList.add('expanded');
                        header.classList.add('expanded');
                    }
                }
                
                function resolveConflict(conflictId, strategy) {
                    showLoading();
                    vscode.postMessage({
                        type: 'resolveConflict',
                        conflictId: conflictId,
                        strategy: strategy
                    });
                }
                
                function autoResolve() {
                    showLoading();
                    vscode.postMessage({
                        type: 'autoResolve'
                    });
                }
                
                function refreshConflicts() {
                    showLoading();
                    vscode.postMessage({
                        type: 'refreshConflicts'
                    });
                }
                
                function showLoading() {
                    document.getElementById('loading').style.display = 'block';
                    document.getElementById('conflicts-container').style.opacity = '0.5';
                }
                
                function hideLoading() {
                    document.getElementById('loading').style.display = 'none';
                    document.getElementById('conflicts-container').style.opacity = '1';
                }
                
                // Listen for messages from extension
                window.addEventListener('message', event => {
                    const message = event.data;
                    switch (message.type) {
                        case 'conflictResolved':
                            hideLoading();
                            // Remove resolved conflict from UI
                            const conflictItem = document.getElementById('conflict-' + message.conflictId);
                            if (conflictItem) {
                                conflictItem.remove();
                            }
                            break;
                        case 'autoResolveComplete':
                            hideLoading();
                            location.reload();
                            break;
                        case 'error':
                            hideLoading();
                            alert('Error: ' + message.message);
                            break;
                    }
                });
                
                // Auto-hide loading after 5 seconds as fallback
                setTimeout(hideLoading, 5000);
            </script>
        </body>
        </html>`;
    }

    private _getEmptyStateHtml(): string {
        return `
            <div class="empty-state">
                <h2>🎉 No Conflicts Detected</h2>
                <p>All patches are compatible and can be merged cleanly.</p>
            </div>
        `;
    }

    private _getConflictsHtml(): string {
        return this.conflicts.map(conflict => `
            <div id="conflict-${conflict.id}" class="conflict-item severity-${conflict.severity}">
                <div id="header-${conflict.id}" class="conflict-header" onclick="toggleConflictDetails('${conflict.id}')">
                    <div>
                        <div style="display: flex; align-items: center; gap: 12px;">
                            <span class="expand-icon">▶</span>
                            <span class="severity-badge">${conflict.severity}</span>
                            <strong>${conflict.type}</strong>
                        </div>
                        <div style="margin-top: 4px; color: var(--vscode-descriptionForeground);">
                            ${conflict.description}
                        </div>
                    </div>
                    ${conflict.canAutoResolve ? '<span style="color: #27ae60;">✨ Auto-resolvable</span>' : ''}
                </div>
                
                <div id="details-${conflict.id}" class="conflict-details">
                    <div class="patches-affected">
                        <strong>Affected Patches:</strong><br>
                        ${conflict.patches.map(patchId => `<code>${patchId}</code>`).join(', ')}
                    </div>
                    
                    <div class="resolution-options">
                        ${this._getResolutionOptionsHtml(conflict)}
                    </div>
                </div>
            </div>
        `).join('');
    }

    private _getResolutionOptionsHtml(conflict: ConflictInfo): string {
        const options = [];

        // Common resolution strategies
        if (conflict.type === 'Same Function Different Implementation') {
            options.push(
                `<button class="resolution-btn" onclick="resolveConflict('${conflict.id}', 'keep-local')">
                    Keep Local Changes
                </button>`,
                `<button class="resolution-btn" onclick="resolveConflict('${conflict.id}', 'keep-remote')">
                    Keep Remote Changes
                </button>`,
                `<button class="resolution-btn" onclick="resolveConflict('${conflict.id}', 'three-way')">
                    Three-Way Merge
                </button>`,
                `<button class="resolution-btn dangerous" onclick="resolveConflict('${conflict.id}', 'manual')">
                    Manual Resolution
                </button>`
            );
        } else if (conflict.type === 'Name Collision') {
            options.push(
                `<button class="resolution-btn primary" onclick="resolveConflict('${conflict.id}', 'rename-both')">
                    Rename Both (Recommended)
                </button>`,
                `<button class="resolution-btn" onclick="resolveConflict('${conflict.id}', 'keep-local')">
                    Keep Local Only
                </button>`,
                `<button class="resolution-btn" onclick="resolveConflict('${conflict.id}', 'keep-remote')">
                    Keep Remote Only
                </button>`
            );
        } else {
            // Generic options
            options.push(
                `<button class="resolution-btn" onclick="resolveConflict('${conflict.id}', 'keep-local')">
                    Keep Local
                </button>`,
                `<button class="resolution-btn" onclick="resolveConflict('${conflict.id}', 'keep-remote')">
                    Keep Remote
                </button>`,
                `<button class="resolution-btn dangerous" onclick="resolveConflict('${conflict.id}', 'manual')">
                    Manual Review
                </button>`
            );
        }

        return options.join('');
    }
}