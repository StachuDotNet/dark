
### Custom Patch TreeView
```typescript
class DarklangPatchTreeProvider implements vscode.TreeDataProvider<PatchTreeItem> {
  async getChildren(): Promise<PatchTreeItem[]> {
    const patchStatus = await this.lspClient.sendRequest('patch/getStatus');
    return [
      new PatchTreeItem('current', patchStatus.current),
      new PatchTreeItem('drafts', patchStatus.drafts),
      new PatchTreeItem('incoming', patchStatus.incoming)
    ];
  }
}
```

### Custom Patch UI
```typescript
// Custom webview for patch operations - NOT diff view
private async showPatchDetailsPanel(patchId: string): Promise<void> {
  const panel = vscode.window.createWebviewPanel(
    'darklangPatch',
    'Patch Details',
    vscode.ViewColumn.Beside
  );

  const patchData = await this.lspClient.sendRequest('patch/getDetails', { patchId });
  panel.webview.html = this.generatePatchHTML(patchData);
}
```








### Patches TreeView (In Darklang ViewContainer)

**Key Principle**: Custom patch management UI, NOT VS Code's SCM integration

**Structure:**
```
📝 Patches (Custom TreeView)
├── 🎯 Current: Add user validation
│   ├── 📄 Operations (3)           # Show patch operations
│   ├── ⚠️ Conflicts (1)           # Show any conflicts
│   └── 🧪 Tests (5)               # Show test coverage
├── 📄 Drafts
│   ├── 📝 Fix email validation
│   └── 📝 Add password strength
├── 📨 Incoming
│   ├── 👤 alice: User management improvements
│   └── 👤 bob: Performance optimizations
├── ✅ Applied (Recent)
│   ├── ✅ Database connection pooling
│   └── ✅ Error handling improvements
└── 🔄 Sync Status
    ├── ⬆️ Ready to push: 2 patches
    └── ⬇️ Available to pull: 1 patch
```

**Critical Design Decisions:**
- **NOT SCM Integration**: Patches are not file changes, so don't use VS Code's source control
- **Custom Operations UI**: Show patch operations, not file diffs
- **Package-Aware**: Understand semantic changes to packages, not text changes
- **Custom Webview for Details**: Click patch → opens custom webview, not diff view

**Context Actions:**
- Right-click patch: Review in Custom Panel (`dark://patch/abc123`), Apply, Edit Intent, Export, Delete
- Right-click incoming: Preview in Custom Panel (`dark://patch/abc123`), Apply, Merge with Local, Reject
- Right-click operation: View Operation Details, Revert Operation, Compare Versions (`dark://compare/hash1/hash2`)
