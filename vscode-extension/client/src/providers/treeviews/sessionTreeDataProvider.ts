import * as vscode from "vscode";
import { SessionNode } from "../../types";
import { ScenarioManager, DevelopmentScenario } from "../../data/scenarioManager";

/**
 * Session Tree Data Provider - Focused on patches within the current session
 */
export class SessionTreeDataProvider implements vscode.TreeDataProvider<SessionNode> {
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
    this.data = this.getSessionPatches();
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: SessionNode): vscode.TreeItem {
    const item = new vscode.TreeItem(
      element.label,
      element.children && element.children.length > 0
        ? vscode.TreeItemCollapsibleState.Expanded
        : vscode.TreeItemCollapsibleState.None
    );

    // Set icons based on type
    if (element.contextValue === 'patch') {
      item.iconPath = new vscode.ThemeIcon("git-branch", new vscode.ThemeColor("charts.blue"));
      item.command = {
        command: "darklang.patch.view",
        title: "View Patch",
        arguments: [{ id: element.id }]
      };
    } else if (element.contextValue === 'operation') {
      const opType = element.label.split(':')[0];
      if (opType.includes('Add')) {
        item.iconPath = new vscode.ThemeIcon("add", new vscode.ThemeColor("charts.green"));
      } else if (opType.includes('Update')) {
        item.iconPath = new vscode.ThemeIcon("edit", new vscode.ThemeColor("charts.yellow"));
      } else if (opType.includes('Create')) {
        item.iconPath = new vscode.ThemeIcon("plus", new vscode.ThemeColor("charts.blue"));
      } else {
        item.iconPath = new vscode.ThemeIcon("circle-outline");
      }
    } else if (element.contextValue === 'conflict') {
      item.iconPath = new vscode.ThemeIcon("warning", new vscode.ThemeColor("charts.red"));
      item.command = {
        command: "darklang.conflict.resolve",
        title: "Resolve Conflict",
        arguments: [element]
      };
    } else if (element.contextValue === 'test') {
      item.iconPath = new vscode.ThemeIcon("beaker", new vscode.ThemeColor("charts.green"));
    } else if (element.contextValue === 'create-patch') {
      item.iconPath = new vscode.ThemeIcon("add", new vscode.ThemeColor("charts.green"));
      item.command = {
        command: "darklang.patch.create",
        title: "Create New Patch",
        arguments: []
      };
    }

    item.contextValue = element.contextValue;
    return item;
  }

  getChildren(element?: SessionNode): Thenable<SessionNode[]> {
    if (!element) {
      return Promise.resolve(this.data);
    }
    return Promise.resolve(element.children || []);
  }

  private getSessionPatches(): SessionNode[] {
    const currentScenario = this.scenarioManager.currentScenario;

    switch (currentScenario) {
      case DevelopmentScenario.CleanStart:
        return [
          {
            id: "create-patch",
            label: "Create New Patch",
            type: "actions",
            contextValue: "create-patch"
          },
          {
            id: "no-patches",
            label: "No patches in this session yet",
            type: "patch",
            contextValue: "empty-state"
          }
        ];

      case DevelopmentScenario.ActiveDevelopment:
        return [
          {
            id: "create-patch",
            label: "Create New Patch",
            type: "actions",
            contextValue: "create-patch"
          },
          {
            id: "patch-filtermap",
            label: "Add List.filterMap (Draft)",
            type: "patch",
            contextValue: "patch",
            patchData: {
              status: "draft",
              operations: 2,
              conflicts: 0,
              tests: 4
            },
            children: [
              {
                id: "op-1",
                label: "AddFunctionContent: Darklang.Stdlib.List.filterMap",
                type: "operation",
                contextValue: "operation"
              },
              {
                id: "op-2",
                label: "CreateName: Darklang.Stdlib.List.filterMap",
                type: "operation",
                contextValue: "operation"
              }
            ]
          }
        ];

      case DevelopmentScenario.ReadyForReview:
        return [
          {
            id: "create-patch",
            label: "Create New Patch",
            type: "actions",
            contextValue: "create-patch"
          },
          {
            id: "patch-db",
            label: "Database Layer: Connection pooling (Ready)",
            type: "patch",
            contextValue: "patch",
            patchData: {
              status: "ready",
              operations: 3,
              conflicts: 0,
              tests: 6
            },
            children: [
              {
                id: "op-db-1",
                label: "AddFunctionContent: MyApp.Database.ConnectionPool",
                type: "operation",
                contextValue: "operation"
              },
              {
                id: "op-db-2",
                label: "UpdateNamePointer: MyApp.Database.connect",
                type: "operation",
                contextValue: "operation"
              },
              {
                id: "op-db-3",
                label: "UpdateNamePointer: MyApp.Database.query",
                type: "operation",
                contextValue: "operation"
              }
            ]
          },
          {
            id: "patch-api",
            label: "API Layer: Use new DB connection (Draft)",
            type: "patch",
            contextValue: "patch",
            patchData: {
              status: "draft",
              operations: 4,
              conflicts: 1,
              tests: 8
            },
            children: [
              {
                id: "conflict-1",
                label: "⚠️ MyApp.API.User.create: Parameter mismatch",
                type: "conflict",
                contextValue: "conflict"
              },
              {
                id: "op-api-1",
                label: "UpdateNamePointer: MyApp.API.User.getAll",
                type: "operation",
                contextValue: "operation"
              },
              {
                id: "op-api-2",
                label: "UpdateNamePointer: MyApp.API.User.create",
                type: "operation",
                contextValue: "operation"
              },
              {
                id: "op-api-3",
                label: "UpdateNamePointer: MyApp.API.Auth.login",
                type: "operation",
                contextValue: "operation"
              },
              {
                id: "op-api-4",
                label: "UpdateNamePointer: MyApp.API.middleware",
                type: "operation",
                contextValue: "operation"
              }
            ]
          }
        ];

      case DevelopmentScenario.ConflictResolution:
        return [
          {
            id: "create-patch",
            label: "Create New Patch",
            type: "actions",
            contextValue: "create-patch"
          },
          {
            id: "patch-validation",
            label: "Fix validation merge conflicts (Conflicts)",
            type: "patch",
            contextValue: "patch",
            patchData: {
              status: "conflicts",
              operations: 5,
              conflicts: 3,
              tests: 7
            },
            children: [
              {
                id: "conflict-1",
                label: "⚠️ MyApp.User.validate: Function signature changed",
                type: "conflict",
                contextValue: "conflict"
              },
              {
                id: "conflict-2",
                label: "⚠️ MyApp.User.validateEmail: Regex pattern conflict",
                type: "conflict",
                contextValue: "conflict"
              },
              {
                id: "conflict-3",
                label: "⚠️ MyApp.User module: Import dependency conflict",
                type: "conflict",
                contextValue: "conflict"
              },
              {
                id: "op-1",
                label: "UpdateNamePointer: MyApp.User.validate",
                type: "operation",
                contextValue: "operation"
              },
              {
                id: "op-2",
                label: "UpdateNamePointer: MyApp.User.validateEmail",
                type: "operation",
                contextValue: "operation"
              },
              {
                id: "op-3",
                label: "AddFunctionContent: MyApp.User.validatePhone",
                type: "operation",
                contextValue: "operation"
              }
            ]
          }
        ];

      case DevelopmentScenario.TeamCollaboration:
        return [
          {
            id: "create-patch",
            label: "Create New Patch",
            type: "actions",
            contextValue: "create-patch"
          },
          {
            id: "patch-frontend",
            label: "Frontend auth integration (Ready)",
            type: "patch",
            contextValue: "patch",
            patchData: {
              status: "ready",
              operations: 6,
              conflicts: 0,
              tests: 12
            },
            children: [
              {
                id: "op-f-1",
                label: "UpdateNamePointer: MyApp.Frontend.Login",
                type: "operation",
                contextValue: "operation"
              },
              {
                id: "op-f-2",
                label: "UpdateNamePointer: MyApp.Frontend.Register",
                type: "operation",
                contextValue: "operation"
              },
              {
                id: "op-f-3",
                label: "AddFunctionContent: MyApp.Frontend.Auth.TokenManager",
                type: "operation",
                contextValue: "operation"
              }
            ]
          }
        ];

      default:
        return this.getSessionPatches();
    }
  }
}