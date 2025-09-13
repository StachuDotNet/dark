import * as cp from 'child_process';
import * as vscode from 'vscode';

export interface PatchInfo {
    id: string;
    author: string;
    intent: string;
    status: 'draft' | 'ready' | 'applied' | 'rejected';
    createdAt: string;
    functions: string[];
}

export interface SessionInfo {
    id: string;
    name: string;
    intent: string;
    owner: string;
    status: 'active' | 'suspended' | 'completed';
    patches: string[];
}

export interface ConflictInfo {
    id: string;
    type: string;
    severity: 'low' | 'medium' | 'high';
    description: string;
    patches: string[];
    canAutoResolve: boolean;
}

export interface SyncStatus {
    connected: boolean;
    serverUrl: string;
    outgoing: number;
    incoming: number;
    lastSync: string;
}

export class DarkCLI {
    private cliPath: string;
    
    constructor() {
        // In real implementation, this would detect the Dark CLI path
        this.cliPath = 'dark';
    }
    
    private async exec(command: string): Promise<string> {
        return new Promise((resolve, reject) => {
            cp.exec(`${this.cliPath} ${command}`, {
                cwd: vscode.workspace.rootPath
            }, (error, stdout, stderr) => {
                if (error) {
                    reject(new Error(`CLI Error: ${stderr || error.message}`));
                } else {
                    resolve(stdout.trim());
                }
            });
        });
    }
    
