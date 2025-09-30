import { ConflictNode } from "../../types";

export class ConflictDemoData {
  static getConflictsData(): ConflictNode[] {
    return [
      {
        id: "session-summary",
        label: "🔄 Session Conflict Summary",
        type: "session-summary",
        contextValue: "session-summary",
        children: [
          {
            id: "summary-stats",
            label: "📊 3 patches, 12 items changed, 2 conflicts",
            type: "session-summary",
            contextValue: "summary-stats"
          },
          {
            id: "summary-actions",
            label: "💡 Auto-resolve simple conflicts",
            type: "session-summary",
            contextValue: "auto-resolve"
          }
        ]
      },
      {
        id: "patch-conflicts",
        label: "⚠️ user-validation (conflicts)",
        type: "patch",
        status: "conflicts",
        contextValue: "conflict-patch",
        children: [
          {
            id: "conflict-1",
            label: "🚫 MyApp.User.update - conflicts with security-improvements",
            type: "conflict-item",
            contextValue: "conflict",
            children: [
              {
                id: "conflict-1-desc",
                label: "💡 Alice added validation, Bob changed signature",
                type: "resolution",
                contextValue: "conflict-description"
              }
            ]
          },
          {
            id: "conflict-2",
            label: "🚫 MyApp.Auth.hashPassword - conflicts with performance-optimizations",
            type: "conflict-item",
            contextValue: "conflict",
            children: [
              {
                id: "conflict-2-desc",
                label: "💡 Different hashing algorithms chosen",
                type: "resolution",
                contextValue: "conflict-description"
              }
            ]
          }
        ]
      },
      {
        id: "resolved-patches",
        label: "✅ Recently Resolved",
        type: "patch",
        status: "ready",
        contextValue: "resolved-category",
        children: [
          {
            id: "resolved-1",
            label: "✅ string-improvements - all conflicts resolved",
            type: "conflict-item",
            contextValue: "resolved-conflict"
          }
        ]
      }
    ];
  }
}