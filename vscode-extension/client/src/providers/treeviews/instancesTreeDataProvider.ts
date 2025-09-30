import * as vscode from "vscode";
import { InstanceNode } from "../../types";
import { InstanceDemoData } from "../../data/demo/instanceDemoData";

export class InstanceTreeItem extends vscode.TreeItem {
  constructor(
    public readonly node: InstanceNode,
    public readonly collapsibleState: vscode.TreeItemCollapsibleState
  ) {
    super(node.label, collapsibleState);

    this.tooltip = this.getTooltip();
    this.contextValue = node.contextValue;
    this.setIcon();
    this.setCommand();
  }

  private getTooltip(): string {
    const { node } = this;

    if (node.instanceData) {
      const data = node.instanceData;
      let tooltip = node.label;

      if (data.url) {
        tooltip += `\nURL: ${data.url}`;
      }
      if (data.path) {
        tooltip += `\nPath: ${data.path}`;
      }
      if (data.status) {
        tooltip += `\nStatus: ${data.status}`;
      }
      if (data.packageCount !== undefined) {
        tooltip += `\nPackages: ${data.packageCount}`;
      }
      if (data.sessionCount !== undefined) {
        tooltip += `\nSessions: ${data.sessionCount}`;
      }
      if (data.patchCount !== undefined) {
        tooltip += `\nPatches: ${data.patchCount}`;
      }

      return tooltip;
    }

    return node.label;
  }

  private setIcon(): void {
    const { node } = this;

    // Instance type icons
    if (node.type === "current") {
      this.iconPath = new vscode.ThemeIcon("home", new vscode.ThemeColor("charts.orange"));
    } else if (node.type === "remote") {
      const status = node.instanceData?.status;
      if (status === "connected") {
        this.iconPath = new vscode.ThemeIcon("cloud", new vscode.ThemeColor("charts.green"));
      } else if (status === "syncing") {
        this.iconPath = new vscode.ThemeIcon("sync~spin", new vscode.ThemeColor("charts.blue"));
      } else {
        this.iconPath = new vscode.ThemeIcon("cloud-outline", new vscode.ThemeColor("charts.gray"));
      }
    } else if (node.type === "local") {
      const status = node.instanceData?.status;
      if (status === "connected") {
        this.iconPath = new vscode.ThemeIcon("folder", new vscode.ThemeColor("charts.blue"));
      } else {
        this.iconPath = new vscode.ThemeIcon("folder-outline", new vscode.ThemeColor("charts.gray"));
      }
    }
    // Content type icons
    else if (node.type === "packages") {
      this.iconPath = new vscode.ThemeIcon("package", new vscode.ThemeColor("charts.blue"));
    } else if (node.type === "sessions") {
      this.iconPath = new vscode.ThemeIcon("target", new vscode.ThemeColor("charts.purple"));
    } else if (node.type === "patches") {
      this.iconPath = new vscode.ThemeIcon("git-branch", new vscode.ThemeColor("charts.green"));
    }
    // Category and action icons
    else if (node.contextValue === "remote-category") {
      this.iconPath = new vscode.ThemeIcon("globe", new vscode.ThemeColor("charts.blue"));
    } else if (node.contextValue === "local-category") {
      this.iconPath = new vscode.ThemeIcon("file-directory", new vscode.ThemeColor("charts.blue"));
    } else if (node.contextValue === "instance-actions-category") {
      this.iconPath = new vscode.ThemeIcon("tools", new vscode.ThemeColor("charts.purple"));
    } else if (node.contextValue === "connect-instance") {
      this.iconPath = new vscode.ThemeIcon("plug", new vscode.ThemeColor("charts.green"));
    } else if (node.contextValue === "browse-remote") {
      this.iconPath = new vscode.ThemeIcon("search", new vscode.ThemeColor("charts.orange"));
    } else if (node.contextValue === "package-namespace") {
      this.iconPath = new vscode.ThemeIcon("symbol-namespace", new vscode.ThemeColor("charts.blue"));
    } else if (node.contextValue === "remote-session") {
      this.iconPath = new vscode.ThemeIcon("target", new vscode.ThemeColor("charts.yellow"));
    } else if (node.contextValue === "patch-category") {
      this.iconPath = new vscode.ThemeIcon("git-branch", new vscode.ThemeColor("charts.green"));
    } else {
      this.iconPath = new vscode.ThemeIcon("circle-large-outline");
    }
  }

