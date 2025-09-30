# Expandable Tree View Source Control UI

## Overview

The source control UI uses an **expandable tree view** within the Darklang ViewContainer that provides progressive disclosure of change information, from high-level summaries down to detailed diffs.

## Tree View Hierarchy

### Level 1: Session Summary (Top Level)
```
🔄 Session: user-improvements
├── 📊 3 patches, 12 items changed, 2 conflicts
├── ✅ validation-enhancements (ready)
├── ⚠️ user-profile-features (conflicts)
└── 📝 performance-optimizations (draft)
```

### Level 2: Patch Summary (Expandable)
```
✅ validation-enhancements (ready)
├── 📊 3 functions modified, 1 type added, 0 conflicts
├── 🔧 Functions (3)
├── 📋 Types (1)
└── 💾 Values (0)
```

### Level 3: Item Type Summary (Expandable)
```
🔧 Functions (3)
├── 📝 MyApp.User.validate (modified)
├── 🆕 MyApp.User.createFromEmail (added)
└── 📝 MyApp.User.create (modified)
```

### Level 4: Individual Items (Expandable)
```
📝 MyApp.User.validate (modified)
├── 📜 Current → hash_validate_v3 (you, 2 hours ago)
├── 📜 Previous → hash_validate_v2 (alice, 1 day ago)
├── 🔍 5 lines added, 12 lines changed, 2 lines removed
└── ⚠️ Uses deprecated MyApp.Utils.Email.oldValidate
```

### Level 5: Historical Versions (Expandable)
```
📜 All versions of MyApp.User.validate
├── 📌 hash_validate_v3 (current) - you, 2 hours ago
├── 📌 hash_validate_v2 - alice, 1 day ago
├── 📌 hash_validate_v1 (deprecated) - bob, 3 days ago
└── 📌 hash_validate_v0 (original) - alice, 1 week ago
```

## Complete Tree View Example

