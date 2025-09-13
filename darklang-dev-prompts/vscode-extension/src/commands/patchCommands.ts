import * as vscode from 'vscode';
import { DarkCLI } from '../utils/darkCli';
import { PatchProvider } from '../providers/patchProvider';

export function registerPatchCommands(
    context: vscode.ExtensionContext,
    darkCli: DarkCLI,
    patchProvider: PatchProvider
) {
    // Create Patch Command
    const createPatchCommand = vscode.commands.registerCommand('darklang.patch.create', async () => {
        try {
            const intent = await vscode.window.showInputBox({
                prompt: 'Describe the intent of this patch',
                placeholder: 'e.g., Add List.filterMap function for better data processing',
                validateInput: (value) => {
                    if (!value || value.trim().length === 0) {
                        return 'Intent description is required';
                    }
                    if (value.length < 10) {
                        return 'Please provide a more detailed description (at least 10 characters)';
                    }
                    return null;
                }
            });

            if (!intent) {
                return;
            }

            const patchId = await darkCli.createPatch(intent.trim());
            
            if (patchId) {
                vscode.window.showInformationMessage(
                    `Patch created: ${patchId}`,
                    'View Patch'
                ).then(selection => {
                    if (selection === 'View Patch') {
                        vscode.commands.executeCommand('darklang.patch.view', patchId);
                    }
                });
                
                // Refresh the patch provider
                patchProvider.refresh();
            } else {
                vscode.window.showErrorMessage('Failed to create patch');
            }

        } catch (error) {
            vscode.window.showErrorMessage(`Error creating patch: ${error}`);
        }
    });

    // View Patch Command
    const viewPatchCommand = vscode.commands.registerCommand('darklang.patch.view', async (patchId?: string) => {
        try {
            if (!patchId) {
                // Show quick pick if no patch ID provided
                const patches = await darkCli.getPatches();
                const items = patches.map(p => ({
                    label: `${p.id.substring(0, 8)}: ${p.intent}`,
                    description: `by ${p.author} (${p.status})`,
                    detail: `Functions: ${p.functions.join(', ')}`,
                    patchId: p.id
                }));

                const selected = await vscode.window.showQuickPick(items, {
                    placeHolder: 'Select a patch to view'
                });

                if (!selected) {
                    return;
                }

                patchId = selected.patchId;
            }

            const patch = await darkCli.getPatchDetails(patchId);
            
            if (patch) {
                // Create and show patch details webview
                const panel = vscode.window.createWebviewPanel(
                    'patchDetails',
                    `Patch: ${patch.intent}`,
                    vscode.ViewColumn.One,
                    {
                        enableScripts: true,
                        retainContextWhenHidden: true
                    }
                );

                panel.webview.html = generatePatchDetailsHtml(patch);
                
                // Handle messages from webview
                panel.webview.onDidReceiveMessage(message => {
                    switch (message.command) {
                        case 'apply':
                            vscode.commands.executeCommand('darklang.patch.apply', patch.id);
                            break;
                        case 'ready':
                            vscode.commands.executeCommand('darklang.patch.ready', patch.id);
                            break;
                        case 'diff':
                            vscode.commands.executeCommand('darklang.patch.diff', patch.id);
                            break;
                    }
                });

            } else {
                vscode.window.showErrorMessage(`Patch ${patchId} not found`);
            }

        } catch (error) {
            vscode.window.showErrorMessage(`Error viewing patch: ${error}`);
        }
    });

    // Apply Patch Command
    const applyPatchCommand = vscode.commands.registerCommand('darklang.patch.apply', async (patchId: string) => {
        try {
            const patch = await darkCli.getPatchDetails(patchId);
            if (!patch) {
                vscode.window.showErrorMessage(`Patch ${patchId} not found`);
                return;
            }

            const confirmation = await vscode.window.showWarningMessage(
                `Apply patch "${patch.intent}" by ${patch.author}?\n\nThis will modify the following functions:\n${patch.functions.join('\n')}`,
                { modal: true },
                'Apply',
                'Cancel'
            );

            if (confirmation === 'Apply') {
                await darkCli.applyPatch(patchId);
                
                vscode.window.showInformationMessage(
                    `Patch applied successfully: ${patch.intent}`,
                    'Show Changes'
                ).then(selection => {
                    if (selection === 'Show Changes') {
                        // Open the modified files
                        patch.functions.forEach(funcName => {
                            // Convert function name to file path and open
                            const filePath = functionNameToFilePath(funcName);
                            if (filePath) {
                                vscode.workspace.openTextDocument(filePath).then(doc => {
                                    vscode.window.showTextDocument(doc);
                                });
                            }
                        });
                    }
                });
                
                patchProvider.refresh();
            }

        } catch (error) {
            vscode.window.showErrorMessage(`Error applying patch: ${error}`);
        }
    });

    // Mark Patch Ready Command
    const readyPatchCommand = vscode.commands.registerCommand('darklang.patch.ready', async (patchId?: string) => {
        try {
            await darkCli.markPatchReady(patchId);
            
            vscode.window.showInformationMessage(
                'Patch marked as ready for sharing',
                'Push to Server'
            ).then(selection => {
                if (selection === 'Push to Server') {
                    vscode.commands.executeCommand('darklang.sync.push');
                }
            });
            
            patchProvider.refresh();

        } catch (error) {
            vscode.window.showErrorMessage(`Error marking patch ready: ${error}`);
        }
    });

    // Show Patch Diff Command
    const diffPatchCommand = vscode.commands.registerCommand('darklang.patch.diff', async (patchId: string) => {
        try {
            // This would show a diff view of the patch changes
            // For now, we'll show a placeholder message
            vscode.window.showInformationMessage(
                `Showing diff for patch ${patchId} (feature coming soon)`,
                'View Patch Details'
            ).then(selection => {
                if (selection === 'View Patch Details') {
                    vscode.commands.executeCommand('darklang.patch.view', patchId);
                }
            });

        } catch (error) {
            vscode.window.showErrorMessage(`Error showing patch diff: ${error}`);
        }
    });

    // Register all commands
    context.subscriptions.push(
        createPatchCommand,
        viewPatchCommand,
        applyPatchCommand,
        readyPatchCommand,
        diffPatchCommand
    );
}

