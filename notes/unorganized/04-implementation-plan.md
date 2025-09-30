# Phase 4: Implementation Planning & Code Changes

## Building on Flows
You should now have core concepts and key developer flows documented. Time to plan the actual implementation.

## Code Changes Needed

### ProgramTypes.fs Additions
Op/PatchOp, Patch, Session, Sync, basic validation types



### CLI Implementation  
**New capabilities needed**:
- Commands for sessions, patches, status, diff
- Display current patch/branch state
- Sync settings (manual/auto)
- Patch focus management

### VS Code Extension
- Virtual file system provider enhancements
- Package tree view integration  
- SCM integration components
- Webview panels for patch review
- Real-time update mechanisms


### Database Changes
- Tables for Ops, Patches, Sessions?
- Sync state tracking?
- Branch dependency representation?

### Darklang Builtins
- Session management (start, continue)
- Patch operations
- Status and diff viewing
- Op manipulation


## Package Management Evolution

### Critical Questions
- How do we separate names from definitions?
- Should package tables be projections of patches?
- How does package search work with branches?
- In-memory projections vs SQL storage?
- How do we handle the Values vs Globals split?


## Sync Protocol Design

### Instance Communication
- HTTP-based protocol between instances
- Op validation and conflict detection
- Merge strategies
- Offline/online transition handling



## Validation Strategy

### Patch Validation
```fsharp
let checkEdit: Edit -> Result<OpsToDo, ValidationError>
```

**Failure modes**:
- New tests fail
- Regressions (old tests now failing)
- Unknown references
- Conflicting edits

### Conflict Resolution
- Model OpConflict/PatchOpConflict types
- Manual vs automatic resolution
- UI for resolution in both CLI and VS Code




### View Flexibility  
Support viewing code at different phases:
- WrittenTypes AST
- ProgramTypes AST
- Pretty-printed with custom formatting
- RuntimeTypes (Instructions)
- Custom views
