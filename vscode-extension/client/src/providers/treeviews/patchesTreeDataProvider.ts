import * as vscode from "vscode";
import { DemoDataProvider, PatchNode } from "../../data/demoData";

export class PatchTreeItem extends vscode.TreeItem {
  constructor(
    public readonly node: PatchNode,
    public readonly collapsibleState: vscode.TreeItemCollapsibleState
  ) {
    super(node.label, collapsibleState);

    this.tooltip = this.getTooltip();
    this.contextValue = node.contextValue;
    this.setIcon();
  }

  private getTooltip(): string {
    const { node } = this;
    switch (node.type) {
      case "current":
        return `Current patch: ${node.intent || node.label}`;
      case "draft":
        return node.intent ? `Draft patch: ${node.intent}` : "Draft patches - click to manage";
      case "incoming":
        return node.author
          ? `Incoming patch from ${node.author}: ${node.intent}`
          : "Incoming patches from team members";
      case "applied":
        return node.intent ? `Applied patch: ${node.intent}` : "Recently applied patches";
      case "sync-status":
        return "Synchronization status with remote repository";
      default:
        return node.label;
    }
  }

  private setIcon(): void {
    const { node } = this;

    if (node.label.includes("🎯")) {
      this.iconPath = new vscode.ThemeIcon("target", new vscode.ThemeColor("charts.orange"));
    } else if (node.label.includes("📄")) {
      this.iconPath = new vscode.ThemeIcon("file", new vscode.ThemeColor("charts.blue"));
    } else if (node.label.includes("📝")) {
      this.iconPath = new vscode.ThemeIcon("edit", new vscode.ThemeColor("charts.yellow"));
    } else if (node.label.includes("📨")) {
      this.iconPath = new vscode.ThemeIcon("mail", new vscode.ThemeColor("charts.green"));
    } else if (node.label.includes("✅")) {
      this.iconPath = new vscode.ThemeIcon("check", new vscode.ThemeColor("charts.green"));
    } else if (node.label.includes("🔄")) {
      this.iconPath = new vscode.ThemeIcon("sync", new vscode.ThemeColor("charts.purple"));
    } else if (node.label.includes("⬆️")) {
      this.iconPath = new vscode.ThemeIcon("arrow-up", new vscode.ThemeColor("charts.orange"));
    } else if (node.label.includes("⬇️")) {
      this.iconPath = new vscode.ThemeIcon("arrow-down", new vscode.ThemeColor("charts.blue"));
    } else if (node.label.includes("⚠️")) {
      this.iconPath = new vscode.ThemeIcon("warning", new vscode.ThemeColor("charts.red"));
    } else if (node.label.includes("🧪")) {
      this.iconPath = new vscode.ThemeIcon("beaker", new vscode.ThemeColor("charts.green"));
    } else if (node.label.includes("👤")) {
      this.iconPath = new vscode.ThemeIcon("person", new vscode.ThemeColor("charts.blue"));
    } else {
      this.iconPath = new vscode.ThemeIcon("git-branch");
    }
  }
}

export class PatchesTreeDataProvider implements vscode.TreeDataProvider<PatchNode> {
  private _onDidChangeTreeData: vscode.EventEmitter<PatchNode | undefined | null | void> =
    new vscode.EventEmitter<PatchNode | undefined | null | void>();
  readonly onDidChangeTreeData: vscode.Event<PatchNode | undefined | null | void> =
    this._onDidChangeTreeData.event;

  private data: PatchNode[] = [];

  constructor() {
    this.refresh();
  }

  refresh(): void {
    this.data = DemoDataProvider.getPatchesData();
    this._onDidChangeTreeData.fire();
  }

  getTreeItem(element: PatchNode): vscode.TreeItem {
    const collapsibleState = element.children && element.children.length > 0
      ? vscode.TreeItemCollapsibleState.Expanded
      : vscode.TreeItemCollapsibleState.None;

    return new PatchTreeItem(element, collapsibleState);
  }

  getChildren(element?: PatchNode): Thenable<PatchNode[]> {
    if (!element) {
      // Return root nodes
      return Promise.resolve(this.data);
    }

    // Return children of the element
    return Promise.resolve(element.children || []);
  }

  simulatePatches(): void {
    // Simulate patch state changes for demo
    const scenarios = [
      () => {
        // Scenario 1: New incoming patch
        const incoming = this.data.find(node => node.id === "incoming");
        if (incoming && incoming.children) {
          incoming.children.push({
            id: "incoming-3",
            label: "👤 charlie: Database optimizations",
            type: "incoming",
            author: "charlie",
            intent: "Database optimizations",
            contextValue: "incoming-patch"
          });
        }
        this._onDidChangeTreeData.fire(undefined);
      },
      () => {
        // Scenario 2: Move draft to ready
        const drafts = this.data.find(node => node.id === "drafts");
        const current = this.data.find(node => node.id === "current-patch");
        if (drafts && current && drafts.children && drafts.children.length > 0) {
          const draftPatch = drafts.children.shift();
          if (draftPatch) {
            current.label = `🎯 Current: ${draftPatch.intent}`;
            current.intent = draftPatch.intent;
          }
        }
        this._onDidChangeTreeData.fire(undefined);
      },
      () => {
        // Scenario 3: Apply incoming patch
        const incoming = this.data.find(node => node.id === "incoming");
        const applied = this.data.find(node => node.id === "applied");
        if (incoming && applied && incoming.children && applied.children && incoming.children.length > 0) {
          const incomingPatch = incoming.children.shift();
          if (incomingPatch) {
            applied.children.unshift({
              id: `applied-${Date.now()}`,
              label: `✅ ${incomingPatch.intent}`,
              type: "applied",
              intent: incomingPatch.intent,
              contextValue: "applied-patch"
            });
          }
        }
        this._onDidChangeTreeData.fire(undefined);
      }
    ];

    let currentScenario = 0;
    setInterval(() => {
      scenarios[currentScenario]();
      console.log(`Patches demo: Scenario ${currentScenario + 1}`);
      currentScenario = (currentScenario + 1) % scenarios.length;
    }, 15000); // Change every 15 seconds
  }
}