```
🏢 Darklang Source Control
│
├── 🔄 Active Session: user-improvements
│   ├── 📊 Summary: 3 patches • 12 items • 2 conflicts • Started 2h ago
│   │
│   ├── ✅ validation-enhancements (ready to merge)
│   │   ├── 📊 3 functions modified • 1 type added • 0 values • Ready 🟢
│   │   ├── 🔧 Functions (3 changes)
│   │   │   ├── 📝 MyApp.User.validate
│   │   │   │   ├── 📜 hash_validate_v2 → hash_validate_v3
│   │   │   │   ├── 🔍 +5 lines, ~12 lines, -2 lines
│   │   │   │   ├── ✨ Added age validation logic
│   │   │   │   ├── 🔧 Improved email validation
│   │   │   │   ├── 🎯 Better error messages
│   │   │   │   └── 📜 Show all versions (4 total)
│   │   │   │       ├── 📌 hash_validate_v3 (current) - you, 2h ago
│   │   │   │       ├── 📌 hash_validate_v2 - alice, 1d ago
│   │   │   │       ├── 📌 hash_validate_v1 (deprecated) - bob, 3d ago
│   │   │   │       └── 📌 hash_validate_v0 (original) - alice, 1w ago
│   │   │   ├── 🆕 MyApp.User.createFromEmail
│   │   │   │   ├── 📜 (new) → hash_create_email_v1
│   │   │   │   ├── 🔍 45 lines added
│   │   │   │   └── ✨ Email-based user creation with validation
│   │   │   └── 📝 MyApp.User.create
│   │   │       ├── 📜 hash_create_v2 → hash_create_v3
│   │   │       ├── 🔍 +2 lines, ~5 lines
│   │   │       └── 🔧 Updated to use new validation logic
│   │   └── 📋 Types (1 change)
│   │       └── 🆕 MyApp.User.ValidationResult
│   │           ├── 📜 (new) → hash_validation_result_v1
│   │           ├── 🔍 15 lines added
│   │           └── ✨ Enhanced result type with detailed errors
│   │
│   ├── ⚠️ user-profile-features (2 conflicts)
│   │   ├── 📊 5 functions added • 1 type modified • 2 conflicts ❌
│   │   ├── ⚠️ Conflicts (2)
│   │   │   ├── 🚫 MyApp.User.update - conflicts with validation-enhancements
│   │   │   │   ├── 💡 Alice changed signature, Bob added profile fields
│   │   │   │   ├── 🔧 Resolution: Merge both changes
│   │   │   │   └── 📄 View conflict details
│   │   │   └── 🚫 MyApp.User.Profile.render - conflicts with ui-improvements
│   │   │       ├── 💡 Different template approaches
│   │   │       ├── ⚖️ Resolution: Choose one or create hybrid
│   │   │       └── 📄 View conflict details
│   │   ├── 🔧 Functions (5 changes)
│   │   │   ├── 🆕 MyApp.User.Profile.create
│   │   │   ├── 🆕 MyApp.User.Profile.update
│   │   │   ├── 🆕 MyApp.User.Profile.delete
│   │   │   ├── 🆕 MyApp.User.Profile.render (🚫 conflict)
│   │   │   └── 📝 MyApp.User.update (🚫 conflict)
│   │   └── 📋 Types (1 change)
│   │       └── 📝 MyApp.User.Profile
│   │           ├── 📜 hash_profile_v1 → hash_profile_v2
│   │           ├── 🔍 +8 lines, ~3 lines
│   │           └── ✨ Added avatar and bio fields
│   │
│   └── 📝 performance-optimizations (draft)
│       ├── 📊 2 functions modified • 0 types • 1 value • Draft mode 📝
│       ├── 🔧 Functions (2 changes)
│       │   ├── ⚡ MyApp.Database.User.findByEmail
│       │   │   ├── 📜 hash_find_v1 → hash_find_v2
│       │   │   ├── 🔍 +12 lines, ~8 lines
│       │   │   └── 🚀 Added caching layer for email lookups
│       │   └── ⚡ MyApp.User.validate
│       │       ├── 📜 hash_validate_v3 → hash_validate_v4
│       │       ├── 🔍 +5 lines, ~15 lines
│       │       └── 🚀 Lazy validation with early exit
│       └── 💾 Values (1 change)
│           └── 🆕 MyApp.Config.Cache.userLookupTTL
│               ├── 📜 (new) → hash_cache_ttl_v1
│               ├── 🔍 Value: 300 (seconds)
│               └── ⚙️ TTL configuration for user cache
│
├── 📚 Recent Patches (merged)
│   ├── ✅ bug-fixes-batch-3 (merged 1 day ago)
│   │   ├── 📊 4 functions modified • 0 types • 0 values
│   │   └── 🔧 Click to expand details...
│   ├── ✅ ui-improvements (merged 2 days ago)
│   │   ├── 📊 8 functions modified • 2 types added • 3 values
│   │   └── 🔧 Click to expand details...
│   └── ✅ initial-user-system (merged 1 week ago)
│       ├── 📊 15 functions added • 5 types added • 2 values
│       └── 🔧 Click to expand details...
│
└── 🔍 Advanced
    ├── 📊 Package Statistics
    ├── 🌊 Commit History
    ├── 🔍 Search Changes
    ├── 📈 Impact Analysis
    └── ⚙️ Sync Settings
```

## Item State Indicators

### Change Type Icons
- 🆕 **Added** - New item created
- 📝 **Modified** - Existing item changed
- 🗑️ **Removed** - Item deleted/deprecated
- ➡️ **Moved** - Item moved to different location
- 🔄 **Renamed** - Item name changed
- ⚡ **Optimized** - Performance improvements
- 🐛 **Fixed** - Bug fixes
- ✨ **Enhanced** - Feature additions

