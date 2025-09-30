import * as vscode from "vscode";
import { ServerBackedTreeDataProvider } from "../providers/treeviews/ServerBackedTreeDataProvider";
import { EnhancedPackagesTreeDataProvider } from "../providers/treeviews/enhancedPackagesTreeDataProvider";

export class RefreshCommands {
  constructor(
    private legacyTreeDataProvider: ServerBackedTreeDataProvider | null,
    private packagesProvider: EnhancedPackagesTreeDataProvider
  ) {}

  register(): vscode.Disposable[] {
    const commands = [
      vscode.commands.registerCommand('darklang.packages.refresh', () => {
        this.packagesProvider.refresh();
      })
    ];

    // Only register legacy refresh if provider exists
    if (this.legacyTreeDataProvider) {
      commands.push(
        vscode.commands.registerCommand('darklang.refreshTreeView', () => {
          this.legacyTreeDataProvider!.refresh();
        })
      );
    }

    return commands;
  }
}