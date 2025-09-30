import * as vscode from "vscode";
import { DemoDataProvider, ConflictNode } from "../../data/demoData";

export class ConflictTreeItem extends vscode.TreeItem {
  constructor(
    public readonly node: ConflictNode,
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
      case "session-summary":
        return "Current session conflict summary";
      case "patch":
        return `Patch status: ${node.status} - ${node.label}`;
      case "conflict-item":
        return "Function with merge conflicts - click to resolve";
      case "resolution":
        return "Conflict resolution option";
      default:
        return node.label;
    }
  }

  private setIcon(): void {
    const { node } = this;

    if (node.label.includes("🔄")) {
      this.iconPath = new vscode.ThemeIcon("sync", new vscode.ThemeColor("charts.blue"));
    } else if (node.label.includes("📊")) {
      this.iconPath = new vscode.ThemeIcon("graph", new vscode.ThemeColor("charts.purple"));
    } else if (node.label.includes("✅")) {
      this.iconPath = new vscode.ThemeIcon("check", new vscode.ThemeColor("charts.green"));
    } else if (node.label.includes("⚠️")) {
      this.iconPath = new vscode.ThemeIcon("warning", new vscode.ThemeColor("charts.orange"));
    } else if (node.label.includes("📝")) {
      this.iconPath = new vscode.ThemeIcon("edit", new vscode.ThemeColor("charts.yellow"));
    } else if (node.label.includes("🚫")) {
      this.iconPath = new vscode.ThemeIcon("error", new vscode.ThemeColor("charts.red"));
    } else if (node.label.includes("💡")) {
      this.iconPath = new vscode.ThemeIcon("lightbulb", new vscode.ThemeColor("charts.yellow"));
    } else if (node.label.includes("🔧")) {
      this.iconPath = new vscode.ThemeIcon("tools", new vscode.ThemeColor("charts.blue"));
    } else if (node.label.includes("📄")) {
      this.iconPath = new vscode.ThemeIcon("file", new vscode.ThemeColor("charts.blue"));
    } else {
      this.iconPath = new vscode.ThemeIcon("git-merge");
    }
  }

  private setCommand(): void {
    const { node } = this;

    if (node.contextValue === "conflict") {
      this.command = {
        command: "darklang.conflicts.resolve",
        title: "Resolve Conflict",
        arguments: [node]
      };
    } else if (node.contextValue === "conflict-details") {
      this.command = {
        command: "darklang.conflicts.view",
        title: "View Conflict Details",
        arguments: [node]
      };
    } else if (node.contextValue === "conflict-patch") {
      this.command = {
        command: "darklang.patch.view",
        title: "View Patch",
        arguments: [node]
      };
    }
  }
}

export class ConflictsTreeDataProvider implements vscode.TreeDataProvider<ConflictNode> {
  private _onDidChangeTreeData: vscode.EventEmitter<ConflictNode | undefined | null | void> =
    new vscode.EventEmitter<ConflictNode | undefined | null | void>();
  readonly onDidChangeTreeData: vscode.Event<ConflictNode | undefined | null | void> =
    this._onDidChangeTreeData.event;

  private data: ConflictNode[] = [];

  constructor() {
    this.refresh();
  }

  refresh(): void {
    this.data = DemoDataProvider.getConflictsData();
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: ConflictNode): vscode.TreeItem {
    const collapsibleState = element.children && element.children.length > 0
      ? vscode.TreeItemCollapsibleState.Expanded
      : vscode.TreeItemCollapsibleState.None;

    return new ConflictTreeItem(element, collapsibleState);
  }

  getChildren(element?: ConflictNode): Thenable<ConflictNode[]> {
    if (!element) {
      // Return root nodes
      return Promise.resolve(this.data);
    }

    // Return children of the element
    return Promise.resolve(element.children || []);
  }

  resolveConflict(conflictId: string): void {
    // Find and update the conflict to resolved state
    const updateNode = (nodes: ConflictNode[]): boolean => {
      for (const node of nodes) {
        if (node.id === conflictId) {
          node.label = node.label.replace("🚫", "✅").replace("conflicts", "resolved");
          node.contextValue = "resolved-conflict";
          return true;
        }
        if (node.children && updateNode(node.children)) {
          return true;
        }
      }
      return false;
    };

    updateNode(this.data);
    this.updateConflictCounts();
    this._onDidChangeTreeData.fire(undefined);
  }

  private updateConflictCounts(): void {
    // Update the session summary with new conflict counts
    const summary = this.data.find(node => node.type === "session-summary");
    if (summary && summary.children) {
      const statsNode = summary.children.find(child => child.id === "summary-stats");
      if (statsNode) {
        // Count remaining conflicts
        const conflictCount = this.countConflicts(this.data);
        statsNode.label = `📊 3 patches, 12 items changed, ${conflictCount} conflicts`;
      }
    }
  }

  private countConflicts(nodes: ConflictNode[]): number {
    let count = 0;
    for (const node of nodes) {
      if (node.contextValue === "conflict") {
        count++;
      }
      if (node.children) {
        count += this.countConflicts(node.children);
      }
    }
    return count;
  }

  simulateConflicts(): void {
    const scenarios = [
      () => {
        // Scenario 1: New conflict appears
        const conflictPatch = this.data.find(node => node.id === "patch-conflicts");
        if (conflictPatch && conflictPatch.children) {
          conflictPatch.children.push({
            id: "conflict-3",
            label: "🚫 MyApp.Auth.login - conflicts with security-improvements",
            type: "conflict-item",
            contextValue: "conflict",
            children: [
              {
                id: "conflict-3-desc",
                label: "💡 Charlie added 2FA, Dave changed validation",
                type: "resolution",
                contextValue: "conflict-description"
              }
            ]
          });
          this.updateConflictCounts();
        }
        this._onDidChangeTreeData.fire(undefined);
      },
      () => {
        // Scenario 2: Resolve a conflict
        this.resolveConflict("conflict-1");
      },
      () => {
        // Scenario 3: Add new patch with conflicts
        this.data.push({
          id: "patch-new-conflicts",
          label: "⚠️ ui-improvements (conflicts)",
          type: "patch",
          status: "conflicts",
          contextValue: "conflict-patch",
          children: [
            {
              id: "conflict-ui",
              label: "🚫 MyApp.UI.Button - conflicts with design-system",
              type: "conflict-item",
              contextValue: "simple-conflict"
            }
          ]
        });
        this.updateConflictCounts();
        this._onDidChangeTreeData.fire(undefined);
      }
    ];

    let currentScenario = 0;
    setInterval(() => {
      scenarios[currentScenario]();
      console.log(`Conflicts demo: Scenario ${currentScenario + 1}`);
      currentScenario = (currentScenario + 1) % scenarios.length;
    }, 25000); // Change every 25 seconds
  }
}