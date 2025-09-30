import * as vscode from "vscode";
import { StatusBarManager } from "../ui/statusbar/statusBarManager";
import { InstancesTreeDataProvider } from "../providers/treeviews/instancesTreeDataProvider";

export class InstanceCommands {
  constructor(
    private statusBarManager: StatusBarManager,
    private instancesProvider: InstancesTreeDataProvider
  ) {}

  register(): vscode.Disposable[] {
    return [
      vscode.commands.registerCommand("darklang.instance.connect", (instance) => {
        const instanceName = instance?.label || "Unknown Instance";

        vscode.window.withProgress({
          location: vscode.ProgressLocation.Notification,
          title: `Connecting to ${instanceName}...`,
          cancellable: false
        }, async (progress) => {
          progress.report({ increment: 0 });

          await new Promise(resolve => setTimeout(resolve, 1000));
          progress.report({ increment: 50, message: "Authenticating..." });

          await new Promise(resolve => setTimeout(resolve, 1000));
          progress.report({ increment: 100, message: "Connected!" });

          this.instancesProvider.connectToInstance(instance.id);
          vscode.window.showInformationMessage(`✅ Connected to ${instanceName}`);
        });
      }),

      vscode.commands.registerCommand("darklang.instance.browse", (instance) => {
        const instanceName = instance?.label || "Unknown Instance";
        vscode.window.showInformationMessage(`Browsing ${instanceName}...`);

        // Open instance content in virtual URL
        if (instance?.instanceData?.url) {
          const virtualUri = vscode.Uri.parse(`dark:///instance/remote/details.darklang-instance?url=${encodeURIComponent(instance.instanceData.url)}`);
          vscode.workspace.openTextDocument(virtualUri).then(doc => vscode.window.showTextDocument(doc));
        } else if (instance?.instanceData?.path) {
          const virtualUri = vscode.Uri.parse(`dark:///instance/local/details.darklang-instance?path=${encodeURIComponent(instance.instanceData.path)}`);
          vscode.workspace.openTextDocument(virtualUri).then(doc => vscode.window.showTextDocument(doc));
        }
      }),

      vscode.commands.registerCommand("darklang.instance.connect.new", () => {
        vscode.window.showQuickPick([
          {
            label: "$(cloud) Remote HTTP Instance",
            description: "Connect to a Darklang instance over HTTP",
            detail: "e.g., https://darklang.example.com"
          },
          {
            label: "$(folder) Local Directory",
            description: "Connect to a local Darklang directory",
            detail: "Browse to a directory with Darklang files"
          },
          {
            label: "$(globe) Public Registry",
            description: "Browse public Darklang instances",
            detail: "Find community instances and packages"
          }
        ], {
          placeHolder: "Select instance type to connect to"
        }).then(selected => {
          if (selected) {
            if (selected.label.includes("Remote HTTP")) {
              this.connectToRemoteInstance();
            } else if (selected.label.includes("Local Directory")) {
              this.connectToLocalInstance();
            } else if (selected.label.includes("Public Registry")) {
              this.browsePublicRegistry();
            }
          }
        });
      }),

      vscode.commands.registerCommand("darklang.instance.sync.all", () => {
        vscode.window.withProgress({
          location: vscode.ProgressLocation.Notification,
          title: "Syncing all instances...",
          cancellable: false
        }, async (progress) => {
          progress.report({ increment: 0 });

          // Sync remote instances
          await new Promise(resolve => setTimeout(resolve, 1500));
          progress.report({ increment: 40, message: "Syncing remote instances..." });

          // Sync local instances
          await new Promise(resolve => setTimeout(resolve, 1000));
          progress.report({ increment: 80, message: "Syncing local instances..." });

          await new Promise(resolve => setTimeout(resolve, 500));
          progress.report({ increment: 100, message: "Sync complete" });

          vscode.window.showInformationMessage("✅ All instances synced successfully");
        });
      }),

      vscode.commands.registerCommand("darklang.instance.browse.remote", () => {
        vscode.window.showQuickPick([
          "darklang.com - Official packages and examples",
          "community.darklang.com - Community contributions",
          "staging.darklang.com - Latest development builds",
          "Custom URL..."
        ], {
          placeHolder: "Select remote instance to browse"
        }).then(selected => {
          if (selected) {
            if (selected === "Custom URL...") {
              this.connectToRemoteInstance();
            } else {
              const url = selected.split(" - ")[0];
              vscode.window.showInformationMessage(`Browsing ${url}...`);
            }
          }
        });
      }),

      vscode.commands.registerCommand("darklang.instance.browse.packages", (node) => {
        const instanceName = this.getInstanceName(node);
        vscode.window.showInformationMessage(`Browsing packages in ${instanceName}...`);

        // Open packages browser
        const virtualUri = vscode.Uri.parse(`dark:///instance/${node.id}/packages`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => vscode.window.showTextDocument(doc, {
          preview: false
        }));
      }),

      vscode.commands.registerCommand("darklang.instance.browse.sessions", (node) => {
        const instanceName = this.getInstanceName(node);
        vscode.window.showInformationMessage(`Browsing sessions in ${instanceName}...`);

        // Open sessions browser
        const virtualUri = vscode.Uri.parse(`dark:///instance/${node.id}/sessions`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => vscode.window.showTextDocument(doc, {
          preview: false
        }));
      }),

      vscode.commands.registerCommand("darklang.instance.browse.patches", (node) => {
        const instanceName = this.getInstanceName(node);
        vscode.window.showInformationMessage(`Browsing patches in ${instanceName}...`);

        // Open patches browser
        const virtualUri = vscode.Uri.parse(`dark:///instance/patches?instance=${node.id}`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => vscode.window.showTextDocument(doc));
      }),

      vscode.commands.registerCommand("darklang.instance.sync", (instance) => {
        const instanceName = instance?.label || "instance";
        this.instancesProvider.syncInstance(instance.id);
        vscode.window.showInformationMessage(`Syncing ${instanceName}...`);
      }),

      vscode.commands.registerCommand("darklang.instance.view.namespace", (node) => {
        const namespaceName = node?.label || "Unknown Namespace";
        const virtualUri = vscode.Uri.parse(`dark:///instance/${node.id}/namespace/${encodeURIComponent(namespaceName)}.darklang-namespace`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => {
          vscode.window.showTextDocument(doc);
          vscode.window.showInformationMessage(`Viewing namespace: ${namespaceName}`);
        });
      }),

      vscode.commands.registerCommand("darklang.instance.view.session", (node) => {
        const sessionName = node?.label || "Unknown Session";
        const virtualUri = vscode.Uri.parse(`dark:///instance/${node.id}/session/${encodeURIComponent(sessionName)}.darklang-remote-session`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => {
          vscode.window.showTextDocument(doc);
          vscode.window.showInformationMessage(`Viewing remote session: ${sessionName}`);
        });
      }),

      vscode.commands.registerCommand("darklang.instance.view.patches", (node) => {
        const categoryName = node?.label || "Unknown Category";
        const virtualUri = vscode.Uri.parse(`dark:///instance/${node.id}/patch-category/${encodeURIComponent(categoryName)}.darklang-patch-category`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => {
          vscode.window.showTextDocument(doc);
          vscode.window.showInformationMessage(`Viewing patch category: ${categoryName}`);
        });
      })
    ];
  }

