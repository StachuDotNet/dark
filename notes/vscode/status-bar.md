[Darklang: Connected] [Patch: draft-89ab4e] [Session: helpful-owl] [↑1 ↓0]




## 6. Status Bar Integration

### Real-Time Status
```typescript
class DarklangStatusBar {
  private async updateStatus(): Promise<void> {
    const status = await this.lspClient.sendRequest('status/getAll');

    this.items.get('instance')!.text = `$(server) ${status.instance.name}`;
    this.items.get('session')!.text = `$(target) ${status.session.name}`;

    if (status.patch) {
      this.items.get('patch')!.text = `$(git-branch) ${status.patch.intent}`;
    }

    const syncIcon = status.sync.hasConflicts ? '$(warning)' : '$(sync)';
    this.items.get('sync')!.text = `${syncIcon} ${status.sync.summary}`;
  }
}
```





## Status Bar Integration

**Layout:**
```
[Darklang] 📦 Local Instance | 🎯 Session: main | 📝 Patch: user-validation | 🔄 Sync: Up to date | ⚠️ 2 conflicts
```

**Components:**
1. **Instance Indicator**: Shows current Darklang instance (Local/Remote)
2. **Session Status**: Current session name, clickable to switch
3. **Patch Status**: Current patch (if any), clickable to manage
4. **Sync Status**: Last sync time, conflicts, click to sync
5. **Validation Status**: Current workspace validation state

**Click Actions:**
- Instance → Switch Instance dialog
- Session → Session management menu
- Patch → Patch operations menu
- Sync → Manual sync options
- Validation → Show validation problems




### Status Bar Integration
```
[🔄 Sync: 2↑ 1↓] [📝 Draft: "Fix edge cases"] [👤 stachu] [⚠️ 1 conflict]
```