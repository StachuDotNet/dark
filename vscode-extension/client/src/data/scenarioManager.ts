import * as vscode from "vscode";
import { PackageNode, PatchNode, SessionNode, ConflictNode, StatusBarData } from "../types";

export enum DevelopmentScenario {
  CleanStart = "clean-start",
  ActiveDevelopment = "active-development",
  ReadyForReview = "ready-for-review",
  ConflictResolution = "conflict-resolution",
  TeamCollaboration = "team-collaboration"
}

export interface ScenarioDefinition {
  id: DevelopmentScenario;
  name: string;
  description: string;
  statusBar: StatusBarData;
  packages: PackageNode[];
  patches: PatchNode[];
  sessions: SessionNode[];
  conflicts: ConflictNode[];
}

export class ScenarioManager {
  private static _instance: ScenarioManager;
  private _currentScenario: DevelopmentScenario = DevelopmentScenario.CleanStart;
  private _onScenarioChanged = new vscode.EventEmitter<DevelopmentScenario>();
  readonly onScenarioChanged = this._onScenarioChanged.event;

  static getInstance(): ScenarioManager {
    if (!ScenarioManager._instance) {
      ScenarioManager._instance = new ScenarioManager();
    }
    return ScenarioManager._instance;
  }

  get currentScenario(): DevelopmentScenario {
    return this._currentScenario;
  }

  setScenario(scenario: DevelopmentScenario): void {
    if (this._currentScenario !== scenario) {
      this._currentScenario = scenario;
      console.log(`🎭 Scenario changed to: ${scenario}`);
      this._onScenarioChanged.fire(scenario);
    }
  }

  getScenarioData(): ScenarioDefinition {
    return this.scenarios[this._currentScenario];
  }

  getAllScenarios(): ScenarioDefinition[] {
    return Object.values(this.scenarios);
  }

  private scenarios: Record<DevelopmentScenario, ScenarioDefinition> = {
    [DevelopmentScenario.CleanStart]: {
      id: DevelopmentScenario.CleanStart,
      name: "Clean Start",
      description: "Fresh workspace, no active work",
      statusBar: {
        session: { name: "No active session", active: false },
        patch: { current: "None", changes: 0 },
        conflicts: { count: 0, hasUnresolved: false },
        sync: { incoming: 0, outgoing: 0 }
      },
      packages: this.getCleanPackages(),
      patches: this.getCleanPatches(),
      sessions: this.getCleanSessions(),
      conflicts: this.getCleanConflicts()
    },

    [DevelopmentScenario.ActiveDevelopment]: {
      id: DevelopmentScenario.ActiveDevelopment,
      name: "Active Development",
      description: "Working on user validation feature",
      statusBar: {
        session: { name: "helpful-owl-42: Add user validation", active: true },
        patch: { current: "user-validation", changes: 3 },
        conflicts: { count: 0, hasUnresolved: false },
        sync: { incoming: 1, outgoing: 0 }
      },
      packages: this.getActiveDevPackages(),
      patches: this.getActiveDevPatches(),
      sessions: this.getActiveDevSessions(),
      conflicts: this.getActiveDevConflicts()
    },

    [DevelopmentScenario.ReadyForReview]: {
      id: DevelopmentScenario.ReadyForReview,
      name: "Ready for Review",
      description: "Patch complete, tests passing, ready for team review",
      statusBar: {
        session: { name: "helpful-owl-42: Add user validation", active: true },
        patch: { current: "user-validation", changes: 5 },
        conflicts: { count: 0, hasUnresolved: false },
        sync: { incoming: 2, outgoing: 1 }
      },
      packages: this.getReadyForReviewPackages(),
      patches: this.getReadyForReviewPatches(),
      sessions: this.getReadyForReviewSessions(),
      conflicts: this.getReadyForReviewConflicts()
    },

    [DevelopmentScenario.ConflictResolution]: {
      id: DevelopmentScenario.ConflictResolution,
      name: "Conflict Resolution",
      description: "Multiple patches affecting same code, needs manual resolution",
      statusBar: {
        session: { name: "helpful-owl-42: Add user validation", active: true },
        patch: { current: "user-validation", changes: 5 },
        conflicts: { count: 3, hasUnresolved: true },
        sync: { incoming: 3, outgoing: 1 }
      },
      packages: this.getConflictPackages(),
      patches: this.getConflictPatches(),
      sessions: this.getConflictSessions(),
      conflicts: this.getConflictConflicts()
    },

    [DevelopmentScenario.TeamCollaboration]: {
      id: DevelopmentScenario.TeamCollaboration,
      name: "Team Collaboration",
      description: "Multiple team members working, patches being shared",
      statusBar: {
        session: { name: "helpful-owl-42: Add user validation", active: true },
        patch: { current: "user-validation", changes: 3 },
        conflicts: { count: 1, hasUnresolved: false },
        sync: { incoming: 5, outgoing: 2 }
      },
      packages: this.getTeamCollabPackages(),
      patches: this.getTeamCollabPatches(),
      sessions: this.getTeamCollabSessions(),
      conflicts: this.getTeamCollabConflicts()
    }
  };

  // Scenario-specific data methods
  private getCleanPackages(): PackageNode[] {
    return [
      {
        id: "Darklang.Stdlib",
        label: "🏢 Darklang.Stdlib",
        type: "namespace",
        collapsibleState: 1,
        contextValue: "namespace",
        children: [
          {
            id: "Darklang.Stdlib.List",
            label: "📁 List",
            type: "module",
            collapsibleState: 1,
            contextValue: "module",
            children: [
              {
                id: "Darklang.Stdlib.List.map",
                label: "🔧 map",
                type: "function",
                collapsibleState: 0,
                contextValue: "fn:Darklang.Stdlib.List.map",
                packagePath: "Darklang.Stdlib.List.map"
              }
            ]
          }
        ]
      },
      {
        id: "MyApp",
        label: "🌐 MyApp",
        type: "namespace",
        collapsibleState: 1,
        contextValue: "namespace",
        children: []
      }
    ];
  }

