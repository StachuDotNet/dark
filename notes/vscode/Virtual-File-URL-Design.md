# Darklang Virtual File URL Design

## Approved URL Patterns

### Core URL Scheme
```
dark://package/Name.Space.item                   # Browse/read (both module-level and item-specific)
    if you edit one of these, we fork you over to a Patch


maybe a Patch is just one virtual file, for now.
with really good collapse and module magic
and keywords like 'deprecate'
idk what else



dark://edit/current-patch/Name.Space.item        # Edit in current patch
dark://history/Name.Space.item                   # Version history (item or module level)
dark://patch/abc123                              # Patch overview
dark://compare/hash1/hash2                       # Version comparison
```

### Package Browse Examples
```
dark://package/Darklang.Stdlib.List.map          # Browse function
dark://package/MyApp.User                        # Browse module (shows all functions/types)
dark://package/MyApp.User.Profile                # Browse specific type
```

### Special Views
```
```

### Comparison Views
```
dark://compare/hash1/hash2                       # Compare two versions
dark://compare/current/patch-abc123              # Compare current with patch
```

## User Flow Examples

### 1. Natural Browse → Edit Flow
```
1. User clicks "MyApp.User.validate" in tree view
2. Opens: dark://package/MyApp.User.validate (browse mode)
3. User clicks "Edit" button or uses Edit context action
4. Transitions to: dark://edit/current-patch/MyApp.User.validate (edit mode)
5. User can view history: dark://history/MyApp.User.validate
```

### 2. Module Browse → Function Edit
```
1. User clicks "MyApp.User" module in tree
2. Opens: dark://package/MyApp.User (shows all functions/types in module)
3. User clicks "Edit validate()" or uses context action
4. Transitions to: dark://edit/current-patch/MyApp.User.validate
5. Can compare versions: dark://compare/hash1/hash2
```

### 3. New Item Creation Flow
```
1. User clicks "New Function" or uses "Create New" action
2. Opens: dark://draft/MyApp.User.newFunction (template)
3. User writes function, saves
4. Automatically creates patch, URL becomes: dark://edit/current-patch/MyApp.User.newFunction
5. Can view patch overview: dark://patch/abc123
```

## URL Structure Pattern

### URL Structure Pattern
```
dark://[mode]/[context]/[target]

mode:    package | edit | draft | patch | history | compare
context: current-patch | patch-id | hash1/hash2
target:  full.package.name (e.g., Name.Space.item)
```

### URL Examples with Context
```
# Browse/read current version
dark://package/MyApp.User.validate

# Edit in current patch
dark://edit/current-patch/MyApp.User.validate

# Edit in specific patch
dark://edit/patch-abc123/MyApp.User.validate

# View version history
dark://history/MyApp.User.validate

# Compare versions
dark://compare/hash1/hash2

# View patch overview
dark://patch/abc123

# Create new item
dark://draft/MyApp.User.newFunction
```

## View Variations with Query Parameters

### Function Views
```
dark://package/MyApp.User.validate               # Default source view
dark://package/MyApp.User.validate?view=ast      # AST representation
dark://package/MyApp.User.validate?view=types    # Type annotations
dark://package/MyApp.User.validate?view=docs     # Documentation view
```

### Module Views
```
dark://package/MyApp.User                        # Module overview (all items)
dark://package/MyApp.User?view=graph             # Dependency graph
dark://package/MyApp.User?view=exports           # Public interface
dark://package/MyApp.User?view=tests             # Test coverage
```

## Natural URL Transitions

### Browse → Edit Flow
```
# Browse mode
dark://package/MyApp.User.validate
    ↓ (user clicks "Edit" or uses context action)
dark://edit/current-patch/MyApp.User.validate

# Auto-patch creation on first edit
dark://edit/current-patch/MyApp.User.validate
    ↓ (user makes changes, auto-save creates patch)
dark://edit/current-patch/MyApp.User.validate (stays same, but now in patch abc123)
```

### Additional Navigation
```
# From any item, can view history
dark://package/MyApp.User.validate → dark://history/MyApp.User.validate

# From patch work, can view patch overview
dark://edit/current-patch/MyApp.User.validate → dark://patch/abc123

# From history, can compare versions
dark://history/MyApp.User.validate → dark://compare/hash1/hash2
```

### Status Bar Context
```
When browsing dark://package/MyApp.User.validate:
Status bar shows: "📦 MyApp.User.validate | Current version"

When editing dark://edit/current-patch/MyApp.User.validate:
Status bar shows: "📝 Editing: MyApp.User.validate | Patch: user-validation (abc123)"

When viewing dark://history/MyApp.User.validate:
Status bar shows: "📜 History: MyApp.User.validate | 15 versions"
```



## Implementation Benefits

### Development Flow
- "Go to Definition" opens `dark://package/...` (browse mode)
- "Edit Definition" opens `dark://edit/current-patch/...` (edit mode)
- Version history via `dark://history/...`
- Patch management via `dark://patch/...`
- Version comparison via `dark://compare/...`
