import { SessionNode } from "../../types";
import { ScenarioManager, DevelopmentScenario } from "../scenarioManager";

export class SessionDemoData {
  static getSessionsData(): SessionNode[] {
    const scenarioManager = ScenarioManager.getInstance();
    const currentScenario = scenarioManager.currentScenario;

    return [
      // Current session as first root-level node with actions as sub-nodes
      {
        id: "current-session",
        label: this.getCurrentSessionLabel(currentScenario),
        type: "current",
        contextValue: "current-session",
        children: [
          {
            id: "create-patch",
            label: "Create New Patch",
            type: "actions",
            contextValue: "create-patch"
          }
        ]
      },
      // Patches as root-level node
      {
        id: "patches",
        label: "Patches",
        type: "patch",
        contextValue: "patches-category",
        children: this.getSessionPatches(currentScenario)
      },
      // Switch session as last item
      {
        id: "switch-session",
        label: "Switch Session",
        type: "actions",
        contextValue: "switch-session-category",
        children: this.getRecentSessionsData(currentScenario)
      }
    ];
  }

  private static getCurrentSessionLabel(scenario: DevelopmentScenario): string {
    switch (scenario) {
      case DevelopmentScenario.CleanStart:
        return "Current: clean-start: New project setup";
      case DevelopmentScenario.ActiveDevelopment:
        return "Current: stdlib-dev: Add List.filterMap function";
      case DevelopmentScenario.ReadyForReview:
        return "Current: multi-layer: Database and API refactor";
      case DevelopmentScenario.ConflictResolution:
        return "Current: conflict-fix: Resolve validation conflicts";
      case DevelopmentScenario.TeamCollaboration:
        return "Current: team-collab: Feature integration sprint";
      default:
        return "Current: clean-start: New project setup";
    }
  }

  private static getSessionPatches(scenario: DevelopmentScenario): SessionNode[] {
    const sessionData = this.getCurrentSessionData(scenario);
    return sessionData.children || [];
  }