### Status Icons
- ✅ **Ready** - Patch ready to merge
- ⚠️ **Conflicts** - Has unresolved conflicts
- 📝 **Draft** - Work in progress
- 🚫 **Blocked** - Cannot proceed due to dependencies
- 🔄 **In Review** - Under review by team
- ⏳ **Pending** - Waiting for external dependency

### Historical Indicators
- 📌 **Current** - Active version pointed to by name
- 📜 **Historical** - Previous version, still associated with name
- ❌ **Deprecated** - Marked as deprecated
- 🗑️ **Orphaned** - No longer associated with any name

## Interaction Patterns

### Click Actions
```typescript
// Single click: Select item, show preview
onItemClick(item: TreeItem) {
  showPreview(item)
  highlightRelatedItems(item)
}

// Double click: Open item for viewing/editing
onItemDoubleClick(item: TreeItem) {
  if (item.type === 'patch') {
    openURL(`dark://session/${item.sessionId}`)
  } else if (item.type === 'function') {
    openURL(`dark://edit/${item.sessionId}/${item.name}`)
  }
}

// Right click: Context menu
onItemRightClick(item: TreeItem) {
  showContextMenu([
    'View Details',
    'Edit Item',
    'View History',
    'Compare Versions',
    'Resolve Conflicts',
    'Revert Changes'
  ])
}
```

### Expansion States
```typescript
// Progressive disclosure based on user interest
const expansionStates = {
  session: 'auto-expand',      // Always show session summary
  patches: 'collapsed',        // Expand on demand
  itemTypes: 'collapsed',      // Expand when patch selected
  items: 'collapsed',          // Expand when item type selected
  versions: 'manual-only'      // Never auto-expand, explicit user action
}
```

### Search and Filter
```
🔍 Search Changes: [validation____________] 🔍
├── Filters: [ ] Functions [✓] Types [ ] Values
├── Status: [✓] Ready [✓] Conflicts [ ] Draft
└── Time: [ ] Last hour [✓] Last day [ ] Last week

Results (3 matching items):
├── 📝 MyApp.User.validate (validation-enhancements)
├── 🆕 MyApp.User.ValidationResult (validation-enhancements)
└── ⚡ MyApp.User.validate (performance-optimizations)
```

## Showing Non-Current Versions

### Historical Version View
```
📜 All versions of MyApp.User.validate
├── 📌 hash_validate_v3 (CURRENT)
│   ├── 👤 you, 2 hours ago
│   ├── 🏷️ Currently pointed to by: MyApp.User.validate
│   ├── 📊 Used by 15 functions
│   └── 🔧 In patch: validation-enhancements
├── 📜 hash_validate_v2 (historical)
│   ├── 👤 alice, 1 day ago
│   ├── 🏷️ Was pointed to by: MyApp.User.validate (1 day ago - 2 hours ago)
│   ├── 📊 Was used by 12 functions
│   └── ✨ Enhanced email validation
├── 📜 hash_validate_v1 (deprecated)
│   ├── 👤 bob, 3 days ago
│   ├── 🏷️ Was pointed to by: MyApp.User.validate (3 days ago - 1 day ago)
│   ├── ❌ Deprecated: "Basic validation insufficient"
│   └── 🔧 Original implementation
└── 📜 hash_validate_v0 (original)
    ├── 👤 alice, 1 week ago
    ├── 🏷️ Was pointed to by: MyApp.User.validate (1 week ago - 3 days ago)
    └── 🌱 Initial version
```

### Name Association History
```
📋 MyApp.User.validate (name history)
├── 📌 CURRENT: hash_validate_v3 (2 hours ago - now)
├── 📜 PREVIOUS: hash_validate_v2 (1 day ago - 2 hours ago)
├── 📜 PREVIOUS: hash_validate_v1 (3 days ago - 1 day ago) [deprecated]
└── 📜 ORIGINAL: hash_validate_v0 (1 week ago - 3 days ago)

