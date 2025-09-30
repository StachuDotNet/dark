# Overview of Darklang's VS Code Presence


### Darklang ViewContainer Structure

```
🌙 Darklang ViewContainer
├── 📦 Packages TreeView
│   ├── 🏢 Darklang
│   │   ├── 📁 Stdlib
│   │   │   ├── 📁 List
│   │   │   │   ├── 🔧 map        # Click → opens dark://package/Darklang.Stdlib.List.map
│   │   │   │   └── 🔧 filter     # Click → opens dark://package/Darklang.Stdlib.List.filter
│   │   │   └── 📁 Option
│   │   │       └── 🔧 map
│   │   └── 📁 Http
│   └── 🌐 Community
├── 📝 Patches TreeView
│   ├── 🎯 Current: Add user validation
│   ├── 📄 Drafts (2)
│   ├── 📨 Incoming (1)
│   └── ✅ Applied (5)
└── 🎯 Sessions TreeView
    ├── ⭐ Current: feature-auth
    ├── 📅 Recent (3)
    └── 👥 Shared (1)
```




## Core Design Principles

- **Darklang-First Development**: Core package and source-management lives in LibMatter. Builttins are exposed by F#. Minimal JS implementation to support various UI components, with as much logic as possible implemented as LSP extensions
- **Direct Collaboration**: VS Code works against central DB/instance, not local files
- **Virtual Everything**: Leverage virtual file systems calling Darklang packages
- **Composable Architecture**: All components call Darklang packages for business logic





## 🏗️ Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        VS Code Editor                          │
├─────────────────────────────────────────────────────────────────┤
│  📋 Patch TreeView  │  ⚠️ Conflict Panel  │  📝 Session Manager │
│  🔄 Status Bar      │  🎛️ Command Palette │  📊 SCM Integration │
└─────────────────────────────────────────────────────────────────┘
                                │
                    LSP Protocol + Extensions
                                │
┌─────────────────────────────────────────────────────────────────┐
│              Enhanced Darklang LSP Server                      │
├─────────────────────────────────────────────────────────────────┤
│  🎯 Collaboration Extensions  │  📡 Real-time WebSocket        │
│  🔧 Standard LSP Features     │  🤝 Editor Capability Negot.   │
└─────────────────────────────────────────────────────────────────┘
                                │
                      Direct Integration
                                │
┌─────────────────────────────────────────────────────────────────┐
│                  Darklang CLI Commands                         │
├─────────────────────────────────────────────────────────────────┤
│  📦 Patch Management  │  ⚔️ Conflict Resolution │  🔗 Database  │
│  📝 Session Context   │  🔄 Sync Operations     │  🗄️ SQLite    │
└─────────────────────────────────────────────────────────────────┘
```


## 📊 Complete Feature Matrix

| Feature | VS Code | Neovim | Emacs | Sublime | Basic LSP |
|---------|---------|---------|-------|---------|-----------|
| **Patch Creation** | ✅ Rich UI | ✅ Commands | ✅ Buffers | ✅ Panels | ✅ JSON RPC |
| **Patch Tree View** | ✅ TreeView | ❌ Quickfix | ✅ Buffers | ❌ Lists | ❌ Text |
| **Conflict Resolution** | ✅ Webview | ❌ Manual | ✅ Buffers | ❌ Console | ❌ CLI |
| **Real-time Updates** | ✅ Live | ❌ Poll | ✅ Live | ❌ Poll | ❌ None |
| **Session Management** | ✅ Full UI | ❌ Commands | ❌ Manual | ❌ Manual | ❌ None |
| **Status Integration** | ✅ Status Bar | ✅ Line | ✅ Mode Line | ✅ Status | ❌ None |



### 1. Separate Worlds Approach

**Frontend/Files in File Explorer:**
```
📁 my-project/
├── 📄 index.html
├── 📄 styles.css
└── 📄 package.json
```



**Backend/Darklang in Custom ViewContainer:**
```
🎯 DARKLANG
├── 📦 Packages
│   ├── 📁 MyApp
│   │   ├── ⚡ User.validate
│   │   └── ⚡ User.createAccount
│   └── 📁 Darklang.Stdlib
├── 📋 Patches
│   ├── 🔄 Current: user-validation-improvements
│   └── ✏️ Draft: api-refactor
└── 🎮 Sessions
    └── 🟢 Active: main-dev
```



### 2. **Virtual Files for Multiple Purposes**
```typescript
// Browse/Read: dark://package/MyApp.User.validate (read-only)
// Edit: dark://edit/current-patch/MyApp.User.validate (editable)
// Draft: dark://draft/MyApp.User.newFunction (new function)
// History: dark://history/MyApp.User.validate (version history)
// Patch: dark://patch/abc123 (patch overview)
// Compare: dark://compare/hash1/hash2 (version comparison)
```

### 3. **Custom Tree Views for Everything Else**
```typescript
// Package exploration: Custom tree view in Darklang container
// Patch management: Custom tree view showing operations
// Session management: Custom tree view for workspace state
```

### 4. **Custom Patch UI Instead of SCM**
```typescript
// No VS Code SCM integration - confusing for non-file changes
// Custom webview/tree view showing patch operations clearly
// Actions: Apply, Discard, Review, Sync
```




# Bridging VS Code's File-Based World with Darklang's Package Manager

## The Fundamental Gap

**VS Code Assumption:** Code lives in files on disk, changes are tracked via git, workflows built around file operations.

**Darklang Reality:** Code lives as immutable, content-addressable items in a package manager. No files, no git - everything is database-backed with built-in source control.

## The Bridge: Custom Darklang ViewContainer + Minimal Virtual Files


### 2. Custom Source Control UI

Instead of VS Code's file-based SCM, **Darklang Patch Panel** in ViewContainer:

**Current Patch: user-validation-improvements**
```
📋 user-validation-improvements
├── ✏️ Modified: MyApp.User.validate (hash: abc123 → def456)
├── ➕ Added: MyApp.User.createAccount (hash: ghi789)
├── ➖ Deprecated: MyApp.Legacy.oldFunction
└── 🔀 Actions: [Apply] [Discard] [Review]
```







## 🔄 Developer Workflows

### 1. **Starting Work Session**
```
VS Code: User clicks "New Session" 
→ LSP: darklang/sessions/create {intent: "Fix List module"}
→ F#: Creates session, saves to SQLite
→ WebSocket: Broadcasts session-started event  
→ UI: Updates session panel, status bar
```

### 2. **Creating Patch**
```
VS Code: User edits function, runs "Create Patch"
→ LSP: darklang/patches/create {intent: "Add error handling"}
→ F#: Analyzes changes, creates patch, saves to DB
→ WebSocket: Broadcasts patch-created event
→ UI: Updates patch tree, shows in draft patches
```

### 3. **Conflict Resolution**
```
Background: F# detects conflict during sync
→ WebSocket: Broadcasts conflict-detected event
→ UI: Shows notification, updates conflict tree
→ User: Clicks "Resolve"
→ LSP: darklang/conflicts/ui {conflictId: "c1"}
→ F#: Returns resolution UI data with strategies
→ UI: Shows webview with resolution options
→ User: Selects strategy
→ LSP: darklang/conflicts/resolve {conflictId: "c1", strategy: "rename-both"}
→ F#: Applies resolution, updates database
→ WebSocket: Broadcasts conflict-resolved event
```
