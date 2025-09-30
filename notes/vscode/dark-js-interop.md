
### WebView Communication
```typescript
// Extension to webview messaging
panel.webview.postMessage({
  type: 'showConflict',
  conflict: conflictData
});

// Webview to extension responses
panel.webview.onDidReceiveMessage(message => {
  switch (message.type) {
    case 'resolveConflict':
      await resolveConflict(message.conflictId, message.strategy);
      break;
  }
});
```

### Workspace State Persistence
```typescript
// Session context preservation
const sessionState = {
  activeSession: context.workspaceState.get('activeSession'),
  openFiles: vscode.window.visibleTextEditors.map(e => e.document.uri),
  cursorPositions: getCursorPositions(),
  foldingState: getFoldingState()
};

context.workspaceState.update('sessionState', sessionState);
```