function generatePatchDetailsHtml(patch: any): string {
    return `
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Patch Details</title>
            <style>
                body {
                    font-family: var(--vscode-font-family);
                    font-size: var(--vscode-font-size);
                    color: var(--vscode-foreground);
                    background-color: var(--vscode-editor-background);
                    padding: 20px;
                    line-height: 1.6;
                }
                .header {
                    border-bottom: 1px solid var(--vscode-panel-border);
                    padding-bottom: 15px;
                    margin-bottom: 20px;
                }
                .intent {
                    font-size: 1.4em;
                    font-weight: bold;
                    margin-bottom: 10px;
                }
                .metadata {
                    display: grid;
                    grid-template-columns: 120px 1fr;
                    gap: 10px;
                    margin-bottom: 20px;
                }
                .label {
                    font-weight: bold;
                    color: var(--vscode-descriptionForeground);
                }
                .functions {
                    background-color: var(--vscode-textCodeBlock-background);
                    padding: 15px;
                    border-radius: 4px;
                    border: 1px solid var(--vscode-panel-border);
                    margin: 15px 0;
                }
                .function-list {
                    list-style: none;
                    padding: 0;
                    margin: 0;
                }
                .function-list li {
                    padding: 5px 0;
                    border-bottom: 1px solid var(--vscode-panel-border);
                }
                .function-list li:last-child {
                    border-bottom: none;
                }
                .actions {
                    margin-top: 20px;
                    display: flex;
                    gap: 10px;
                }
                .btn {
                    padding: 8px 16px;
                    border: 1px solid var(--vscode-button-border);
                    background-color: var(--vscode-button-background);
                    color: var(--vscode-button-foreground);
                    cursor: pointer;
                    border-radius: 2px;
                }
                .btn:hover {
                    background-color: var(--vscode-button-hoverBackground);
                }
                .btn.primary {
                    background-color: var(--vscode-button-background);
                    color: var(--vscode-button-foreground);
                }
                .status-${patch.status} {
                    display: inline-block;
                    padding: 2px 8px;
                    border-radius: 12px;
                    font-size: 0.8em;
                    font-weight: bold;
                }
                .status-draft {
                    background-color: #f39c12;
                    color: #fff;
                }
                .status-ready {
                    background-color: #27ae60;
                    color: #fff;
                }
                .status-applied {
                    background-color: #3498db;
                    color: #fff;
                }
            </style>
        </head>
        <body>
            <div class="header">
                <div class="intent">${patch.intent}</div>
                <span class="status-${patch.status}">${patch.status.toUpperCase()}</span>
            </div>
            
            <div class="metadata">
                <div class="label">Patch ID:</div>
                <div>${patch.id}</div>
                
                <div class="label">Author:</div>
                <div>${patch.author}</div>
                
                <div class="label">Created:</div>
                <div>${new Date(patch.createdAt).toLocaleString()}</div>
            </div>
            
            <div class="functions">
                <h3>Modified Functions</h3>
                <ul class="function-list">
                    ${patch.functions.map(func => `<li><code>${func}</code></li>`).join('')}
                </ul>
            </div>
            
            <div class="actions">
                ${patch.status === 'draft' ? 
                    '<button class="btn primary" onclick="sendMessage(\'ready\')">Mark Ready</button>' : ''
                }
                ${patch.status === 'ready' && patch.author !== 'stachu' ? 
                    '<button class="btn primary" onclick="sendMessage(\'apply\')">Apply Patch</button>' : ''
                }
                <button class="btn" onclick="sendMessage('diff')">Show Diff</button>
            </div>
            
            <script>
                const vscode = acquireVsCodeApi();
                
                function sendMessage(command) {
                    vscode.postMessage({ command: command });
                }
            </script>
        </body>
        </html>
    `;
}

function functionNameToFilePath(functionName: string): string | null {
    // Convert function name like "Darklang.Stdlib.List.filterMap" to file path
    // This is a simplified version - real implementation would be more sophisticated
    const parts = functionName.split('.');
    if (parts.length >= 3) {
        const packagePath = parts.slice(0, -1).join('/').toLowerCase();
        return `packages/${packagePath}.dark`;
    }
    return null;
}