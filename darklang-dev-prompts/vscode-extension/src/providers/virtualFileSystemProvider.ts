import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

/**
 * Virtual File System Provider for Darklang packages
 * Enables editing package items as virtual .dark files
 */
export class DarklangVirtualFileSystemProvider implements vscode.FileSystemProvider {
    private readonly _onDidChangeFile = new vscode.EventEmitter<vscode.FileChangeEvent[]>();
    readonly onDidChangeFile = this._onDidChangeFile.event;

    private watchers = new Map<string, vscode.Disposable>();

    constructor(private client: LanguageClient) {
        // Listen for file change notifications from LSP server
        this.client.onNotification('workspace/didChangeWatchedFiles', (params) => {
            const events: vscode.FileChangeEvent[] = params.changes.map((change: any) => ({
                type: this.mapChangeType(change.type),
                uri: vscode.Uri.parse(change.uri)
            }));
            this._onDidChangeFile.fire(events);
        });
    }

    private mapChangeType(lspType: number): vscode.FileChangeType {
        switch (lspType) {
            case 1: return vscode.FileChangeType.Created;
            case 2: return vscode.FileChangeType.Changed;
            case 3: return vscode.FileChangeType.Deleted;
            default: return vscode.FileChangeType.Changed;
        }
    }

    // FileSystemProvider interface implementation

    async stat(uri: vscode.Uri): Promise<vscode.FileStat> {
        try {
            const response = await this.client.sendRequest('darklang/vfs/stat', {
                uri: uri.toString()
            });

            return {
                type: response.isDirectory ? vscode.FileType.Directory : vscode.FileType.File,
                ctime: response.created || Date.now(),
                mtime: response.modified || Date.now(),
                size: response.size || 0
            };
        } catch (error) {
            throw vscode.FileSystemError.FileNotFound(uri);
        }
    }

    async readDirectory(uri: vscode.Uri): Promise<[string, vscode.FileType][]> {
        try {
            const response = await this.client.sendRequest('darklang/vfs/readDirectory', {
                uri: uri.toString()
            });

            return response.entries.map((entry: any) => [
                entry.name,
                entry.isDirectory ? vscode.FileType.Directory : vscode.FileType.File
            ]);
        } catch (error) {
            throw vscode.FileSystemError.FileNotFound(uri);
        }
    }

    async readFile(uri: vscode.Uri): Promise<Uint8Array> {
        try {
            const response = await this.client.sendRequest('darklang/vfs/readFile', {
                uri: uri.toString()
            });

            return Buffer.from(response.content, 'utf8');
        } catch (error) {
            throw vscode.FileSystemError.FileNotFound(uri);
        }
    }

    async writeFile(
        uri: vscode.Uri, 
        content: Uint8Array, 
        options: { create: boolean; overwrite: boolean }
    ): Promise<void> {
        try {
            const textContent = Buffer.from(content).toString('utf8');
            
            await this.client.sendRequest('darklang/vfs/writeFile', {
                uri: uri.toString(),
                content: textContent,
                options: options
            });

            this._onDidChangeFile.fire([{
                type: vscode.FileChangeType.Changed,
                uri: uri
            }]);
        } catch (error) {
            throw vscode.FileSystemError.NoPermissions(uri);
        }
    }

    async delete(uri: vscode.Uri, options: { recursive: boolean }): Promise<void> {
        try {
            await this.client.sendRequest('darklang/vfs/delete', {
                uri: uri.toString(),
                options: options
            });

            this._onDidChangeFile.fire([{
                type: vscode.FileChangeType.Deleted,
                uri: uri
            }]);
        } catch (error) {
            throw vscode.FileSystemError.NoPermissions(uri);
        }
    }

    async rename(oldUri: vscode.Uri, newUri: vscode.Uri, options: { overwrite: boolean }): Promise<void> {
        try {
            await this.client.sendRequest('darklang/vfs/rename', {
                oldUri: oldUri.toString(),
                newUri: newUri.toString(),
                options: options
            });

            this._onDidChangeFile.fire([
                { type: vscode.FileChangeType.Deleted, uri: oldUri },
                { type: vscode.FileChangeType.Created, uri: newUri }
            ]);
        } catch (error) {
            throw vscode.FileSystemError.NoPermissions(oldUri);
        }
    }

    async createDirectory(uri: vscode.Uri): Promise<void> {
        try {
            await this.client.sendRequest('darklang/vfs/createDirectory', {
                uri: uri.toString()
            });

            this._onDidChangeFile.fire([{
                type: vscode.FileChangeType.Created,
                uri: uri
            }]);
        } catch (error) {
            throw vscode.FileSystemError.NoPermissions(uri);
        }
    }

    // Watch implementation
    watch(uri: vscode.Uri, options: { recursive: boolean; excludes: string[] }): vscode.Disposable {
        const watchKey = uri.toString();
        
        // Don't create duplicate watchers
        if (this.watchers.has(watchKey)) {
            return this.watchers.get(watchKey)!;
        }

        // Send watch request to LSP server
        this.client.sendNotification('darklang/vfs/watch', {
            uri: uri.toString(),
            options: options
        });

        const disposable = new vscode.Disposable(() => {
            this.watchers.delete(watchKey);
            this.client.sendNotification('darklang/vfs/unwatch', {
                uri: uri.toString()
            });
        });

        this.watchers.set(watchKey, disposable);
        return disposable;
    }
}

/**
 * Package Tree Provider for virtual file system
 * Shows package structure in VS Code explorer
 */
