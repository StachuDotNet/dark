import * as vscode from 'vscode';
import { LanguageClient, LanguageClientOptions, ServerOptions } from 'vscode-languageclient/node';
import { PatchProvider } from './providers/patchProvider';
import { SessionProvider } from './providers/sessionProvider';
import { ConflictProvider } from './providers/conflictProvider';
import { DarklangScmProvider } from './providers/scmProvider';
import { registerVirtualFileSystem } from './providers/virtualFileSystemProvider';
import { DarkCLI } from './utils/darkCli';
import { CollaborationService } from './utils/collaboration';
import { registerPatchCommands } from './commands/patchCommands';
import { registerSyncCommands } from './commands/syncCommands';
import { registerSessionCommands } from './commands/sessionCommands';
import { registerConflictCommands } from './commands/conflictCommands';
import { registerSessionTransferCommands } from './commands/sessionTransferCommands';
import { registerAiAgentCommands } from './commands/aiAgentCommands';

let statusBarItem: vscode.StatusBarItem;
let collaborationService: CollaborationService;

let client: LanguageClient;

export function activate(context: vscode.ExtensionContext) {
    console.log('Darklang Collaboration extension is now active!');

    // Start LSP client
    startLanguageClient(context);

    // Initialize CLI wrapper (for fallback operations)
    const darkCli = new DarkCLI();
    
    // Initialize collaboration service
    collaborationService = new CollaborationService(darkCli, context);
    
    // Create providers
    const patchProvider = new PatchProvider(darkCli, context);
    const sessionProvider = new SessionProvider(darkCli, context);
    const conflictProvider = new ConflictProvider(darkCli, context);
    const scmProvider = new DarklangScmProvider(darkCli, context);
    
    // Register tree views
    const patchTreeView = vscode.window.createTreeView('darklangPatches', {
        treeDataProvider: patchProvider,
        showCollapseAll: true
    });
    
    const sessionTreeView = vscode.window.createTreeView('darklangSessions', {
        treeDataProvider: sessionProvider,
        showCollapseAll: true
    });
    
    const conflictTreeView = vscode.window.createTreeView('darklangConflicts', {
        treeDataProvider: conflictProvider,
        showCollapseAll: true
    });
    
    // Register SCM provider
    const scmDisposable = vscode.scm.createSourceControl('darklang', 'Darklang', vscode.Uri.file(vscode.workspace.rootPath || ''));
    scmProvider.initialize(scmDisposable);
    
    // Create status bar item
    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
    statusBarItem.command = 'darklang.sync.status';
    context.subscriptions.push(statusBarItem);
    
    // Register commands
    registerPatchCommands(context, darkCli, patchProvider);
    registerSyncCommands(context, darkCli, collaborationService);
    registerSessionCommands(context, darkCli, sessionProvider);
    registerConflictCommands(context, darkCli, conflictProvider);
    registerSessionTransferCommands(context, darkCli);
    registerAiAgentCommands(context, darkCli);
    
    // Register refresh commands
    context.subscriptions.push(
        vscode.commands.registerCommand('darklangPatches.refresh', () => {
            patchProvider.refresh();
            sessionProvider.refresh();
            conflictProvider.refresh();
            updateStatusBar();
        })
    );
    
    // Watch for file changes to detect function modifications
    const fileWatcher = vscode.workspace.createFileSystemWatcher('**/*.dark');
    fileWatcher.onDidChange((uri) => {
        // Detect function changes and update providers
        detectFunctionChanges(uri);
        patchProvider.refresh();
        scmProvider.refresh();
    });
    
    context.subscriptions.push(fileWatcher);
    
    // Initialize status bar
    updateStatusBar();
    
    // Start collaboration service
    collaborationService.start();
    
    // Show welcome message for first-time users
    showWelcomeMessage(context);
}

