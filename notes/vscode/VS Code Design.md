# VS Code Design: Darklang-First Developer Experience

## Custom ViewContainer Architecture

### Virtual File Schema (Minimal - Editor Only)

```
dark://package/Name.Space.item                     # Browse/read (both module-level and item-specific)
dark://edit/current-patch/Name.Space.item          # Edit in current patch
dark://draft/Name.Space.newItem                    # Create new item
dark://history/Name.Space.item                     # Version history (item or module level)
dark://patch/abc123                                # Patch overview
dark://compare/hash1/hash2                         # Version comparison
```


## User Experience Flows

### 1. Opening VS Code and Connecting to Darklang

**User Actions:**
1. Open VS Code
2. Use `Ctrl+Shift+P` → "Darklang: Connect to Instance"
3. Choose local CLI or remote instance
4. VS Code creates virtual workspace


### 2. Browsing and Opening Package Items

**User Actions:**
1. Navigate package tree: `Darklang > Stdlib > List`
2. Double-click `map.dark` to open
3. Function opens in editor with syntax highlighting


**What User Sees:**
- File opens in editor as `dark://package/Darklang.Stdlib.List.map`
- Syntax highlighting and IntelliSense work normally
- Status bar shows package location and content hash
- Tree view highlights current location
- Can click "Edit" to transition to `dark://edit/current-patch/Darklang.Stdlib.List.map`



### 3. Creating and Working with Patches

**User Actions:**
1. `Ctrl+Shift+P` → "Darklang: Create Patch"
2. Enter intent: "Add validation to user input"
3. VS Code creates patch workspace
4. Edit functions, add new ones
5. See live diff and validation

**What User Sees:**
- New workspace opens at `dark://patch/abc123`
- Side panel shows "Patch: Add validation to user input"
- Can browse packages and edit them via `dark://edit/current-patch/Name.Space.item`
- Changes are tracked as Ops in real-time
- Status bar shows `📝 Patch: Add validation...`
- Can view version history via `dark://history/Name.Space.item`



### 4. Custom Patch UI (No SCM Integration)

**User Actions:**
1. Right-click patch in Darklang ViewContainer → "Review Patch"
2. Custom webview panel opens with patch review interface
3. Use custom UI for patch operations (not VS Code's SCM)

**What User Sees:**
- Custom patch interface in webview panel (not SCM view)
- List of operations in the patch
- Custom validation status display
- Patch-specific actions (apply, validate, conflict resolution)
- No confusion with git/file-based version control



### 5. Session Management and Transfer

**User Actions:**
1. `Ctrl+Shift+P` → "Darklang: Switch Session"
2. Choose from recent sessions or create new one
3. Workspace updates to reflect session state


**What User Sees:**
- Workspace completely refreshes to match session
- Open files change to session's open files
- Current patch switches if different
- Status bar updates: `🎯 Session: feature-auth-system`



### 6. Conflict Resolution

**User Actions:**
1. Attempt to apply patch with conflicts
2. VS Code shows conflict resolution interface
3. Choose resolution strategy for each conflict
4. Apply resolved patch

**What User Sees:**
- Conflict resolution panel opens
- Each conflict shows: what changed, where it conflicts, resolution options
- Can choose: Keep Local, Keep Remote, Manual Merge, Rename Both
- Real-time preview of resolution results



## Webview Panel Integrations

### 3. Package Documentation Panel

**Features:**
- Rich markdown documentation
- Interactive examples with run buttons
- Type signature visualization
- Usage statistics and examples
- Related package suggestions



## NO Source Control Integration

**Critical Design Decision: No SCM Integration**

Patches are NOT file changes, so they should NOT use VS Code's Source Control Management:

```typescript
// ❌ WRONG - Don't implement SourceControlProvider for patches
// class DarklangSCMProvider implements SourceControlProvider

// ✅ CORRECT - Use custom TreeView and WebView panels
class DarklangPatchManager {
  // Custom patch UI in Darklang ViewContainer
  private patchTreeProvider: DarklangPatchTreeProvider;

  showPatchDetails(patchId: string) {
    // Open custom webview, not SCM diff view
    const panel = vscode.window.createWebviewPanel(
      'darklangPatch',
      'Patch Details',
      vscode.ViewColumn.Beside
    );
    // Custom HTML showing patch operations, not file diffs
  }
}
```




This VS Code design provides a comprehensive experience for Darklang development by clearly separating file-based and package-based concepts:

**File Explorer**: Frontend files, configs, documentation (traditional file-based development)
**Darklang ViewContainer**: Backend packages, patches, sessions (package-based development)

This separation prevents user confusion and provides appropriate interfaces for each development paradigm while leveraging the power of the content-addressable package manager.