import * as vscode from 'vscode';
import { DarkCLI } from '../utils/darkCli';

/**
 * Session Transfer Commands for VS Code
 * Provides UI for transferring sessions between devices
 */

export function registerSessionTransferCommands(
    context: vscode.ExtensionContext,
    darkCli: DarkCLI
) {
    // Export current session
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.session.export', async () => {
            try {
                const sessions = await darkCli.getSessions();
                
                if (sessions.length === 0) {
                    vscode.window.showWarningMessage('No sessions available to export');
                    return;
                }
                
                // Let user pick session to export
                const sessionItems = sessions.map(s => ({
                    label: s.name,
                    description: s.description,
                    detail: `Created: ${s.createdAt}`,
                    sessionId: s.id
                }));
                
                const selectedSession = await vscode.window.showQuickPick(sessionItems, {
                    placeHolder: 'Select session to export'
                });
                
                if (!selectedSession) return;
                
                // Choose export format
                const formatItems = [
                    { label: 'QR Code', description: 'Quick transfer via QR code', format: 'qr' },
                    { label: 'Shareable Link', description: 'Upload to cloud and get link', format: 'link' },
                    { label: 'JSON File', description: 'Export to file', format: 'json' },
                    { label: 'Compressed', description: 'Compressed JSON for large sessions', format: 'compressed' }
                ];
                
                const selectedFormat = await vscode.window.showQuickPick(formatItems, {
                    placeHolder: 'Select export format'
                });
                
                if (!selectedFormat) return;
                
                // Export session
                const result = await darkCli.exportSession(selectedSession.sessionId, selectedFormat.format);
                
                // Handle different export formats
                switch (selectedFormat.format) {
                    case 'qr':
                        await showQrCodeDialog(result, selectedSession.label);
                        break;
                    
                    case 'link':
                        await showShareableLinkDialog(result, selectedSession.label);
                        break;
                    
                    case 'json':
                    case 'compressed':
                        await saveSessionToFile(result, selectedSession.label, selectedFormat.format);
                        break;
                }
                
            } catch (error) {
                vscode.window.showErrorMessage(`Failed to export session: ${error}`);
            }
        })
    );
    
    // Import session
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.session.import', async () => {
            try {
                const importMethods = [
                    { label: 'From QR Code', description: 'Scan or paste QR code data', method: 'qr' },
                    { label: 'From Link', description: 'Import from shareable link', method: 'link' },
                    { label: 'From File', description: 'Import from JSON file', method: 'file' },
                    { label: 'From Clipboard', description: 'Import from clipboard data', method: 'clipboard' }
                ];
                
                const selectedMethod = await vscode.window.showQuickPick(importMethods, {
                    placeHolder: 'Select import method'
                });
                
                if (!selectedMethod) return;
                
                let importData: string;
                let format: string;
                
                switch (selectedMethod.method) {
                    case 'qr':
                        importData = await vscode.window.showInputBox({
                            prompt: 'Paste QR code data',
                            placeHolder: 'QR:...'
                        }) || '';
                        format = 'qr';
                        break;
                    
                    case 'link':
                        importData = await vscode.window.showInputBox({
                            prompt: 'Paste shareable link',
                            placeHolder: 'https://transfer.darklang.com/session/...'
                        }) || '';
                        format = 'link';
                        break;
                    
                    case 'file':
                        const fileUri = await vscode.window.showOpenDialog({
                            canSelectFiles: true,
                            canSelectFolders: false,
                            canSelectMany: false,
                            filters: {
                                'Session Files': ['json', 'session'],
                                'All Files': ['*']
                            }
                        });
                        
                        if (!fileUri || fileUri.length === 0) return;
                        
                        importData = fileUri[0].fsPath;
                        format = 'json';
                        break;
                    
                    case 'clipboard':
                        importData = await vscode.env.clipboard.readText();
                        format = 'json'; // Auto-detect format
                        break;
                    
                    default:
                        return;
                }
                
                if (!importData) {
                    vscode.window.showWarningMessage('No import data provided');
                    return;
                }
                
                // Import session
                const result = await darkCli.importSession(importData, format);
                
                if (result.success) {
                    vscode.window.showInformationMessage(
                        `Session imported successfully: ${result.sessionId}`,
                        'Switch to Session'
                    ).then(action => {
                        if (action === 'Switch to Session') {
                            vscode.commands.executeCommand('darklang.session.switch', result.sessionId);
                        }
                    });
                } else {
                    vscode.window.showErrorMessage(`Import failed: ${result.message}`);
                }
                
            } catch (error) {
                vscode.window.showErrorMessage(`Failed to import session: ${error}`);
            }
        })
    );
    
    // Quick transfer - prepare session for transfer
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.session.transfer', async () => {
            try {
                const currentSession = await darkCli.getCurrentSession();
                
                if (!currentSession) {
                    vscode.window.showWarningMessage('No active session to transfer');
                    return;
                }
                
                // Show transfer preparation dialog
                await showTransferDialog(currentSession, darkCli);
                
            } catch (error) {
                vscode.window.showErrorMessage(`Failed to prepare transfer: ${error}`);
            }
        })
    );
    
    // Auto-save configuration
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.session.autosave', async () => {
            try {
                const currentSession = await darkCli.getCurrentSession();
                
                if (!currentSession) {
                    vscode.window.showWarningMessage('No active session');
                    return;
                }
                
                const action = await vscode.window.showQuickPick([
                    { label: 'Enable Auto-save', action: 'enable' },
                    { label: 'Disable Auto-save', action: 'disable' },
                    { label: 'Configure Interval', action: 'configure' }
                ], {
                    placeHolder: 'Auto-save action'
                });
                
                if (!action) return;
                
                switch (action.action) {
                    case 'enable':
                        await darkCli.setSessionAutosave(currentSession.id, true, 5);
                        vscode.window.showInformationMessage('Auto-save enabled (5 minute interval)');
                        break;
                    
                    case 'disable':
                        await darkCli.setSessionAutosave(currentSession.id, false);
                        vscode.window.showInformationMessage('Auto-save disabled');
                        break;
                    
                    case 'configure':
                        const intervalStr = await vscode.window.showInputBox({
                            prompt: 'Auto-save interval (minutes)',
                            value: '5',
                            validateInput: (value) => {
                                const num = parseInt(value);
                                if (isNaN(num) || num < 1 || num > 60) {
                                    return 'Please enter a number between 1 and 60';
                                }
                                return null;
                            }
                        });
                        
                        if (intervalStr) {
                            const interval = parseInt(intervalStr);
                            await darkCli.setSessionAutosave(currentSession.id, true, interval);
                            vscode.window.showInformationMessage(`Auto-save enabled (${interval} minute interval)`);
                        }
                        break;
                }
                
            } catch (error) {
                vscode.window.showErrorMessage(`Auto-save configuration failed: ${error}`);
            }
        })
    );
    
    // Sync session to cloud
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.session.syncCloud', async () => {
            try {
                const currentSession = await darkCli.getCurrentSession();
                
                if (!currentSession) {
                    vscode.window.showWarningMessage('No active session to sync');
                    return;
                }
                
                const action = await vscode.window.showQuickPick([
                    { label: 'Push to Cloud', description: 'Upload current session', action: 'push' },
                    { label: 'Pull from Cloud', description: 'Download session from cloud', action: 'pull' }
                ], {
                    placeHolder: 'Cloud sync action'
                });
                
                if (!action) return;
                
                if (action.action === 'push') {
                    const result = await darkCli.syncSessionToCloud(currentSession.id);
                    if (result.success) {
                        vscode.window.showInformationMessage(
                            `Session synced to cloud: ${result.uploadId}`,
                            'Copy ID'
                        ).then(copyAction => {
                            if (copyAction === 'Copy ID') {
                                vscode.env.clipboard.writeText(result.uploadId);
                            }
                        });
                    } else {
                        vscode.window.showErrorMessage(`Cloud sync failed: ${result.message}`);
                    }
                } else {
                    const uploadId = await vscode.window.showInputBox({
                        prompt: 'Enter cloud upload ID',
                        placeHolder: 'upload-id-123...'
                    });
                    
                    if (uploadId) {
                        const result = await darkCli.downloadSessionFromCloud(uploadId);
                        if (result.success) {
                            vscode.window.showInformationMessage(`Session downloaded: ${result.sessionId}`);
                        } else {
                            vscode.window.showErrorMessage(`Download failed: ${result.message}`);
                        }
                    }
                }
                
            } catch (error) {
                vscode.window.showErrorMessage(`Cloud sync failed: ${error}`);
            }
        })
    );
}