  private getActiveDevPackages(): PackageNode[] {
    return [
      {
        id: "MyApp",
        label: "🌐 MyApp",
        type: "namespace",
        collapsibleState: 1,
        contextValue: "namespace",
        children: [
          {
            id: "MyApp.User",
            label: "📁 User",
            type: "module",
            collapsibleState: 1,
            contextValue: "module",
            children: [
              {
                id: "MyApp.User.User",
                label: "📋 User",
                type: "type",
                collapsibleState: 0,
                contextValue: "type:MyApp.User.User",
                packagePath: "MyApp.User.User"
              },
              {
                id: "MyApp.User.create",
                label: "🔧 create [MODIFIED]",
                type: "function",
                collapsibleState: 0,
                contextValue: "fn:MyApp.User.create",
                packagePath: "MyApp.User.create"
              },
              {
                id: "MyApp.User.validate",
                label: "🔧 validate [NEW]",
                type: "function",
                collapsibleState: 0,
                contextValue: "fn:MyApp.User.validate",
                packagePath: "MyApp.User.validate"
              }
            ]
          }
        ]
      }
    ];
  }

  // Add more scenario-specific methods...
  private getReadyForReviewPackages(): PackageNode[] {
    // Similar to active dev but with different states
    return this.getActiveDevPackages();
  }

  private getConflictPackages(): PackageNode[] {
    const packages = this.getActiveDevPackages();
    // Add conflict indicators
    if (packages[0]?.children?.[0]?.children) {
      packages[0].children[0].children.push({
        id: "MyApp.User.update",
        label: "🔧 update [CONFLICT]",
        type: "function",
        collapsibleState: 0,
        contextValue: "fn:MyApp.User.update",
        packagePath: "MyApp.User.update"
      });
    }
    return packages;
  }

  private getTeamCollabPackages(): PackageNode[] {
    return this.getActiveDevPackages();
  }

  // Placeholder methods for other data types
  private getCleanPatches(): PatchNode[] {
    return [
      {
        id: "no-patches",
        label: "No active patches",
        type: "current",
        contextValue: "empty-state"
      }
    ];
  }

  private getActiveDevPatches(): PatchNode[] {
    return [
      {
        id: "current-patch",
        label: "🎯 Current: Add user validation",
        type: "current",
        intent: "Add user validation",
        contextValue: "current-patch",
        children: [
          {
            id: "operations",
            label: "📄 Operations (3)",
            type: "current",
            contextValue: "patch-operations"
          },
          {
            id: "tests",
            label: "🧪 Tests (5 passing)",
            type: "current",
            contextValue: "patch-tests"
          }
        ]
      }
    ];
  }

  private getReadyForReviewPatches(): PatchNode[] {
    return [
      {
        id: "current-patch",
        label: "🎯 Ready: Add user validation",
        type: "current",
        intent: "Add user validation",
        contextValue: "current-patch",
        children: [
          {
            id: "operations",
            label: "📄 Operations (5)",
            type: "current",
            contextValue: "patch-operations"
          },
          {
            id: "tests",
            label: "✅ Tests (12 passing)",
            type: "current",
            contextValue: "patch-tests"
          }
        ]
      }
    ];
  }

  private getConflictPatches(): PatchNode[] {
    return [
      {
        id: "current-patch",
        label: "⚠️ Conflicts: Add user validation",
        type: "current",
        intent: "Add user validation",
        contextValue: "current-patch",
        children: [
          {
            id: "conflicts",
            label: "🚫 Conflicts (3)",
            type: "current",
            contextValue: "patch-conflicts"
          }
        ]
      }
    ];
  }

  private getTeamCollabPatches(): PatchNode[] {
    return [
      {
        id: "current-patch",
        label: "🎯 Current: Add user validation",
        type: "current",
        intent: "Add user validation",
        contextValue: "current-patch"
      },
      {
        id: "incoming",
        label: "📨 Incoming (5)",
        type: "incoming",
        contextValue: "incoming-category",
        children: [
          {
            id: "incoming-1",
            label: "👤 alice: Security improvements",
            type: "incoming",
            author: "alice",
            intent: "Security improvements",
            contextValue: "incoming-patch"
          },
          {
            id: "incoming-2",
            label: "👤 bob: Performance optimizations",
            type: "incoming",
            author: "bob",
            intent: "Performance optimizations",
            contextValue: "incoming-patch"
          }
        ]
      }
    ];
  }

  // Placeholder implementations for sessions and conflicts
  private getCleanSessions(): SessionNode[] { return []; }
  private getActiveDevSessions(): SessionNode[] { return []; }
  private getReadyForReviewSessions(): SessionNode[] { return []; }
  private getConflictSessions(): SessionNode[] { return []; }
  private getTeamCollabSessions(): SessionNode[] { return []; }

  private getCleanConflicts(): ConflictNode[] { return []; }
  private getActiveDevConflicts(): ConflictNode[] { return []; }
  private getReadyForReviewConflicts(): ConflictNode[] { return []; }
  private getConflictConflicts(): ConflictNode[] { return []; }
  private getTeamCollabConflicts(): ConflictNode[] { return []; }
}