    private async execWithProgress(command: string, title: string): Promise<string> {
        return vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: title,
            cancellable: false
        }, async (progress) => {
            progress.report({ message: 'Executing...' });
            try {
                const result = await this.exec(command);
                progress.report({ message: 'Complete!' });
                return result;
            } catch (error) {
                throw error;
            }
        });
    }
    
    // Patch Management
    async createPatch(intent: string): Promise<string> {
        const result = await this.execWithProgress(`patch create "${intent}"`, 'Creating patch');
        // Extract patch ID from CLI output
        const match = result.match(/Created patch: ([a-zA-Z0-9-]+)/);
        return match ? match[1] : '';
    }
    
    async getPatches(): Promise<PatchInfo[]> {
        try {
            const result = await this.exec('patch list --json');
            return JSON.parse(result) as PatchInfo[];
        } catch (error) {
            // Return mock data if CLI not available or JSON parsing fails
            return this.getMockPatches();
        }
    }
    
    async getPatchDetails(patchId: string): Promise<PatchInfo | null> {
        try {
            const result = await this.exec(`patch view ${patchId} --json`);
            return JSON.parse(result) as PatchInfo;
        } catch (error) {
            return null;
        }
    }
    
    async applyPatch(patchId: string): Promise<void> {
        await this.execWithProgress(`patch apply ${patchId}`, 'Applying patch');
    }
    
    async markPatchReady(patchId?: string): Promise<void> {
        const command = patchId ? `patch ready ${patchId}` : 'patch ready';
        await this.execWithProgress(command, 'Marking patch ready');
    }
    
    // Session Management
    async createSession(intent: string): Promise<string> {
        const result = await this.execWithProgress(`session new --intent "${intent}"`, 'Creating session');
        const match = result.match(/Created session: ([a-zA-Z0-9-]+)/);
        return match ? match[1] : '';
    }
    
    async getSessions(): Promise<SessionInfo[]> {
        try {
            const result = await this.exec('session list --json');
            return JSON.parse(result) as SessionInfo[];
        } catch (error) {
            return this.getMockSessions();
        }
    }
    
    async getCurrentSession(): Promise<SessionInfo | null> {
        try {
            const result = await this.exec('session current --json');
            return JSON.parse(result) as SessionInfo;
        } catch (error) {
            return null;
        }
    }
    
    async switchSession(sessionId: string): Promise<void> {
        await this.execWithProgress(`session continue ${sessionId}`, 'Switching session');
    }
    
    async suspendSession(): Promise<void> {
        await this.execWithProgress('session suspend', 'Suspending session');
    }
    
    async endSession(): Promise<void> {
        await this.execWithProgress('session end', 'Ending session');
    }
    
    // Sync Operations
    async getSyncStatus(): Promise<SyncStatus> {
        try {
            const result = await this.exec('sync status --json');
            return JSON.parse(result) as SyncStatus;
        } catch (error) {
            return {
                connected: false,
                serverUrl: 'http://localhost:3000',
                outgoing: 0,
                incoming: 0,
                lastSync: 'Never'
            };
        }
    }
    
    async syncPush(): Promise<void> {
        await this.execWithProgress('sync push', 'Pushing patches to server');
    }
    
    async syncPull(): Promise<void> {
        await this.execWithProgress('sync pull', 'Pulling patches from server');
    }
    
    // Conflict Management
    async getConflicts(): Promise<ConflictInfo[]> {
        try {
            const result = await this.exec('conflicts list --json');
            return JSON.parse(result) as ConflictInfo[];
        } catch (error) {
            return this.getMockConflicts();
        }
    }
    
    async resolveConflict(conflictId: string, strategy: string): Promise<void> {
        await this.execWithProgress(`conflicts resolve ${conflictId} ${strategy}`, 'Resolving conflict');
    }
    
    async autoResolveConflicts(): Promise<void> {
        await this.execWithProgress('conflicts auto', 'Auto-resolving conflicts');
    }
    
    // Authentication
    async getCurrentUser(): Promise<string | null> {
        try {
            const result = await this.exec('auth whoami');
            return result.trim();
        } catch (error) {
            return null;
        }
    }
    
    async login(username: string): Promise<void> {
        await this.execWithProgress(`auth login ${username}`, 'Logging in');
    }
    
    async logout(): Promise<void> {
        await this.execWithProgress('auth logout', 'Logging out');
    }
    
    // Mock data for when CLI is not available
    private getMockPatches(): PatchInfo[] {
        return [
            {
                id: 'patch-abc123',
                author: 'stachu',
                intent: 'Add List.filterMap function',
                status: 'draft',
                createdAt: '2025-01-15T10:30:00Z',
                functions: ['Darklang.Stdlib.List.filterMap']
            },
            {
                id: 'patch-def456',
                author: 'ocean',
                intent: 'Fix String.split edge cases',
                status: 'ready',
                createdAt: '2025-01-15T09:15:00Z',
                functions: ['Darklang.Stdlib.String.split']
            },
            {
                id: 'patch-ghi789',
                author: 'stachu',
                intent: 'Update error handling',
                status: 'applied',
                createdAt: '2025-01-14T16:45:00Z',
                functions: ['Darklang.Stdlib.Result.mapError', 'Darklang.Stdlib.Result.withDefault']
            }
        ];
    }
    
    private getMockSessions(): SessionInfo[] {
        return [
            {
                id: 'session-123',
                name: 'list-improvements',
                intent: 'Improve List module functions',
                owner: 'stachu',
                status: 'active',
                patches: ['patch-abc123']
            },
            {
                id: 'session-456',
                name: 'string-fixes',
                intent: 'Fix String module edge cases',
                owner: 'stachu',
                status: 'suspended',
                patches: ['patch-def456']
            }
        ];
    }
    
    private getMockConflicts(): ConflictInfo[] {
        return [
            {
                id: 'conflict-1',
                type: 'Same Function Different Implementation',
                severity: 'high',
                description: 'Function List.filterMap modified in patches abc123 and def456',
                patches: ['patch-abc123', 'patch-def456'],
                canAutoResolve: false
            },
            {
                id: 'conflict-2',
                type: 'Name Collision',
                severity: 'medium',
                description: 'Both patches create type Result',
                patches: ['patch-ghi789', 'patch-jkl012'],
                canAutoResolve: true
            }
        ];
    }
}