  private static getCurrentSessionData(scenario: DevelopmentScenario): SessionNode {
    switch (scenario) {
      case DevelopmentScenario.CleanStart:
        return {
          id: "current-session",
          label: "Current: clean-start: New project setup",
          type: "current",
          contextValue: "current-session",
          children: [
            {
              id: "no-patches",
              label: "No patches yet",
              type: "patch",
              contextValue: "empty-state"
            }
          ]
        };

      case DevelopmentScenario.ActiveDevelopment:
        return {
          id: "current-session",
          label: "Current: stdlib-dev: Add List.filterMap function",
          type: "current",
          contextValue: "current-session",
          children: [
            {
              id: "current-patch",
              label: "Current Patch: Add List.filterMap",
              type: "patch",
              contextValue: "current-patch",
              patchData: {
                status: "draft",
                operations: 2,
                conflicts: 0,
                tests: 4,
                intent: "Add filterMap function to Darklang.Stdlib.List"
              },
              children: [
                {
                  id: "operations",
                  label: "Operations (2)",
                  type: "operation",
                  contextValue: "operations-category",
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
                },
                {
                  id: "tests",
                  label: "Tests (4)",
                  type: "operation",
                  contextValue: "tests-category",
                  children: [
                    {
                      id: "test-1",
                      label: "filterMap basic functionality",
                      type: "operation",
                      contextValue: "test"
                    },
                    {
                      id: "test-2",
                      label: "filterMap empty list handling",
                      type: "operation",
                      contextValue: "test"
                    },
                    {
                      id: "test-3",
                      label: "filterMap type safety",
                      type: "operation",
                      contextValue: "test"
                    },
                    {
                      id: "test-4",
                      label: "filterMap performance",
                      type: "operation",
                      contextValue: "test"
                    }
                  ]
                }
              ]
            }
          ]
        };

      case DevelopmentScenario.ReadyForReview:
        return {
          id: "current-session",
          label: "Current: multi-layer: Database and API refactor",
          type: "current",
          contextValue: "current-session",
          children: [
            {
              id: "patch-1",
              label: "Database Layer: Connection pooling",
              type: "patch",
              contextValue: "current-patch",
              patchData: {
                status: "ready",
                operations: 3,
                conflicts: 0,
                tests: 6,
                intent: "Implement connection pooling for better performance"
              },
              children: [
                {
                  id: "ops-db",
                  label: "Operations (3)",
                  type: "operation",
                  contextValue: "operations-category",
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
                }
              ]
            },
            {
              id: "patch-2",
              label: "API Layer: Use new DB connection",
              type: "patch",
              contextValue: "draft-patch",
              patchData: {
                status: "draft",
                operations: 4,
                conflicts: 1,
                tests: 8,
                intent: "Update API layer to use connection pooling"
              },
              children: [
                {
                  id: "ops-api",
                  label: "Operations (4)",
                  type: "operation",
                  contextValue: "operations-category",
                  children: [
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
                },
                {
                  id: "conflicts-api",
                  label: "Conflicts (1)",
                  type: "conflict",
                  contextValue: "conflicts-category",
                  children: [
                    {
                      id: "conflict-api-1",
                      label: "MyApp.API.User.create: Parameter mismatch",
                      type: "conflict",
                      contextValue: "conflict"
                    }
                  ]
                }
              ]
            }
          ]
        };

      case DevelopmentScenario.ConflictResolution:
        return {
          id: "current-session",
          label: "Current: conflict-fix: Resolve validation conflicts",
          type: "current",
          contextValue: "current-session",
          children: [
            {
              id: "current-patch",
              label: "Current Patch: Fix validation merge conflicts",
              type: "patch",
              contextValue: "current-patch",
              patchData: {
                status: "conflicts",
                operations: 5,
                conflicts: 3,
                tests: 7,
                intent: "Merge validation improvements with concurrent changes"
              },
              children: [
                {
                  id: "operations",
                  label: "Operations (5)",
                  type: "operation",
                  contextValue: "operations-category",
                  children: [
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
                },
                {
                  id: "conflicts",
                  label: "Conflicts (3)",
                  type: "conflict",
                  contextValue: "conflicts-category",
                  children: [
                    {
                      id: "conflict-1",
                      label: "MyApp.User.validate: Function signature changed",
                      type: "conflict",
                      contextValue: "conflict"
                    },
                    {
                      id: "conflict-2",
                      label: "MyApp.User.validateEmail: Regex pattern conflict",
                      type: "conflict",
                      contextValue: "conflict"
                    },
                    {
                      id: "conflict-3",
                      label: "MyApp.User module: Import dependency conflict",
                      type: "conflict",
                      contextValue: "conflict"
                    }
                  ]
                }
              ]
            }
          ]
        };

      case DevelopmentScenario.TeamCollaboration:
        return {
          id: "current-session",
          label: "Current: team-collab: Feature integration sprint",
          type: "current",
          contextValue: "current-session",
          children: [
            {
              id: "my-patch",
              label: "My Patch: Frontend auth integration",
              type: "patch",
              contextValue: "current-patch",
              patchData: {
                status: "ready",
                operations: 6,
                conflicts: 0,
                tests: 12,
                intent: "Integrate new auth system with frontend"
              },
              children: [
                {
                  id: "ops-frontend",
                  label: "Operations (6)",
                  type: "operation",
                  contextValue: "operations-category",
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
              ]
            }
          ]
        };

      default:
        return this.getCurrentSessionData(DevelopmentScenario.CleanStart);
    }
  }

  private static getRecentSessionsData(currentScenario: DevelopmentScenario): SessionNode[] {
    // Show different recent sessions based on current scenario
    const allSessions = [
      {
        id: "session-clean",
        label: "clean-start: New project setup",
        type: "recent" as const,
        contextValue: "session"
      },
      {
        id: "session-stdlib",
        label: "stdlib-dev: Add List.filterMap function",
        type: "recent" as const,
        contextValue: "session"
      },
      {
        id: "session-multi",
        label: "multi-layer: Database and API refactor",
        type: "recent" as const,
        contextValue: "session"
      },
      {
        id: "session-conflicts",
        label: "conflict-fix: Resolve validation conflicts",
        type: "recent" as const,
        contextValue: "session"
      },
      {
        id: "session-team",
        label: "team-collab: Feature integration sprint",
        type: "recent" as const,
        contextValue: "session"
      }
    ];

    // Filter out the current session and return 3 others
    return allSessions
      .filter(session => !session.label.startsWith(currentScenario.replace('-', '-')))
      .slice(0, 3);
  }
}