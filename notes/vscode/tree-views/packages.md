
## 2. Tree View Data Provider in Custom ViewContainer

### VS Code Feature
`TreeDataProvider<T>` interface for custom tree views, but NOT in Explorer panel.

### How We Use It (Custom ViewContainer)
**Package TreeView in Darklang ViewContainer:**
```typescript
class DarklangPackageTreeProvider implements vscode.TreeDataProvider<PackageTreeNode> {
  constructor(private lspClient: LanguageClient) {}

  async getChildren(element?: PackageTreeNode): Promise<PackageTreeNode[]> {
    const parentPath = element?.packagePath || "";
    // Get package tree data from Darklang packages
    const response = await this.lspClient.sendRequest('darklang/getPackageChildren', {
      parentPath
    });
    return response.children.map(child => new PackageTreeNode(child));
  }

  getTreeItem(element: PackageTreeNode): vscode.TreeItem {
    return {
      label: element.name,
      iconPath: this.getPackageIcon(element.type),
      contextValue: `package.${element.type}`,
      command: element.isPackageItem() ? {
        command: 'darklang.openPackageItem',
        title: 'Open Package Item',
        arguments: [`dark://package/${element.packagePath}`]
      } : undefined,
      collapsibleState: element.hasChildren ?
        vscode.TreeItemCollapsibleState.Collapsed :
        vscode.TreeItemCollapsibleState.None
    };
  }

  private getPackageIcon(type: string): vscode.ThemeIcon {
    switch (type) {
      case 'owner': return new vscode.ThemeIcon('organization');
      case 'module': return new vscode.ThemeIcon('folder');
      case 'function': return new vscode.ThemeIcon('symbol-function');
      case 'type': return new vscode.ThemeIcon('symbol-class');
      case 'value': return new vscode.ThemeIcon('symbol-constant');
      default: return new vscode.ThemeIcon('question');
    }
  }
}

class PackageTreeNode {
  constructor(
    public name: string,
    public type: 'owner' | 'module' | 'function' | 'type' | 'value',
    public packagePath: string,
    public hasChildren: boolean
  ) {}

  isPackageItem(): boolean {
    return ['function', 'type', 'value'].includes(this.type);
  }
}
```

**Critical Design Differences:**
- **NOT in Explorer**: This tree view is in the custom Darklang ViewContainer
- **Package Navigation**: Shows package hierarchy, not file system
- **Virtual URIs for Browse/Edit**: Clicking opens `dark://package/...` for browsing, with natural transitions to `dark://edit/current-patch/...` for editing
- **No File Confusion**: Users understand this is package browsing, not file browsing
- **Rich URL Patterns**: Supports `dark://package/...`, `dark://edit/...`, `dark://history/...`, `dark://compare/...`
```

### 80% Darklang Logic - Package Implementation:
```darklang
// Package: Darklang.VSCode.TreeView
module Darklang.VSCode.TreeView =

  type TreeNode = {
    id: String
    label: String
    nodeType: TreeNodeType
    icon: String
    hasChildren: Bool
    contextValue: String
    command: (Option Command)
  }

  let getPackageChildren (parentPath: String) : List<TreeNode> =
    if String.isEmpty parentPath then
      // Root level - get all package owners
      PackageDB.getAllOwners ()
      |> List.map createOwnerNode
    else if not (String.contains "." parentPath) then
      // Owner level - get modules for owner
      PackageDB.getModulesForOwner parentPath
      |> List.map (createModuleNode parentPath)
    else
      // Module level - get package items
      let location = PackageLocation.parse parentPath
      PackageDB.getItemsInModule location
      |> List.map createPackageItemNode

  let createOwnerNode (owner: String) : TreeNode =
    TreeNode.create
      owner
      owner
      TreeNodeType.Owner
      "organization"
      true
      "package.owner"
      None

  let createPackageItemNode (item: PackageItem) : TreeNode =
    let icon = match item.type with
                | Function -> "symbol-function"
                | Type -> "symbol-class"
                | Value -> "symbol-constant"

    let command = Command.create
                    "Open"
                    "darklang.openPackageItem"
                    [`dark://package/${PackageLocation.toString item.location}`]

    TreeNode.create
      (PackageLocation.toString item.location)
      item.name
      TreeNodeType.PackageItem
      icon
      false
      ("package." ++ item.type.toString())
      (Some command)

  let getContextActions (nodeType: TreeNodeType) : List<ContextAction> =
    match nodeType with
    | Owner -> [
        ContextAction.create "Browse All Packages" "darklang.browseOwnerPackages"
        ContextAction.create "View Statistics" "darklang.viewOwnerStats"
      ]
    | Module -> [
        ContextAction.create "Open Module View" "darklang.openModule"
        ContextAction.create "Search Within" "darklang.searchModule"
      ]
    | PackageItem -> [
        ContextAction.create "Open (dark://package/...)" "darklang.openPackageItem"
        ContextAction.create "Edit (dark://edit/current-patch/...)" "darklang.editPackageItem"
        ContextAction.create "View History (dark://history/...)" "darklang.viewPackageHistory"
        ContextAction.create "Run Tests" "darklang.runPackageTests"
        ContextAction.create "View Documentation" "darklang.viewPackageDocs"
      ]