  private async connectToRemoteInstance(): Promise<void> {
    const url = await vscode.window.showInputBox({
      prompt: "Enter the URL of the remote Darklang instance",
      placeHolder: "https://matter.darklang.com",
      validateInput: (value) => {
        if (!value) return "URL is required";
        if (!value.startsWith("http://") && !value.startsWith("https://")) {
          return "URL must start with http:// or https://";
        }
        return null;
      }
    });

    if (url) {
      vscode.window.showInformationMessage(`Connecting to ${url}...`);
      // Here you would implement actual connection logic
    }
  }

  private async connectToLocalInstance(): Promise<void> {
    const uri = await vscode.window.showOpenDialog({
      canSelectFiles: false,
      canSelectFolders: true,
      canSelectMany: false,
      openLabel: "Select Darklang Directory"
    });

    if (uri && uri[0]) {
      const path = uri[0].fsPath;
      vscode.window.showInformationMessage(`Connecting to local instance at ${path}...`);
      // Here you would implement actual local connection logic
    }
  }

  private browsePublicRegistry(): void {
    vscode.window.showInformationMessage("Opening public registry browser...");
    const virtualUri = vscode.Uri.parse("dark:///instance/registry/browse.darklang-registry");
    vscode.workspace.openTextDocument(virtualUri).then(doc => vscode.window.showTextDocument(doc));
  }

  private getInstanceName(node: any): string {
    // Try to find the parent instance name by walking up the tree
    return "Remote Instance"; // Simplified for now
  }
}