import { InstanceNode } from "../../types";

export class InstanceDemoData {
  static getInstancesData(): InstanceNode[] {
    return [
      // Local instance (current/selected)
      {
        id: "local-instance",
        label: "★ Local (/home/stachu/code/dark)",
        type: "current",
        contextValue: "current-instance",
        instanceData: {
          path: "/home/stachu/code/dark",
          status: "connected",
          packageCount: 47,
          sessionCount: 1,
          patchCount: 2
        },
        children: [
          {
            id: "local-packages",
            label: "Packages (47)",
            type: "packages",
            contextValue: "current-packages"
          },
          {
            id: "local-sessions",
            label: "Sessions (1)",
            type: "sessions",
            contextValue: "current-sessions"
          },
          {
            id: "local-actions",
            label: "Actions",
            type: "category",
            contextValue: "instance-actions",
            children: [
              {
                id: "browse-local",
                label: "Browse Packages",
                type: "category",
                contextValue: "browse-packages"
              }
            ]
          }
        ]
      },
      // matter.darklang.com
      {
        id: "matter-instance",
        label: "matter.darklang.com",
        type: "remote",
        contextValue: "remote-instance",
        instanceData: {
          url: "https://matter.darklang.com",
          status: "connected",
          packageCount: 156,
          sessionCount: 8,
          patchCount: 23
        },
        children: [
          {
            id: "matter-packages",
            label: "Packages (156)",
            type: "packages",
            contextValue: "remote-packages",
            children: [
              {
                id: "matter-stdlib",
                label: "Darklang.Stdlib (87 packages)",
                type: "packages",
                contextValue: "package-namespace"
              },
              {
                id: "matter-community",
                label: "Community (45 packages)",
                type: "packages",
                contextValue: "package-namespace"
              },
              {
                id: "matter-examples",
                label: "Examples (24 packages)",
                type: "packages",
                contextValue: "package-namespace"
              }
            ]
          },
          {
            id: "matter-sessions",
            label: "Active Sessions (8)",
            type: "sessions",
            contextValue: "remote-sessions",
            children: [
              {
                id: "session-alice",
                label: "alice: stdlib-improvements",
                type: "sessions",
                contextValue: "remote-session"
              },
              {
                id: "session-bob",
                label: "bob: validation-fixes",
                type: "sessions",
                contextValue: "remote-session"
              },
              {
                id: "session-charlie",
                label: "charlie: performance-opt",
                type: "sessions",
                contextValue: "remote-session"
              }
            ]
          },
          {
            id: "matter-actions",
            label: "Actions",
            type: "category",
            contextValue: "instance-actions",
            children: [
              {
                id: "sync-with-matter",
                label: "Sync with Remote",
                type: "category",
                contextValue: "sync-instance"
              },
              {
                id: "browse-matter",
                label: "Browse Packages",
                type: "category",
                contextValue: "browse-packages"
              }
            ]
          }
        ]
      },
      // Private server
      {
        id: "private-server",
        label: "team.company.com",
        type: "remote",
        contextValue: "remote-instance",
        instanceData: {
          url: "https://team.company.com",
          status: "disconnected",
          packageCount: 78,
          sessionCount: 3,
          patchCount: 12
        },
        children: [
          {
            id: "private-packages",
            label: "Packages (78)",
            type: "packages",
            contextValue: "remote-packages"
          },
          {
            id: "private-sessions",
            label: "Active Sessions (3)",
            type: "sessions",
            contextValue: "remote-sessions"
          },
          {
            id: "private-actions",
            label: "Actions",
            type: "category",
            contextValue: "instance-actions",
            children: [
              {
                id: "connect-private",
                label: "Connect to Server",
                type: "category",
                contextValue: "connect-instance"
              },
              {
                id: "sync-private",
                label: "Sync with Private",
                type: "category",
                contextValue: "sync-instance"
              }
            ]
          }
        ]
      }
    ];
  }
}