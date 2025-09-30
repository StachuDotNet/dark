import * as vscode from "vscode";
import { DemoDataProvider, SessionNode } from "../../data/demoData";
import { ScenarioManager } from "../../data/scenarioManager";

export class SessionTreeItem extends vscode.TreeItem {
  constructor(
    public readonly node: SessionNode,
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
    switch (node.type) {
      case "current":
        return "Currently active development session";
      case "recent":
        return node.children ? "Recently used sessions" : `Recent session: ${node.label}`;
      case "shared":
        return node.children ? "Shared team sessions" : `Shared session: ${node.label}`;
      case "actions":
        return node.children ? "Session management actions" : `Action: ${node.label}`;
      default:
        return node.label;
    }
  }

  private setIcon(): void {
    const { node } = this;

    // Session management icons
    if (node.contextValue === "session-management") {
      this.iconPath = new vscode.ThemeIcon("target", new vscode.ThemeColor("charts.orange"));
    } else if (node.contextValue === "current-session-info") {
      this.iconPath = new vscode.ThemeIcon("star-full", new vscode.ThemeColor("charts.orange"));
    } else if (node.contextValue === "switch-session-category") {
      this.iconPath = new vscode.ThemeIcon("arrow-swap", new vscode.ThemeColor("charts.blue"));
    } else if (node.contextValue === "patch-management-category") {
      this.iconPath = new vscode.ThemeIcon("git-branch", new vscode.ThemeColor("charts.blue"));
    } else if (node.contextValue === "create-patch") {
      this.iconPath = new vscode.ThemeIcon("add", new vscode.ThemeColor("charts.green"));
    } else if (node.contextValue === "pull-patches") {
      this.iconPath = new vscode.ThemeIcon("arrow-down", new vscode.ThemeColor("charts.blue"));
    } else if (node.contextValue === "sync-patches") {
      this.iconPath = new vscode.ThemeIcon("sync", new vscode.ThemeColor("charts.purple"));
    }
    // Patch-level icons
    else if (node.type === "patch") {
      if (node.contextValue === "current-patch") {
        this.iconPath = new vscode.ThemeIcon("git-branch", new vscode.ThemeColor("charts.orange"));
      } else if (node.contextValue?.includes("draft")) {
        this.iconPath = new vscode.ThemeIcon("edit", new vscode.ThemeColor("charts.blue"));
      } else if (node.contextValue?.includes("incoming")) {
        this.iconPath = new vscode.ThemeIcon("inbox", new vscode.ThemeColor("charts.green"));
      } else if (node.contextValue === "empty-state") {
        this.iconPath = new vscode.ThemeIcon("circle-large-outline", new vscode.ThemeColor("charts.gray"));
      } else {
        this.iconPath = new vscode.ThemeIcon("git-branch", new vscode.ThemeColor("charts.purple"));
      }
    }
    // Operation and conflict icons
    else if (node.type === "operation") {
      if (node.contextValue === "operations-category") {
        this.iconPath = new vscode.ThemeIcon("list-ordered", new vscode.ThemeColor("charts.blue"));
      } else if (node.contextValue === "tests-category") {
        this.iconPath = new vscode.ThemeIcon("beaker", new vscode.ThemeColor("charts.green"));
      } else if (node.label.includes("Modified:")) {
        this.iconPath = new vscode.ThemeIcon("edit", new vscode.ThemeColor("charts.yellow"));
      } else if (node.label.includes("Added:")) {
        this.iconPath = new vscode.ThemeIcon("add", new vscode.ThemeColor("charts.green"));
      } else if (node.label.includes("Updated:")) {
        this.iconPath = new vscode.ThemeIcon("gear", new vscode.ThemeColor("charts.blue"));
      } else {
        this.iconPath = new vscode.ThemeIcon("symbol-method", new vscode.ThemeColor("charts.blue"));
      }
    }
    else if (node.type === "conflict") {
      if (node.contextValue === "conflicts-category") {
        this.iconPath = new vscode.ThemeIcon("warning", new vscode.ThemeColor("charts.red"));
      } else {
        this.iconPath = new vscode.ThemeIcon("error", new vscode.ThemeColor("charts.red"));
      }
    }
    // Session context icons
    else if (node.contextValue === "session") {
      this.iconPath = new vscode.ThemeIcon("target", new vscode.ThemeColor("charts.yellow"));
    } else {
      this.iconPath = new vscode.ThemeIcon("circle-large-outline");
    }
  }

