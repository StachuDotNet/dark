import * as vscode from "vscode";
import { StatusBarManager } from "../ui/statusbar/statusBarManager";

export class SyncCommands {
  constructor(private statusBarManager: StatusBarManager) {}

  register(): vscode.Disposable[] {
    return [
      vscode.commands.registerCommand("darklang.sync.push", () => {
        vscode.window.showInformationMessage("Pushing patches to remote...");
        this.statusBarManager.updateSync({ outgoing: 0 });
      }),

      vscode.commands.registerCommand("darklang.sync.pull", () => {
        vscode.window.showInformationMessage("Pulling patches from remote...");
        this.statusBarManager.updateSync({ incoming: 0 });
      }),

      vscode.commands.registerCommand("darklang.sync.status", () => {
        const data = this.statusBarManager.getCurrentData();
        vscode.window.showInformationMessage(`Sync status: ${data.sync.outgoing} outgoing, ${data.sync.incoming} incoming`);
      })
    ];
  }
}