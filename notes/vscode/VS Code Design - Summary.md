# VS Code Design - Summary


## ViewContainer Architecture

### Separation of Concerns
```
File Explorer (Native)        Darklang ViewContainer (Custom)
├── Frontend files            ├── 📦 Packages TreeView
├── Configs                   ├── 📝 Patches TreeView
└── Documentation             └── 🎯 Sessions TreeView
```

**Critical Design**: Packages are NOT files, so they get their own activity bar section.

### Virtual File URLs
```
dark://package/Name.Space.item          # Browse package content
dark://edit/current-patch/Name.Space.item   # Edit in current patch
dark://draft/Name.Space.newItem         # Create new item
dark://history/Name.Space.item          # Version history
```



## User Flows

### 1. Opening and Connecting
1. User opens VS Code
2. `Ctrl+Shift+P` → "Darklang: Connect to Instance"
3. ViewContainer shows package tree, patches, sessions
4. Status bar: `[Darklang] 📦 Local Instance | 🎯 Session: main`

### 2. Package Navigation
1. Browse packages in custom TreeView (NOT File Explorer)
2. Click package item → opens `dark://package/...`
3. Edit button → transitions to `dark://edit/current-patch/...`
4. All edits create Ops in current patch

### 3. Patch Management
1. `Ctrl+Shift+P` → "Darklang: Create Patch"
2. Enter intent description
3. Make changes via `dark://edit/...` URLs
4. Custom webview for patch review (NOT SCM integration)
5. Apply patch when ready

**No SCM Integration**: Patches use custom UI, not VS Code's source control.



## Key Features

### Custom TreeViews
- **Packages**: Show package hierarchy with appropriate icons
- **Patches**: Current, drafts, incoming, applied (NOT in SCM view)
- **Sessions**: Switch between workspaces, transfer sessions

### Status Bar Integration
```
[Darklang] 📦 Instance | 🎯 Session | 📝 Patch | 🔄 Sync Status
(hmm maybe this shows the # of patches/edits/ops currently in context of the session?)
```

### Command Palette
- `Darklang: Browse Packages`
- `Darklang: Create Patch`
- `Darklang: Switch Session`
- `Darklang: Sync Patches`

### Custom Webviews
- **Patch Review**: Side-by-side diffs, validation, approval workflow
- **Conflict Resolution**: Visual conflict representation, resolution strategies
- **Session Transfer**: Export/import sessions between environments