  private setCommand(): void {
    const { node } = this;

    // Instance connection commands
    if (node.contextValue === "remote-instance" || node.contextValue === "local-instance") {
      const isConnected = node.instanceData?.status === "connected";
      this.command = {
        command: isConnected ? "darklang.instance.browse" : "darklang.instance.connect",
        title: isConnected ? "Browse Instance" : "Connect to Instance",
        arguments: [node]
      };
    }
    // Action commands
    else if (node.contextValue === "connect-instance") {
      this.command = {
        command: "darklang.instance.connect.new",
        title: "Connect to New Instance",
        arguments: [node]
      };
    } else if (node.contextValue === "browse-remote") {
      this.command = {
        command: "darklang.instance.browse.remote",
        title: "Browse Remote Instances",
        arguments: [node]
      };
    }
    // Content browsing commands
    else if (node.contextValue === "remote-packages" || node.contextValue === "local-packages" || node.contextValue === "current-packages") {
      this.command = {
        command: "darklang.instance.browse.packages",
        title: "Browse Packages",
        arguments: [node]
      };
    } else if (node.contextValue === "remote-sessions" || node.contextValue === "current-sessions") {
      this.command = {
        command: "darklang.instance.browse.sessions",
        title: "Browse Sessions",
        arguments: [node]
      };
    } else if (node.contextValue === "remote-patches" || node.contextValue === "local-patches") {
      this.command = {
        command: "darklang.instance.browse.patches",
        title: "Browse Patches",
        arguments: [node]
      };
    }
    // Namespace and specific content commands
    else if (node.contextValue === "package-namespace") {
      this.command = {
        command: "darklang.instance.view.namespace",
        title: "View Package Namespace",
        arguments: [node]
      };
    } else if (node.contextValue === "remote-session") {
      this.command = {
        command: "darklang.instance.view.session",
        title: "View Remote Session",
        arguments: [node]
      };
    } else if (node.contextValue === "patch-category") {
      this.command = {
        command: "darklang.instance.view.patches",
        title: "View Patch Category",
        arguments: [node]
      };
    }
  }
}

export class InstancesTreeDataProvider implements vscode.TreeDataProvider<InstanceNode> {
  private _onDidChangeTreeData: vscode.EventEmitter<InstanceNode | undefined | null | void> =
    new vscode.EventEmitter<InstanceNode | undefined | null | void>();
  readonly onDidChangeTreeData: vscode.Event<InstanceNode | undefined | null | void> =
    this._onDidChangeTreeData.event;

  private data: InstanceNode[] = [];

  constructor() {
    this.refresh();
  }

  refresh(): void {
    this.data = InstanceDemoData.getInstancesData();
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: InstanceNode): vscode.TreeItem {
    let collapsibleState = vscode.TreeItemCollapsibleState.None;

    if (element.children && element.children.length > 0) {
      // Actions should be collapsed by default
      if (element.contextValue === "instance-actions") {
        collapsibleState = vscode.TreeItemCollapsibleState.Collapsed;
      } else {
        collapsibleState = vscode.TreeItemCollapsibleState.Expanded;
      }
    }

    return new InstanceTreeItem(element, collapsibleState);
  }

  getChildren(element?: InstanceNode): Thenable<InstanceNode[]> {
    if (!element) {
      // Return root nodes
      return Promise.resolve(this.data);
    }

    // Return children of the element
    return Promise.resolve(element.children || []);
  }

  connectToInstance(instanceId: string): void {
    // Find and update instance status
    const updateInstanceStatus = (nodes: InstanceNode[]): void => {
      for (const node of nodes) {
        if (node.id === instanceId && node.instanceData) {
          node.instanceData.status = "connected";
          break;
        }
        if (node.children) {
          updateInstanceStatus(node.children);
        }
      }
    };

    updateInstanceStatus(this.data);
    this._onDidChangeTreeData.fire(undefined);
  }

  syncInstance(instanceId: string): void {
    // Find and update instance status to syncing, then back to connected
    const updateInstanceStatus = (nodes: InstanceNode[], status: "syncing" | "connected"): void => {
      for (const node of nodes) {
        if (node.id === instanceId && node.instanceData) {
          node.instanceData.status = status;
          break;
        }
        if (node.children) {
          updateInstanceStatus(node.children, status);
        }
      }
    };

    updateInstanceStatus(this.data, "syncing");
    this._onDidChangeTreeData.fire(undefined);

    // Simulate sync completion after 3 seconds
    setTimeout(() => {
      updateInstanceStatus(this.data, "connected");
      this._onDidChangeTreeData.fire(undefined);
    }, 3000);
  }
}