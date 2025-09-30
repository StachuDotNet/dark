import * as vscode from "vscode";
import { PackageNode } from "../../types";
import { ScenarioManager } from "../../data/scenarioManager";

export class EnhancedPackageTreeItem extends vscode.TreeItem {
  constructor(
    public readonly node: PackageNode,
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

    if (node.label.includes("[MODIFIED]")) {
      return `${node.label} - Modified in current patch`;
    } else if (node.label.includes("[NEW]")) {
      return `${node.label} - New function added in current patch`;
    } else if (node.label.includes("[CONFLICT]")) {
      return `${node.label} - Has conflicts that need resolution`;
    } else if (node.label.includes("[CONFLICTS]")) {
      return `${node.label} - Module contains conflicts`;
    } else {
      return `${node.label} - Click to view or edit`;
    }
  }

  private setIcon(): void {
    const { node } = this;

    if (node.label.includes("🏢")) {
      this.iconPath = new vscode.ThemeIcon("organization", new vscode.ThemeColor("charts.orange"));
    } else if (node.label.includes("🌐")) {
      this.iconPath = new vscode.ThemeIcon("globe", new vscode.ThemeColor("charts.blue"));
    } else if (node.label.includes("📁") && node.label.includes("[CONFLICTS]")) {
      this.iconPath = new vscode.ThemeIcon("folder", new vscode.ThemeColor("charts.red"));
    } else if (node.label.includes("📁")) {
      this.iconPath = new vscode.ThemeIcon("folder", new vscode.ThemeColor("charts.blue"));
    } else if (node.label.includes("🔧") && node.label.includes("[MODIFIED]")) {
      this.iconPath = new vscode.ThemeIcon("symbol-function", new vscode.ThemeColor("charts.orange"));
    } else if (node.label.includes("🔧") && node.label.includes("[NEW]")) {
      this.iconPath = new vscode.ThemeIcon("symbol-function", new vscode.ThemeColor("charts.green"));
    } else if (node.label.includes("🔧") && node.label.includes("[CONFLICT]")) {
      this.iconPath = new vscode.ThemeIcon("symbol-function", new vscode.ThemeColor("charts.red"));
    } else if (node.label.includes("🔧")) {
      this.iconPath = new vscode.ThemeIcon("symbol-function", new vscode.ThemeColor("charts.blue"));
    } else if (node.label.includes("📋")) {
      this.iconPath = new vscode.ThemeIcon("symbol-type-parameter", new vscode.ThemeColor("charts.purple"));
    } else {
      this.iconPath = new vscode.ThemeIcon("symbol-structure");
    }
  }

  private setCommand(): void {
    const { node } = this;

    // Add command to open the definition when clicked on functions/types
    if (node.type === "function" || node.type === "type" || node.type === "constant") {
      if (node.packagePath) {
        if (node.label.includes("[NEW]") || node.label.includes("[MODIFIED]")) {
          // Open in edit mode for modified/new items
          this.command = {
            command: "darklang.openPackageForEdit",
            title: "Edit in Patch",
            arguments: [node.packagePath]
          };
        } else if (node.label.includes("[CONFLICT]")) {
          // Open conflict resolution for conflicted items
          this.command = {
            command: "darklang.conflicts.resolve",
            title: "Resolve Conflict",
            arguments: [node.packagePath]
          };
        } else {
          // Open in read-only mode for normal items
          this.command = {
            command: "darklang.openPackageDefinition",
            title: "View Definition",
            arguments: [node.packagePath]
          };
        }
      }
    }
    // Add commands for modules and namespaces
    else if (node.type === "module" && node.packagePath) {
      this.command = {
        command: "darklang.openFullModule",
        title: "View Module",
        arguments: [node]
      };
    } else if (node.type === "namespace" && node.packagePath) {
      this.command = {
        command: "darklang.package.view.namespace",
        title: "View Namespace Overview",
        arguments: [{ path: node.packagePath, label: node.label }]
      };
    }
  }
}

export class EnhancedPackagesTreeDataProvider implements vscode.TreeDataProvider<PackageNode> {
  private _onDidChangeTreeData: vscode.EventEmitter<PackageNode | undefined | null | void> =
    new vscode.EventEmitter<PackageNode | undefined | null | void>();
  readonly onDidChangeTreeData: vscode.Event<PackageNode | undefined | null | void> =
    this._onDidChangeTreeData.event;

  private data: PackageNode[] = [];
  private scenarioManager = ScenarioManager.getInstance();

  constructor() {
    this.refresh();
    // Listen for scenario changes
    this.scenarioManager.onScenarioChanged(() => {
      this.refresh();
    });
  }

  refresh(): void {
    this.data = this.scenarioManager.getScenarioData().packages;
    this._onDidChangeTreeData.fire();
  }

  getTreeItem(element: PackageNode): vscode.TreeItem {
    const collapsibleState = element.children && element.children.length > 0
      ? (element.collapsibleState === 1
          ? vscode.TreeItemCollapsibleState.Collapsed
          : vscode.TreeItemCollapsibleState.Expanded)
      : vscode.TreeItemCollapsibleState.None;

    return new EnhancedPackageTreeItem(element, collapsibleState);
  }

  getChildren(element?: PackageNode): Thenable<PackageNode[]> {
    if (!element) {
      // Return root nodes
      return Promise.resolve(this.data);
    }

    // Return children of the element
    return Promise.resolve(element.children || []);
  }

  // Removed automatic simulation - now uses ScenarioManager

  private findNode(id: string): PackageNode | undefined {
    const search = (nodes: PackageNode[]): PackageNode | undefined => {
      for (const node of nodes) {
        if (node.id === id) {
          return node;
        }
        if (node.children) {
          const found = search(node.children);
          if (found) return found;
        }
      }
      return undefined;
    };

    return search(this.data);
  }
}