function startLanguageClient(context: vscode.ExtensionContext) {
    // Server options - point to Darklang LSP server
    const serverOptions: ServerOptions = {
        run: { command: 'dark', args: ['lsp-server'] },
        debug: { command: 'dark', args: ['lsp-server', '--debug'] }
    };

    // Client options - configure collaboration capabilities
    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'darklang' }, { scheme: 'dark' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.dark')
        },
        initializationOptions: {
            // Advertise VS Code collaboration capabilities
            darklangCollaboration: {
                patchTreeView: true,
                conflictResolutionUI: true,
                sessionManagementUI: true,
                realtimeNotifications: true,
                webviewProvider: true,
                virtualFileSystem: true
            }
        }
    };

    // Create and start the language client
    client = new LanguageClient(
        'darklangLanguageServer',
        'Darklang Language Server',
        serverOptions,
        clientOptions
    );

    // Start the client and register virtual file system when ready
    client.start().then(() => {
        console.log('Darklang LSP client started');
        
        // Register virtual file system provider
        registerVirtualFileSystem(context, client);
        
        // Update providers to use LSP client instead of CLI where possible
        updateProvidersWithLspClient(client);
    });

    context.subscriptions.push(client);
}

function updateProvidersWithLspClient(lspClient: LanguageClient) {
    // Replace CLI-based operations with LSP-based ones for better performance
    // This allows real-time updates and better integration
}

export function deactivate() {
    if (collaborationService) {
        collaborationService.stop();
    }
}

async function detectFunctionChanges(uri: vscode.Uri) {
    try {
        const document = await vscode.workspace.openTextDocument(uri);
        const text = document.getText();
        
        // Simple function change detection (would be more sophisticated in real implementation)
        const functionRegex = /let\s+(\w+)\s*[\(=]/g;
        const functions = [];
        let match;
        
        while ((match = functionRegex.exec(text)) !== null) {
            functions.push({
                name: match[1],
                line: document.positionAt(match.index).line,
                uri: uri
            });
        }
        
        // Store detected functions for patch creation
        const workspaceState = vscode.workspace.getConfiguration('darklang.collaboration');
        await workspaceState.update('detectedChanges', functions, vscode.ConfigurationTarget.Workspace);
        
    } catch (error) {
        console.error('Error detecting function changes:', error);
    }
}

async function updateStatusBar() {
    try {
        const darkCli = new DarkCLI();
        
        // Get sync status
        const syncStatus = await darkCli.getSyncStatus();
        const conflicts = await darkCli.getConflicts();
        const currentSession = await darkCli.getCurrentSession();
        
        let statusText = '';
        let statusItems = [];
        
        // Sync status
        if (syncStatus.outgoing > 0 || syncStatus.incoming > 0) {
            statusItems.push(`🔄 ${syncStatus.outgoing}↑ ${syncStatus.incoming}↓`);
        }
        
        // Current session
        if (currentSession) {
            statusItems.push(`📝 ${currentSession.name}`);
        }
        
        // User
        const user = await darkCli.getCurrentUser();
        if (user) {
            statusItems.push(`👤 ${user}`);
        }
        
        // Conflicts
        if (conflicts.length > 0) {
            statusItems.push(`⚠️ ${conflicts.length} conflict${conflicts.length > 1 ? 's' : ''}`);
        }
        
        statusBarItem.text = statusItems.join(' ');
        statusBarItem.show();
        
    } catch (error) {
        statusBarItem.text = '❌ Darklang CLI not available';
        statusBarItem.show();
    }
}

function showWelcomeMessage(context: vscode.ExtensionContext) {
    const hasShownWelcome = context.globalState.get('darklang.collaboration.welcomeShown', false);
    
    if (!hasShownWelcome) {
        vscode.window.showInformationMessage(
            'Welcome to Darklang Collaboration! Create your first patch to start sharing code with your team.',
            'Create Patch',
            'Learn More'
        ).then(selection => {
            if (selection === 'Create Patch') {
                vscode.commands.executeCommand('darklang.patch.create');
            } else if (selection === 'Learn More') {
                vscode.env.openExternal(vscode.Uri.parse('https://docs.darklang.com/collaboration'));
            }
        });
        
        context.globalState.update('darklang.collaboration.welcomeShown', true);
    }
}

// Auto-refresh status bar every 30 seconds
setInterval(updateStatusBar, 30000);