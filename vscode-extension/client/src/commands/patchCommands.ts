import * as vscode from "vscode";
import { StatusBarManager } from "../ui/statusbar/statusBarManager";
import { PatchReviewPanel } from "../panels";

export class PatchCommands {
  constructor(
    private context: vscode.ExtensionContext,
    private statusBarManager: StatusBarManager
  ) {}

  register(): vscode.Disposable[] {
    return [
      vscode.commands.registerCommand("darklang.patch.create", () => {
        vscode.window.showInformationMessage("Creating new patch...");
        // TODO: Implement patch creation
      }),

      vscode.commands.registerCommand("darklang.patch.ready", () => {
        vscode.window.showInformationMessage("Marking patch as ready...");
        this.statusBarManager.updatePatch("Ready for review");
      }),

      vscode.commands.registerCommand("darklang.patch.apply", (patch) => {
        vscode.window.showInformationMessage(`Applying patch: ${patch?.intent || "selected patch"}`);
      }),

      vscode.commands.registerCommand("darklang.patch.view", (patch) => {
        const patchId = patch?.id || "abc123";
        const virtualUri = vscode.Uri.parse(`dark:///patch/${patchId}`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => vscode.window.showTextDocument(doc));
      }),

      vscode.commands.registerCommand("darklang.patch.review", (patch) => {
        const patchId = patch?.id || "abc123";
        PatchReviewPanel.createOrShow(this.context.extensionUri, patchId);
      }),

      vscode.commands.registerCommand("darklang.patch.export", (patch) => {
        vscode.window.showInformationMessage(`Exporting patch: ${patch?.intent || "selected patch"}`);
      }),

      vscode.commands.registerCommand("darklang.patch.delete", (patch) => {
        vscode.window.showWarningMessage(`Delete patch: ${patch?.intent || "selected patch"}?`, "Yes", "No")
          .then(choice => {
            if (choice === "Yes") {
              vscode.window.showInformationMessage("Patch deleted");
            }
          });
      }),

      vscode.commands.registerCommand("darklang.patch.pull", () => {
        vscode.window.showQuickPick([
          "alice: Enhanced email validation (Ready)",
          "bob: Phone validation patterns (Ready)",
          "charlie: Backend auth endpoints (Ready)",
          "diana: Database auth schema (Applied)",
          "eve: Auth middleware (Ready)"
        ], {
          placeHolder: "Select patches to pull into current session",
          canPickMany: true
        }).then(selected => {
          if (selected && selected.length > 0) {
            vscode.window.showInformationMessage(`Pulled ${selected.length} patch(es) into current session`);
            this.statusBarManager.updatePatch("Multiple patches pulled");
          }
        });
      }),

      vscode.commands.registerCommand("darklang.patch.sync", () => {
        vscode.window.withProgress({
          location: vscode.ProgressLocation.Notification,
          title: "Syncing patches with remote...",
          cancellable: false
        }, async (progress) => {
          progress.report({ increment: 0 });

          // Simulate sync process
          await new Promise(resolve => setTimeout(resolve, 1000));
          progress.report({ increment: 50, message: "Fetching remote patches..." });

          await new Promise(resolve => setTimeout(resolve, 1000));
          progress.report({ increment: 100, message: "Sync complete" });

          vscode.window.showInformationMessage("✅ Synced with remote. 2 new patches available.");
        });
      }),

      vscode.commands.registerCommand("darklang.patch.view.operations", (args) => {
        const patchId = args?.patchId || "current";
        const virtualUri = vscode.Uri.parse(`dark:///patch/${patchId}/operations`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => vscode.window.showTextDocument(doc, {
          preview: false
        }));
      }),

      vscode.commands.registerCommand("darklang.patch.view.conflicts", (args) => {
        const patchId = args?.patchId || "current";
        const virtualUri = vscode.Uri.parse(`dark:///patch/${patchId}/conflicts`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => vscode.window.showTextDocument(doc, {
          preview: false
        }));
      }),

      vscode.commands.registerCommand("darklang.patch.view.tests", (args) => {
        const patchId = args?.patchId || "current";
        const virtualUri = vscode.Uri.parse(`dark:///patch/${patchId}/tests`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => vscode.window.showTextDocument(doc, {
          preview: false
        }));
      }),

      vscode.commands.registerCommand("darklang.test.view", (args) => {
        const testName = args?.testName || "unknown";
        const patchId = args?.patchId || "current";
        const displayName = testName.replace(/_/g, ' ');
        const virtualUri = vscode.Uri.parse(`dark:///patch/${patchId}/test?name=${testName}`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => vscode.window.showTextDocument(doc, {
          preview: false
        }));
      })
    ];
  }
}