async function showQrCodeDialog(qrData: string, sessionName: string) {
    const panel = vscode.window.createWebviewPanel(
        'sessionQrCode',
        `Session Transfer - ${sessionName}`,
        vscode.ViewColumn.Beside,
        {
            enableScripts: true
        }
    );
    
    panel.webview.html = getQrCodeHtml(qrData, sessionName);
}

async function showShareableLinkDialog(link: string, sessionName: string) {
    const action = await vscode.window.showInformationMessage(
        `Session "${sessionName}" is ready for transfer`,
        'Copy Link',
        'Open Link'
    );
    
    if (action === 'Copy Link') {
        await vscode.env.clipboard.writeText(link);
        vscode.window.showInformationMessage('Link copied to clipboard');
    } else if (action === 'Open Link') {
        vscode.env.openExternal(vscode.Uri.parse(link));
    }
}

async function saveSessionToFile(data: string, sessionName: string, format: string) {
    const defaultFileName = `${sessionName.replace(/[^a-zA-Z0-9]/g, '_')}.${format === 'compressed' ? 'session' : 'json'}`;
    
    const saveUri = await vscode.window.showSaveDialog({
        defaultUri: vscode.Uri.file(defaultFileName),
        filters: {
            'Session Files': ['json', 'session'],
            'All Files': ['*']
        }
    });
    
    if (saveUri) {
        await vscode.workspace.fs.writeFile(saveUri, Buffer.from(data, 'utf8'));
        vscode.window.showInformationMessage(`Session exported to ${saveUri.fsPath}`);
    }
}