  private setCommand(): void {
    const { node } = this;

    // Session management commands
    if (node.contextValue === "current-session-info") {
      this.command = {
        command: "darklang.session.view",
        title: "View Session Details",
        arguments: [{ id: "current", label: node.label }]
      };
    } else if (node.contextValue === "session" && !node.children) {
      this.command = {
        command: "darklang.session.switch",
        title: "Switch to Session",
        arguments: [node]
      };
    }
    // Category navigation commands
    else if (node.contextValue === "operations-category") {
      this.command = {
        command: "darklang.patch.view.operations",
        title: "View Operations",
        arguments: [{ patchId: "current", view: "operations" }]
      };
    } else if (node.contextValue === "conflicts-category") {
      this.command = {
        command: "darklang.patch.view.conflicts",
        title: "View Conflicts",
        arguments: [{ patchId: "current", view: "conflicts" }]
      };
    } else if (node.contextValue === "tests-category") {
      this.command = {
        command: "darklang.patch.view.tests",
        title: "View Tests",
        arguments: [{ patchId: "current", view: "tests" }]
      };
    } else if (node.contextValue === "test") {
      this.command = {
        command: "darklang.test.view",
        title: "View Test Details",
        arguments: [{ testName: node.label, patchId: "current" }]
      };
    }
    // Patch-related commands
    else if (node.contextValue === "current-patch" || node.contextValue === "draft-patch" || node.contextValue === "incoming-patch") {
      this.command = {
        command: "darklang.patch.view",
        title: "View Patch",
        arguments: [{ id: node.id, intent: node.patchData?.intent }]
      };
    }
    // Operation commands - don't open tabs, but could open patch URL somehow
    else if (node.contextValue === "operation" && !node.children) {
      // Operations don't open tabs - leave command undefined
      // Maybe add a right-click context menu option to open patch URL later
    }
    // Conflict commands
    else if (node.contextValue === "conflict" && !node.children) {
      this.command = {
        command: "darklang.conflict.resolve",
        title: "Resolve Conflict",
        arguments: [node]
      };
    }
    // Patch management commands
    else if (node.contextValue === "create-patch") {
      this.command = {
        command: "darklang.patch.create",
        title: "Create New Patch",
        arguments: [node]
      };
    } else if (node.contextValue === "pull-patches") {
      this.command = {
        command: "darklang.patch.pull",
        title: "Pull Available Patches",
        arguments: [node]
      };
    } else if (node.contextValue === "sync-patches") {
      this.command = {
        command: "darklang.patch.sync",
        title: "Sync with Remote",
        arguments: [node]
      };
    }
  }
}

export class SessionsTreeDataProvider implements vscode.TreeDataProvider<SessionNode> {
  private _onDidChangeTreeData: vscode.EventEmitter<SessionNode | undefined | null | void> =
    new vscode.EventEmitter<SessionNode | undefined | null | void>();
  readonly onDidChangeTreeData: vscode.Event<SessionNode | undefined | null | void> =
    this._onDidChangeTreeData.event;

  private data: SessionNode[] = [];
  private scenarioManager = ScenarioManager.getInstance();

  constructor() {
    this.refresh();

    // Listen for scenario changes and refresh the tree
    this.scenarioManager.onScenarioChanged(() => {
      this.refresh();
    });
  }

  refresh(): void {
    this.data = DemoDataProvider.getSessionsData();
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: SessionNode): vscode.TreeItem {
    const collapsibleState = element.children && element.children.length > 0
      ? vscode.TreeItemCollapsibleState.Expanded
      : vscode.TreeItemCollapsibleState.None;

    return new SessionTreeItem(element, collapsibleState);
  }

  getChildren(element?: SessionNode): Thenable<SessionNode[]> {
    if (!element) {
      // Return root nodes
      return Promise.resolve(this.data);
    }

    // Return children of the element
    return Promise.resolve(element.children || []);
  }

  // Sessions are now managed via ScenarioManager, no need for manual switching
}