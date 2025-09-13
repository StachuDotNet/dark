import * as vscode from 'vscode';
import { DarkCLI, PatchInfo } from '../utils/darkCli';

export class PatchProvider implements vscode.TreeDataProvider<PatchItem> {
    private _onDidChangeTreeData: vscode.EventEmitter<PatchItem | undefined | null | void> = new vscode.EventEmitter<PatchItem | undefined | null | void>();
    readonly onDidChangeTreeData: vscode.Event<PatchItem | undefined | null | void> = this._onDidChangeTreeData.event;

    private patches: PatchInfo[] = [];

    constructor(
        private darkCli: DarkCLI,
        private context: vscode.ExtensionContext
    ) {
        this.loadPatches();
    }

    refresh(): void {
        this.loadPatches();
        this._onDidChangeTreeData.fire();
    }

    getTreeItem(element: PatchItem): vscode.TreeItem {
        return element;
    }

    getChildren(element?: PatchItem): Thenable<PatchItem[]> {
        if (!element) {
            // Root level - show categories
            return Promise.resolve([
                new PatchItem('Draft Patches', vscode.TreeItemCollapsibleState.Expanded, 'category'),
                new PatchItem('Ready Patches', vscode.TreeItemCollapsibleState.Expanded, 'category'),
                new PatchItem('Incoming Patches', vscode.TreeItemCollapsibleState.Expanded, 'category'),
                new PatchItem('Applied Patches', vscode.TreeItemCollapsibleState.Collapsed, 'category')
            ]);
        } else {
            // Category level - show patches
            return Promise.resolve(this.getPatchesForCategory(element.label as string));
        }
    }

    private async loadPatches(): Promise<void> {
        try {
            this.patches = await this.darkCli.getPatches();
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to load patches: ${error}`);
            this.patches = [];
        }
    }

    private getPatchesForCategory(category: string): PatchItem[] {
        let filteredPatches: PatchInfo[] = [];
        
        switch (category) {
            case 'Draft Patches':
                filteredPatches = this.patches.filter(p => p.status === 'draft');
                break;
            case 'Ready Patches':
                filteredPatches = this.patches.filter(p => p.status === 'ready');
                break;
            case 'Incoming Patches':
                // In real implementation, would filter by author != current user
                filteredPatches = this.patches.filter(p => p.status === 'ready' && p.author !== 'stachu');
                break;
            case 'Applied Patches':
                filteredPatches = this.patches.filter(p => p.status === 'applied');
                break;
        }

        return filteredPatches.map(patch => {
            const item = new PatchItem(
                `${patch.id.substring(0, 8)}: ${patch.intent}`,
                vscode.TreeItemCollapsibleState.None,
                this.getPatchContextValue(patch)
            );
            
            item.description = `by ${patch.author}`;
            item.tooltip = this.createPatchTooltip(patch);
            item.command = {
                command: 'darklang.patch.view',
                title: 'View Patch',
                arguments: [patch.id]
            };
            
            // Set icons based on status
            item.iconPath = this.getPatchIcon(patch);
            
            return item;
        });
    }

    private getPatchContextValue(patch: PatchInfo): string {
        switch (patch.status) {
            case 'draft':
                return 'draftPatch';
            case 'ready':
                return patch.author !== 'stachu' ? 'incomingPatch' : 'readyPatch';
            case 'applied':
                return 'appliedPatch';
            default:
                return 'patch';
        }
    }

    private getPatchIcon(patch: PatchInfo): vscode.ThemeIcon {
        switch (patch.status) {
            case 'draft':
                return new vscode.ThemeIcon('edit', new vscode.ThemeColor('charts.yellow'));
            case 'ready':
                return new vscode.ThemeIcon('check', new vscode.ThemeColor('charts.green'));
            case 'applied':
                return new vscode.ThemeIcon('check-all', new vscode.ThemeColor('charts.blue'));
            default:
                return new vscode.ThemeIcon('file');
        }
    }

    private createPatchTooltip(patch: PatchInfo): vscode.MarkdownString {
        const tooltip = new vscode.MarkdownString();
        tooltip.appendMarkdown(`**${patch.intent}**\n\n`);
        tooltip.appendMarkdown(`- **Author:** ${patch.author}\n`);
        tooltip.appendMarkdown(`- **Status:** ${patch.status}\n`);
        tooltip.appendMarkdown(`- **Created:** ${new Date(patch.createdAt).toLocaleDateString()}\n`);
        tooltip.appendMarkdown(`- **Functions:** ${patch.functions.join(', ')}\n`);
        
        if (patch.status === 'draft') {
            tooltip.appendMarkdown(`\n*Click to view details or use context menu to mark ready*`);
        } else if (patch.status === 'ready' && patch.author !== 'stachu') {
            tooltip.appendMarkdown(`\n*Click to view details or use context menu to apply*`);
        }
        
        return tooltip;
    }

    // Get patches by status for external use
    getDraftPatches(): PatchInfo[] {
        return this.patches.filter(p => p.status === 'draft');
    }

    getReadyPatches(): PatchInfo[] {
        return this.patches.filter(p => p.status === 'ready');
    }

    getIncomingPatches(): PatchInfo[] {
        return this.patches.filter(p => p.status === 'ready' && p.author !== 'stachu');
    }
}

class PatchItem extends vscode.TreeItem {
    constructor(
        public readonly label: string,
        public readonly collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly contextValue: string
    ) {
        super(label, collapsibleState);
    }
}