async function showTransferDialog(session: any, darkCli: DarkCLI) {
    const panel = vscode.window.createWebviewPanel(
        'sessionTransfer',
        `Transfer Session - ${session.name}`,
        vscode.ViewColumn.Beside,
        {
            enableScripts: true
        }
    );
    
    // Generate all transfer options
    const transferData = await darkCli.prepareSessionTransfer(session.id);
    
    panel.webview.html = getTransferDialogHtml(session, transferData);
    
    // Handle messages from webview
    panel.webview.onDidReceiveMessage(async (message) => {
        switch (message.command) {
            case 'copyToClipboard':
                await vscode.env.clipboard.writeText(message.data);
                vscode.window.showInformationMessage('Copied to clipboard');
                break;
        }
    });
}

function getQrCodeHtml(qrData: string, sessionName: string): string {
    return `
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Session QR Code</title>
            <style>
                body { font-family: sans-serif; padding: 20px; text-align: center; }
                .qr-container { margin: 20px; padding: 20px; border: 1px solid #ccc; }
                .qr-code { font-family: monospace; background: #f5f5f5; padding: 10px; margin: 10px; }
                button { padding: 8px 16px; margin: 5px; }
            </style>
        </head>
        <body>
            <h2>Session Transfer QR Code</h2>
            <p>Session: <strong>${sessionName}</strong></p>
            
            <div class="qr-container">
                <div class="qr-code">${qrData}</div>
                <button onclick="copyToClipboard()">Copy QR Data</button>
            </div>
            
            <p>Scan this QR code or copy the data to transfer the session to another device.</p>
            
            <script>
                function copyToClipboard() {
                    navigator.clipboard.writeText('${qrData}').then(() => {
                        alert('QR data copied to clipboard');
                    });
                }
            </script>
        </body>
        </html>
    `;
}

function getTransferDialogHtml(session: any, transferData: any): string {
    return `
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Session Transfer</title>
            <style>
                body { font-family: sans-serif; padding: 20px; }
                .method { margin: 20px 0; padding: 15px; border: 1px solid #ddd; border-radius: 5px; }
                .method h3 { margin-top: 0; }
                .data-box { background: #f5f5f5; padding: 10px; margin: 10px 0; font-family: monospace; word-break: break-all; }
                button { padding: 8px 16px; margin: 5px; background: #007acc; color: white; border: none; border-radius: 3px; cursor: pointer; }
                button:hover { background: #005999; }
            </style>
        </head>
        <body>
            <h2>Transfer Session: ${session.name}</h2>
            <p>Choose a transfer method below:</p>
            
            <div class="method">
                <h3>🔗 Shareable Link</h3>
                <p>Upload to cloud and share link (expires in 24 hours)</p>
                <div class="data-box">${transferData.shareableLink || 'Generating...'}</div>
                <button onclick="copyLink()">Copy Link</button>
                <button onclick="openLink()">Open Link</button>
            </div>
            
            <div class="method">
                <h3>📱 QR Code</h3>
                <p>Scan with mobile device or copy data</p>
                <div class="data-box">${transferData.qrCode || 'Generating...'}</div>
                <button onclick="copyQR()">Copy QR Data</button>
            </div>
            
            <div class="method">
                <h3>💾 File Export</h3>
                <p>Save session to file for manual transfer</p>
                <button onclick="saveFile()">Save to File</button>
            </div>
            
            <div class="method">
                <h3>☁️ Cloud Sync</h3>
                <p>Sync to Darklang cloud (requires account)</p>
                ${transferData.cloudId ? 
                    `<div class="data-box">Cloud ID: ${transferData.cloudId}</div><button onclick="copyCloudId()">Copy Cloud ID</button>` :
                    '<button onclick="syncToCloud()">Sync to Cloud</button>'
                }
            </div>
            
            <script>
                const vscode = acquireVsCodeApi();
                
                function copyLink() {
                    vscode.postMessage({ command: 'copyToClipboard', data: '${transferData.shareableLink}' });
                }
                
                function copyQR() {
                    vscode.postMessage({ command: 'copyToClipboard', data: '${transferData.qrCode}' });
                }
                
                function copyCloudId() {
                    vscode.postMessage({ command: 'copyToClipboard', data: '${transferData.cloudId}' });
                }
                
                function openLink() {
                    window.open('${transferData.shareableLink}', '_blank');
                }
                
                function saveFile() {
                    vscode.postMessage({ command: 'saveFile' });
                }
                
                function syncToCloud() {
                    vscode.postMessage({ command: 'syncToCloud' });
                }
            </script>
        </body>
        </html>
    `;
}