```

### 15% F# Infrastructure - CLI Bridge:
```fsharp
// CLI command bridges to Darklang package
let handleTreeViewPackages (parentPath: string) : Task<obj> = task {
  let! nodes = callDarklangFunction
    "Darklang.VSCode.TreeView.getPackageChildren"
    [DString parentPath]

  return serializeToVSCodeFormat nodes
}
```

### Benefits of Custom ViewContainer Approach
- **Separate Concerns**: Package navigation separate from file navigation
- **Package-Aware**: Tree understands package semantics, not file structure
- **Custom Context**: Package-specific actions, not file actions
- **No User Confusion**: Clear distinction between files and packages
- **Appropriate Icons**: Package-specific icons, not file type icons






### Package TreeView Implementation
```typescript
class DarklangPackageTreeProvider implements vscode.TreeDataProvider<PackageTreeNode> {
  async getChildren(element?: PackageTreeNode): Promise<PackageTreeNode[]> {
    // Delegate to Darklang packages
    const response = await this.lspClient.sendRequest('darklang/getPackageChildren', {
      parentPath: element?.packagePath || ""
    });
    return response.children.map(child => new PackageTreeNode(child));
  }
}
```

### Darklang Tree Logic
```darklang
// Package: Darklang.VSCode.TreeView
module Darklang.VSCode.TreeView =
  let getPackageChildren (parentPath: String) : List<TreeNode> =
    if String.isEmpty parentPath then
      PackageDB.getAllOwners()
      |> List.map createOwnerNode
    else
      PackageDB.getChildrenOf parentPath
      |> List.map createPackageItemNode
```



**Darklang Packages View** (separate from File Explorer):
```
📦 PACKAGES
├── 📁 MyApp (12 functions, 3 types)
│   ├── ⚡ User.validate → abc123
│   ├── ⚡ User.create → def456
│   └── 📊 User (type) → ghi789
├── 📁 Darklang.Stdlib (imported)
│   ├── 📁 List
│   └── 📁 Option
└── 📁 ThirdParty.Auth (v2.1.0)






## Custom TreeView Specifications

### Packages TreeView (In Darklang ViewContainer)

**Key Principle**: This is NOT in File Explorer - it's a custom tree view in the Darklang ViewContainer

**Structure:**
```
📦 Packages (Custom TreeView)
├── 🏢 Darklang
│   ├── 📁 Stdlib
│   │   ├── 📁 List
│   │   │   ├── 🔧 map        # Click → opens dark://package/Darklang.Stdlib.List.map in editor
│   │   │   ├── 🔧 filter     # Click → opens dark://package/Darklang.Stdlib.List.filter in editor
│   │   │   └── 🔧 fold
│   │   ├── 📁 Option
│   │   │   ├── 📋 Option (type)
│   │   │   ├── 🔧 map
│   │   │   └── 🔧 withDefault
│   │   └── 📁 String
│   │       └── 🔧 length
│   └── 📁 Http
│       ├── 🔧 get
│       └── 🔧 post
├── 🌐 Community
│   └── 📁 JSON
│       ├── 🔧 parse
│       └── 🔧 stringify
└── 👤 Local
    └── 📁 MyProject
        └── 🔧 validateUser
```

**Critical Design Decisions:**
- **NOT in File Explorer**: Packages are not files, so they don't belong in file navigation
- **Custom TreeView**: Uses VS Code's TreeDataProvider but in custom ViewContainer
- **Virtual File for Browse/Edit**: Clicking opens `dark://package/...` for browsing, then transitions to `dark://edit/current-patch/...` for editing
- **No File System Confusion**: Users understand this is package navigation, not file navigation
- **Natural Flow**: Browse → Edit transitions via URL patterns

**Context Actions:**
- Right-click function: Open (`dark://package/...`), Edit (`dark://edit/current-patch/...`), View History (`dark://history/...`), Copy Package Reference, View Documentation, Run Tests
- Right-click module: Browse Module (`dark://package/...`), Search Within Module, Add to Bookmarks
- Right-click owner: Browse All Packages, View Package Statistics