export class DarklangPackageTreeProvider implements vscode.TreeDataProvider<PackageTreeItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<PackageTreeItem | undefined>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    constructor(private client: LanguageClient) {
        // Refresh tree when files change
        this.client.onNotification('darklang/packages/changed', () => {
            this._onDidChangeTreeData.fire(undefined);
        });
    }

    refresh(): void {
        this._onDidChangeTreeData.fire(undefined);
    }

    getTreeItem(element: PackageTreeItem): vscode.TreeItem {
        return element;
    }

    async getChildren(element?: PackageTreeItem): Promise<PackageTreeItem[]> {
        if (!element) {
            // Root level - show packages
            return this.getPackages();
        } else if (element.contextValue === 'package') {
            // Package level - show modules
            return this.getModules(element.label!);
        } else if (element.contextValue === 'module') {
            // Module level - show package items
            return this.getPackageItems(element.packagePath);
        } else {
            return [];
        }
    }

    private async getPackages(): Promise<PackageTreeItem[]> {
        try {
            const response = await this.client.sendRequest('darklang/packages/list', {});
            
            return response.packages.map((pkg: any) => new PackageTreeItem(
                pkg.name,
                vscode.TreeItemCollapsibleState.Collapsed,
                'package',
                pkg.name
            ));
        } catch (error) {
            return [];
        }
    }

    private async getModules(packageName: string): Promise<PackageTreeItem[]> {
        try {
            const response = await this.client.sendRequest('darklang/packages/modules', {
                packageName: packageName
            });
            
            return response.modules.map((module: any) => new PackageTreeItem(
                module.name,
                vscode.TreeItemCollapsibleState.Collapsed,
                'module',
                `${packageName}.${module.name}`
            ));
        } catch (error) {
            return [];
        }
    }

    private async getPackageItems(packagePath: string): Promise<PackageTreeItem[]> {
        try {
            const response = await this.client.sendRequest('darklang/packages/items', {
                packagePath: packagePath
            });
            
            return response.items.map((item: any) => {
                const uri = vscode.Uri.parse(`dark://package/${packagePath}/${item.name}.dark`);
                
                const treeItem = new PackageTreeItem(
                    item.name,
                    vscode.TreeItemCollapsibleState.None,
                    item.type, // 'function', 'type', 'constant', 'value'
                    packagePath,
                    uri
                );
                
                // Set icons based on item type
                treeItem.iconPath = this.getIconForItemType(item.type);
                
                // Enable opening the virtual file
                treeItem.command = {
                    command: 'vscode.open',
                    title: 'Open',
                    arguments: [uri]
                };
                
                return treeItem;
            });
        } catch (error) {
            return [];
        }
    }

    private getIconForItemType(itemType: string): vscode.ThemeIcon {
        switch (itemType) {
            case 'function':
                return new vscode.ThemeIcon('symbol-function', new vscode.ThemeColor('symbolIcon.functionForeground'));
            case 'type':
                return new vscode.ThemeIcon('symbol-class', new vscode.ThemeColor('symbolIcon.classForeground'));
            case 'constant':
                return new vscode.ThemeIcon('symbol-constant', new vscode.ThemeColor('symbolIcon.constantForeground'));
            case 'value':
                return new vscode.ThemeIcon('symbol-variable', new vscode.ThemeColor('symbolIcon.variableForeground'));
            default:
                return new vscode.ThemeIcon('symbol-misc');
        }
    }
}

class PackageTreeItem extends vscode.TreeItem {
    constructor(
        public readonly label: string,
        public readonly collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly contextValue: string,
        public readonly packagePath: string,
        public readonly resourceUri?: vscode.Uri
    ) {
        super(label, collapsibleState);
        this.tooltip = `${this.packagePath}.${this.label}`;
    }
}

/**
 * Register virtual file system and package tree provider
 */
export function registerVirtualFileSystem(
    context: vscode.ExtensionContext, 
    client: LanguageClient
): void {
    // Register virtual file system provider
    const vfsProvider = new DarklangVirtualFileSystemProvider(client);
    context.subscriptions.push(
        vscode.workspace.registerFileSystemProvider('dark', vfsProvider, {
            isCaseSensitive: true,
            isReadonly: false
        })
    );

    // Register package tree provider
    const packageTreeProvider = new DarklangPackageTreeProvider(client);
    const packageTreeView = vscode.window.createTreeView('darklangPackages', {
        treeDataProvider: packageTreeProvider,
        showCollapseAll: true
    });
    
    context.subscriptions.push(packageTreeView);

    // Register commands
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.packages.refresh', () => {
            packageTreeProvider.refresh();
        }),
        
        vscode.commands.registerCommand('darklang.packages.openWorkspace', async () => {
            const workspaceUri = vscode.Uri.parse('dark://package');
            const success = await vscode.commands.executeCommand('vscode.openFolder', workspaceUri, true);
            if (success) {
                vscode.window.showInformationMessage('Opened Darklang package workspace');
            }
        }),

        vscode.commands.registerCommand('darklang.packages.createFunction', async (packagePath?: string) => {
            const path = packagePath || await vscode.window.showInputBox({
                prompt: 'Enter package path (e.g., Darklang.Stdlib.List)',
                placeHolder: 'Package.Module.SubModule'
            });
            
            if (!path) return;
            
            const functionName = await vscode.window.showInputBox({
                prompt: 'Enter function name',
                placeHolder: 'functionName'
            });
            
            if (!functionName) return;
            
            const uri = vscode.Uri.parse(`dark://package/${path}/${functionName}.dark`);
            const doc = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(doc);
        })
    );

    // Auto-refresh when collaboration events occur
    client.onNotification('darklang/notify/patchApplied', () => {
        packageTreeProvider.refresh();
    });
    
    client.onNotification('darklang/notify/conflictResolved', () => {
        packageTreeProvider.refresh();
    });
}