import { PatchNode } from "../../types";

export class PatchDemoData {
  static getPatchesData(): PatchNode[] {
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
            id: "conflicts",
            label: "⚠️ Conflicts (1)",
            type: "current",
            contextValue: "patch-conflicts"
          },
          {
            id: "tests",
            label: "🧪 Tests (12 passing)",
            type: "current",
            contextValue: "patch-tests"
          }
        ]
      },
      {
        id: "drafts",
        label: "📝 Drafts (2)",
        type: "draft",
        contextValue: "drafts-category",
        children: [
          {
            id: "draft-1",
            label: "📄 Fix string interpolation edge cases",
            type: "draft",
            intent: "Fix string interpolation edge cases",
            contextValue: "draft-patch"
          },
          {
            id: "draft-2",
            label: "📄 Add database migration helpers",
            type: "draft",
            intent: "Add database migration helpers",
            contextValue: "draft-patch"
          }
        ]
      },
      {
        id: "incoming",
        label: "📨 Incoming (3)",
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
      },
      {
        id: "applied",
        label: "✅ Applied (5)",
        type: "applied",
        contextValue: "applied-category",
        children: [
          {
            id: "applied-1",
            label: "✅ HTTP client improvements",
            type: "applied",
            intent: "HTTP client improvements",
            contextValue: "applied-patch"
          },
          {
            id: "applied-2",
            label: "✅ List processing enhancements",
            type: "applied",
            intent: "List processing enhancements",
            contextValue: "applied-patch"
          }
        ]
      },
      {
        id: "sync-status",
        label: "🔄 Sync Status",
        type: "sync-status",
        contextValue: "sync-category",
        children: [
          {
            id: "outgoing",
            label: "⬆️ Outgoing (2)",
            type: "sync-status",
            contextValue: "sync-outgoing"
          },
          {
            id: "incoming-sync",
            label: "⬇️ Incoming (1)",
            type: "sync-status",
            contextValue: "sync-incoming"
          }
        ]
      }
    ];
  }
}