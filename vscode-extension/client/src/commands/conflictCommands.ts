import * as vscode from "vscode";
import { StatusBarManager } from "../ui/statusbar/statusBarManager";
import { ConflictResolutionPanel } from "../panels";

export class ConflictCommands {
  constructor(
    private context: vscode.ExtensionContext,
    private statusBarManager: StatusBarManager
  ) {}

  register(): vscode.Disposable[] {
    return [
      vscode.commands.registerCommand("darklang.conflicts.resolve", (conflict) => {
        const conflictId = conflict?.id || "conflict-1";
        ConflictResolutionPanel.createOrShow(this.context.extensionUri, conflictId);
      }),

      vscode.commands.registerCommand("darklang.conflict.resolve", (conflict) => {
        const conflictId = conflict?.id || "conflict-1";
        ConflictResolutionPanel.createOrShow(this.context.extensionUri, conflictId);
        this.statusBarManager.updateConflicts(false); // Assume conflict resolved
      }),

      vscode.commands.registerCommand("darklang.conflicts.auto", () => {
        vscode.window.showInformationMessage("Auto-resolving simple conflicts...");
        this.statusBarManager.updateConflicts(false); // Mark conflicts as resolved
      }),

      vscode.commands.registerCommand("darklang.conflicts.list", () => {
        vscode.commands.executeCommand("darklangSessions.focus");
        vscode.window.showInformationMessage("Showing conflicts in sessions view");
      })
    ];
  }
}