Items that have used this name:
├── 📌 hash_validate_v3 (current, active)
├── 📜 hash_validate_v2 (historical, remembers this name)
├── 📜 hash_validate_v1 (historical, remembers this name, deprecated)
└── 📜 hash_validate_v0 (historical, remembers this name)
```

### Cross-Reference View
```
📎 Items associated with name "MyApp.User.validate"
├── 📌 Currently points to: hash_validate_v3
├── 📜 Previously pointed to (4 items):
│   ├── hash_validate_v2 - active 1 day ago to 2 hours ago
│   ├── hash_validate_v1 - active 3 days ago to 1 day ago (deprecated)
│   └── hash_validate_v0 - active 1 week ago to 3 days ago
├── 🔗 Items that remember this name (4 items):
│   ├── hash_validate_v3 - original name: MyApp.User.validate ⭐ current
│   ├── hash_validate_v2 - original name: MyApp.User.validate
│   ├── hash_validate_v1 - original name: MyApp.User.validate (deprecated)
│   └── hash_validate_v0 - original name: MyApp.User.validate
└── 📊 Usage statistics:
    ├── Total versions: 4
    ├── Currently referenced by: 15 functions
    ├── Peak usage: 18 functions (hash_validate_v2)
    └── Deprecated versions: 1
```

## Advanced Features

### Dependency Impact Visualization
```
🌐 Impact Analysis: MyApp.User.validate changes
├── 📊 Direct dependencies (3):
│   ├── 🔧 MyApp.Business.processUser - will need update
│   ├── 🔧 MyApp.API.userEndpoint - compatible
│   └── 🔧 MyApp.Utils.validateInput - compatible
├── 📊 Indirect dependencies (12):
│   ├── 🔧 MyApp.Orders.createOrder (via processUser)
│   ├── 🔧 MyApp.Reports.userStats (via processUser)
│   └── 🔧 10 more functions...
├── ⚠️ Breaking changes detected (1):
│   └── 🚫 Return type changed: Bool → Result<Bool, Error>
└── 💡 Suggested actions:
    ├── 🔧 Update processUser to handle Result type
    ├── 📝 Add migration guide for API users
    └── 🧪 Run affected tests before merging
```

### Timeline View
```
⏰ Change Timeline: MyApp.User module (last 7 days)
├── 📅 Today
│   ├── 2h ago - 📝 validate modified (you)
│   └── 4h ago - 🆕 ValidationResult added (you)
├── 📅 Yesterday
│   ├── 10h ago - 📝 validate modified (alice)
│   └── 18h ago - 🆕 createFromEmail added (alice)
├── 📅 3 days ago
│   └── 14h ago - ❌ validate deprecated old version (bob)
└── 📅 1 week ago
    └── 09h ago - 🌱 validate originally created (alice)
```

## Implementation Notes

### TreeDataProvider Structure
```typescript
interface SourceControlTreeItem {
  id: string
  label: string
  tooltip: string
  iconPath: vscode.ThemeIcon
  collapsibleState: vscode.TreeItemCollapsibleState
  contextValue: string  // For context menu
  children?: SourceControlTreeItem[]

  // Custom properties
  itemType: 'session' | 'patch' | 'itemType' | 'item' | 'version'
  changeType: 'added' | 'modified' | 'removed' | 'moved' | 'renamed'
  conflictState: 'none' | 'conflict' | 'resolved'

  // Actions
  command?: vscode.Command
  resourceUri?: vscode.Uri
}
```

### Performance Optimization
- **Lazy loading**: Only load tree details when expanded
- **Virtual scrolling**: Handle large numbers of changes efficiently
- **Caching**: Cache expensive tree calculations
- **Progressive disclosure**: Show summaries before details

This expandable tree view provides comprehensive source control visibility while maintaining performance and usability through progressive disclosure and intuitive interaction patterns.