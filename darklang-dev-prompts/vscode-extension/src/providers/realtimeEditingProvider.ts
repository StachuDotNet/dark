import * as vscode from 'vscode';
import * as WebSocket from 'ws';

/**
 * Real-time Collaborative Editing Provider for VS Code
 * Enables multiple developers to edit the same file simultaneously
 */
export class RealtimeEditingProvider implements vscode.Disposable {
    private webSocket: WebSocket | null = null;
    private sessionId: string | null = null;
    private userId: string;
    private collaborators = new Map<string, CollaboratorInfo>();
    private isConnected = false;
    
    // Operation tracking
    private pendingOperations = new Map<string, PendingOperation>();
    private documentVersion = 0;
    
    // Decorations for collaborator cursors
    private cursorDecorationTypes = new Map<string, vscode.TextEditorDecorationType>();
    
    // Event handlers
    private documentChangeDisposable: vscode.Disposable | null = null;
    private selectionChangeDisposable: vscode.Disposable | null = null;
    
    constructor(private serverUrl: string, userId: string) {
        this.userId = userId;
        this.setupEventHandlers();
    }
    
    /**
     * Join a collaborative editing session
     */
    async joinSession(sessionId: string, documentUri: vscode.Uri): Promise<boolean> {
        try {
            if (this.webSocket) {
                this.leaveSession();
            }
            
            this.sessionId = sessionId;
            
            // Connect to WebSocket server
            await this.connectToServer();
            
            // Join the specific session
            await this.sendMessage({
                messageType: 'JoinSession',
                sessionId: sessionId,
                documentUri: documentUri.toString(),
                timestamp: Date.now()
            });
            
            vscode.window.showInformationMessage(`Joined collaborative session: ${sessionId}`);
            return true;
            
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to join session: ${error}`);
            return false;
        }
    }
    
    /**
     * Leave the current collaborative session
     */
    leaveSession(): void {
        if (this.webSocket && this.isConnected) {
            this.sendMessage({
                messageType: 'LeaveSession',
                timestamp: Date.now()
            });
        }
        
        this.disconnect();
        this.clearCollaboratorDecorations();
        this.sessionId = null;
        
        vscode.window.showInformationMessage('Left collaborative session');
    }
    
    /**
     * Get list of active collaborators in current session
     */
    getCollaborators(): CollaboratorInfo[] {
        return Array.from(this.collaborators.values());
    }
    
    /**
     * Start collaborative editing for current document
     */
    async startCollaborativeEditing(): Promise<void> {
        const editor = vscode.window.activeTextEditor;
        
        if (!editor) {
            vscode.window.showWarningMessage('No active editor');
            return;
        }
        
        const sessionId = await vscode.window.showInputBox({
            prompt: 'Enter session ID (or leave empty to create new)',
            placeHolder: 'session-id'
        });
        
        const finalSessionId = sessionId || this.generateSessionId();
        await this.joinSession(finalSessionId, editor.document.uri);
    }
    
    private async connectToServer(): Promise<void> {
        return new Promise((resolve, reject) => {
            try {
                this.webSocket = new WebSocket(this.serverUrl);
                
                this.webSocket.on('open', () => {
                    this.isConnected = true;
                    this.setupWebSocketHandlers();
                    resolve();
                });
                
                this.webSocket.on('error', (error) => {
                    this.isConnected = false;
                    reject(error);
                });
                
            } catch (error) {
                reject(error);
            }
        });
    }
    
    private setupWebSocketHandlers(): void {
        if (!this.webSocket) return;
        
        this.webSocket.on('message', (data) => {
            try {
                const message = JSON.parse(data.toString()) as RealtimeMessage;
                this.handleIncomingMessage(message);
            } catch (error) {
                console.error('Failed to parse WebSocket message:', error);
            }
        });
        
        this.webSocket.on('close', () => {
            this.isConnected = false;
            this.handleDisconnection();
        });
        
        this.webSocket.on('error', (error) => {
            console.error('WebSocket error:', error);
            vscode.window.showErrorMessage(`Connection error: ${error.message}`);
        });
    }
    
    private handleIncomingMessage(message: RealtimeMessage): void {
        switch (message.messageType) {
            case 'SessionJoined':
                this.handleSessionJoined(message);
                break;
            
            case 'ParticipantJoined':
                this.handleParticipantJoined(message);
                break;
            
            case 'ParticipantLeft':
                this.handleParticipantLeft(message);
                break;
            
            case 'OperationBroadcast':
                this.handleOperationBroadcast(message);
                break;
            
            case 'CursorBroadcast':
                this.handleCursorBroadcast(message);
                break;
            
            case 'ConflictDetected':
                this.handleConflictDetected(message);
                break;
            
            case 'ErrorMessage':
                this.handleErrorMessage(message);
                break;
            
            case 'HeartbeatPong':
                // Handle heartbeat response
                break;
            
            default:
                console.warn('Unknown message type:', message.messageType);
        }
    }
    
    private handleSessionJoined(message: RealtimeMessage): void {
        const joinInfo = message.payload as SessionJoinInfo;
        
        // Update document version
        this.documentVersion = joinInfo.documentVersion;
        
        // Initialize collaborators
        joinInfo.participants.forEach(participant => {
            if (participant.userId !== this.userId) {
                this.collaborators.set(participant.userId, {
                    userId: participant.userId,
                    userName: participant.userName,
                    cursor: participant.cursor,
                    selection: participant.selection,
                    color: participant.color,
                    isActive: participant.isActive
                });
            }
        });
        
        this.updateCollaboratorDecorations();
        this.showCollaborationStatus();
    }
    
    private handleParticipantJoined(message: RealtimeMessage): void {
        const participant = message.payload as CollaboratorInfo;
        
        if (participant.userId !== this.userId) {
            this.collaborators.set(participant.userId, participant);
            this.updateCollaboratorDecorations();
            
            vscode.window.showInformationMessage(
                `${participant.userName} joined the session`,
                { detail: 'Collaborative editing active' }
            );
        }
    }
    
    private handleParticipantLeft(message: RealtimeMessage): void {
        const userId = message.payload as string;
        
        if (this.collaborators.has(userId)) {
            const collaborator = this.collaborators.get(userId)!;
            this.collaborators.delete(userId);
            this.clearCollaboratorDecoration(userId);
            
            vscode.window.showInformationMessage(
                `${collaborator.userName} left the session`
            );
        }
    }
    
    private handleOperationBroadcast(message: RealtimeMessage): void {
        const operation = message.payload as EditOperation;
        
        // Skip operations from this user
        if (operation.userId === this.userId) {
            return;
        }
        
        // Apply the operation to the document
        this.applyRemoteOperation(operation);
    }
    
    private handleCursorBroadcast(message: RealtimeMessage): void {
        const cursorUpdate = message.payload as CursorUpdate;
        
        if (cursorUpdate.userId !== this.userId) {
            // Update collaborator cursor position
            const collaborator = this.collaborators.get(cursorUpdate.userId);
            if (collaborator) {
                collaborator.cursor = cursorUpdate.cursor;
                collaborator.selection = cursorUpdate.selection;
                this.updateCollaboratorDecorations();
            }
        }
    }
    
    private handleConflictDetected(message: RealtimeMessage): void {
        const conflict = message.payload as RealtimeConflict;
        
        vscode.window.showWarningMessage(
            `Editing conflict detected at line ${conflict.line}`,
            'Resolve',
            'Ignore'
        ).then(action => {
            if (action === 'Resolve') {
                this.showConflictResolution(conflict);
            }
        });
    }
    
    private handleErrorMessage(message: RealtimeMessage): void {
        const error = message.payload as string;
        vscode.window.showErrorMessage(`Collaboration error: ${error}`);
    }
    
    private setupEventHandlers(): void {
        // Document change handler
        this.documentChangeDisposable = vscode.workspace.onDidChangeTextDocument(
            (event) => this.handleDocumentChange(event)
        );
        
        // Selection change handler  
        this.selectionChangeDisposable = vscode.window.onDidChangeTextEditorSelection(
            (event) => this.handleSelectionChange(event)
        );
    }
    
    private handleDocumentChange(event: vscode.TextDocumentChangeEvent): void {
        if (!this.isConnected || !this.sessionId) {
            return;
        }
        
        // Convert VS Code changes to operations
        const operations = this.convertChangesToOperations(event.contentChanges);
        
        // Send operations to server
        operations.forEach(operation => {
            this.sendOperation(operation);
        });
    }
    
    private handleSelectionChange(event: vscode.TextEditorSelectionChangeEvent): void {
        if (!this.isConnected || !this.sessionId) {
            return;
        }
        
        const primarySelection = event.selections[0];
        
        // Send cursor update to server
        this.sendMessage({
            messageType: 'CursorUpdate',
            payload: {
                cursor: {
                    line: primarySelection.active.line,
                    column: primarySelection.active.character
                },
                selection: {
                    start: {
                        line: primarySelection.start.line,
                        column: primarySelection.start.character
                    },
                    end: {
                        line: primarySelection.end.line,
                        column: primarySelection.end.character
                    }
                }
            },
            timestamp: Date.now()
        });
    }
    
    private convertChangesToOperations(changes: readonly vscode.TextDocumentContentChangeEvent[]): EditOperation[] {
        return changes.map(change => ({
            operationId: this.generateOperationId(),
            userId: this.userId,
            operationType: change.text.length > 0 ? 'Insert' : 'Delete',
            position: this.offsetFromPosition(change.range.start),
            content: change.text || undefined,
            length: change.text.length > 0 ? undefined : change.rangeLength,
            version: this.documentVersion,
            timestamp: Date.now()
        }));
    }
    
    private async sendOperation(operation: EditOperation): Promise<void> {
        // Track pending operation
        this.pendingOperations.set(operation.operationId, {
            operation,
            sentAt: Date.now()
        });
        
        await this.sendMessage({
            messageType: 'Operation',
            payload: operation,
            timestamp: Date.now()
        });
    }
    
    private async applyRemoteOperation(operation: EditOperation): Promise<void> {
        const editor = vscode.window.activeTextEditor;
        if (!editor) return;
        
        // Convert operation to VS Code edit
        const edit = new vscode.WorkspaceEdit();
        
        switch (operation.operationType) {
            case 'Insert':
                const insertPosition = this.positionFromOffset(operation.position);
                edit.insert(editor.document.uri, insertPosition, operation.content || '');
                break;
            
            case 'Delete':
                const deleteStart = this.positionFromOffset(operation.position);
                const deleteEnd = this.positionFromOffset(operation.position + (operation.length || 0));
                edit.delete(editor.document.uri, new vscode.Range(deleteStart, deleteEnd));
                break;
        }
        
        // Apply edit without triggering change events from this provider
        this.documentChangeDisposable?.dispose();
        await vscode.workspace.applyEdit(edit);
        this.setupEventHandlers();
        
        // Update document version
        this.documentVersion = operation.version;
    }
    
    private updateCollaboratorDecorations(): void {
        const editor = vscode.window.activeTextEditor;
        if (!editor) return;
        
        // Clear existing decorations
        this.cursorDecorationTypes.forEach(decorationType => {
            editor.setDecorations(decorationType, []);
        });
        
        // Create new decorations for each collaborator
        this.collaborators.forEach((collaborator, userId) => {
            if (collaborator.isActive) {
                this.createCollaboratorDecoration(editor, collaborator);
            }
        });
    }
    
    private createCollaboratorDecoration(editor: vscode.TextEditor, collaborator: CollaboratorInfo): void {
        const cursorPosition = new vscode.Position(collaborator.cursor.line, collaborator.cursor.column);
        
        // Create cursor decoration
        const cursorDecorationType = vscode.window.createTextEditorDecorationType({
            backgroundColor: collaborator.color + '40', // Semi-transparent
            borderLeft: `2px solid ${collaborator.color}`,
            borderRadius: '2px',
            after: {
                contentText: ` ${collaborator.userName}`,
                backgroundColor: collaborator.color,
                color: 'white',
                margin: '0 0 0 5px',
                fontWeight: 'bold',
                fontSize: '12px'
            }
        });
        
        // Create selection decoration if there's a selection
        let selectionRanges: vscode.Range[] = [];
        if (collaborator.selection.start.line !== collaborator.selection.end.line ||
            collaborator.selection.start.column !== collaborator.selection.end.column) {
            
            const selectionStart = new vscode.Position(
                collaborator.selection.start.line,
                collaborator.selection.start.column
            );
            const selectionEnd = new vscode.Position(
                collaborator.selection.end.line,
                collaborator.selection.end.column
            );
            
            selectionRanges = [new vscode.Range(selectionStart, selectionEnd)];
        }
        
        // Apply decorations
        editor.setDecorations(cursorDecorationType, [
            {
                range: new vscode.Range(cursorPosition, cursorPosition),
                hoverMessage: `${collaborator.userName} is editing here`
            }
        ]);
        
        if (selectionRanges.length > 0) {
            const selectionDecorationType = vscode.window.createTextEditorDecorationType({
                backgroundColor: collaborator.color + '20', // Very transparent
                borderRadius: '2px'
            });
            
            editor.setDecorations(selectionDecorationType, selectionRanges.map(range => ({ range })));
            this.cursorDecorationTypes.set(collaborator.userId + '_selection', selectionDecorationType);
        }
        
        this.cursorDecorationTypes.set(collaborator.userId, cursorDecorationType);
    }
    
    private clearCollaboratorDecoration(userId: string): void {
        const cursorDecoration = this.cursorDecorationTypes.get(userId);
        const selectionDecoration = this.cursorDecorationTypes.get(userId + '_selection');
        
        if (cursorDecoration) {
            cursorDecoration.dispose();
            this.cursorDecorationTypes.delete(userId);
        }
        
        if (selectionDecoration) {
            selectionDecoration.dispose();
            this.cursorDecorationTypes.delete(userId + '_selection');
        }
    }
    
    private clearCollaboratorDecorations(): void {
        this.cursorDecorationTypes.forEach(decorationType => {
            decorationType.dispose();
        });
        this.cursorDecorationTypes.clear();
        this.collaborators.clear();
    }
    
    private showCollaborationStatus(): void {
        const collaboratorCount = this.collaborators.size;
        const statusMessage = collaboratorCount > 0 
            ? `${collaboratorCount} collaborator${collaboratorCount > 1 ? 's' : ''} online`
            : 'You are alone in this session';
        
        vscode.window.setStatusBarMessage(
            `🤝 ${statusMessage}`,
            5000
        );
    }
    
    private async showConflictResolution(conflict: RealtimeConflict): Promise<void> {
        // Show conflict resolution UI
        const panel = vscode.window.createWebviewPanel(
            'realtimeConflict',
            'Resolve Editing Conflict',
            vscode.ViewColumn.Beside,
            { enableScripts: true }
        );
        
        panel.webview.html = this.getConflictResolutionHtml(conflict);
        
        // Handle resolution choice
        panel.webview.onDidReceiveMessage(async (message) => {
            switch (message.command) {
                case 'resolveConflict':
                    await this.resolveConflict(conflict, message.resolution);
                    panel.dispose();
                    break;
            }
        });
    }
    
    private async resolveConflict(conflict: RealtimeConflict, resolution: string): Promise<void> {
        await this.sendMessage({
            messageType: 'ResolveConflict',
            payload: {
                conflictId: conflict.conflictId,
                resolution: resolution
            },
            timestamp: Date.now()
        });
    }
    
    private getConflictResolutionHtml(conflict: RealtimeConflict): string {
        return `
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>Resolve Conflict</title>
                <style>
                    body { font-family: sans-serif; padding: 20px; }
                    .conflict { margin: 20px 0; padding: 15px; border: 1px solid #ccc; }
                    .version { margin: 10px 0; padding: 10px; background: #f5f5f5; }
                    button { padding: 8px 16px; margin: 5px; }
                </style>
            </head>
            <body>
                <h2>Editing Conflict at Line ${conflict.line}</h2>
                
                <div class="conflict">
                    <h3>Your Version:</h3>
                    <div class="version">${conflict.yourVersion}</div>
                    
                    <h3>Their Version:</h3>
                    <div class="version">${conflict.theirVersion}</div>
                </div>
                
                <button onclick="resolve('yours')">Keep Your Version</button>
                <button onclick="resolve('theirs')">Accept Their Version</button>
                <button onclick="resolve('manual')">Manual Merge</button>
                
                <script>
                    const vscode = acquireVsCodeApi();
                    
                    function resolve(choice) {
                        vscode.postMessage({
                            command: 'resolveConflict',
                            resolution: choice
                        });
                    }
                </script>
            </body>
            </html>
        `;
    }
    
    private async sendMessage(message: Partial<RealtimeMessage>): Promise<void> {
        if (!this.webSocket || !this.isConnected) {
            throw new Error('Not connected to collaboration server');
        }
        
        const fullMessage: RealtimeMessage = {
            messageId: this.generateMessageId(),
            sessionId: this.sessionId,
            userId: this.userId,
            ...message,
            timestamp: message.timestamp || Date.now()
        } as RealtimeMessage;
        
        this.webSocket.send(JSON.stringify(fullMessage));
    }
    
    private handleDisconnection(): void {
        this.isConnected = false;
        this.clearCollaboratorDecorations();
        
        vscode.window.showWarningMessage(
            'Disconnected from collaboration server',
            'Reconnect'
        ).then(action => {
            if (action === 'Reconnect' && this.sessionId) {
                // Attempt to reconnect
                this.connectToServer().then(() => {
                    return this.sendMessage({
                        messageType: 'JoinSession',
                        sessionId: this.sessionId!
                    });
                }).catch(error => {
                    vscode.window.showErrorMessage(`Reconnection failed: ${error}`);
                });
            }
        });
    }
    
    private disconnect(): void {
        if (this.webSocket) {
            this.webSocket.close();
            this.webSocket = null;
        }
        
        this.isConnected = false;
    }
    
    // Utility methods
    private generateSessionId(): string {
        return 'session-' + Math.random().toString(36).substr(2, 9);
    }
    
    private generateOperationId(): string {
        return 'op-' + Math.random().toString(36).substr(2, 9);
    }
    
    private generateMessageId(): string {
        return 'msg-' + Math.random().toString(36).substr(2, 9);
    }
    
    private offsetFromPosition(position: vscode.Position): number {
        const editor = vscode.window.activeTextEditor;
        if (!editor) return 0;
        
        return editor.document.offsetAt(position);
    }
    
    private positionFromOffset(offset: number): vscode.Position {
        const editor = vscode.window.activeTextEditor;
        if (!editor) return new vscode.Position(0, 0);
        
        return editor.document.positionAt(offset);
    }
    
    dispose(): void {
        this.leaveSession();
        this.documentChangeDisposable?.dispose();
        this.selectionChangeDisposable?.dispose();
        this.clearCollaboratorDecorations();
    }
}

// Type definitions
interface RealtimeMessage {
    messageId: string;
    messageType: string;
    sessionId?: string | null;
    userId?: string;
    payload?: any;
    timestamp: number;
}

interface CollaboratorInfo {
    userId: string;
    userName: string;
    cursor: CursorPosition;
    selection: SelectionRange;
    color: string;
    isActive: boolean;
}

interface CursorPosition {
    line: number;
    column: number;
}

interface SelectionRange {
    start: CursorPosition;
    end: CursorPosition;
}

interface EditOperation {
    operationId: string;
    userId: string;
    operationType: 'Insert' | 'Delete' | 'Replace';
    position: number;
    content?: string;
    length?: number;
    version: number;
    timestamp: number;
}

interface PendingOperation {
    operation: EditOperation;
    sentAt: number;
}

interface SessionJoinInfo {
    documentVersion: number;
    participants: CollaboratorInfo[];
    documentContent: string;
}

interface CursorUpdate {
    userId: string;
    cursor: CursorPosition;
    selection: SelectionRange;
}

interface RealtimeConflict {
    conflictId: string;
    line: number;
    yourVersion: string;
    theirVersion: string;
    timestamp: number;
}