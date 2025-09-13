# Phase 4: Implementation Planning & Code Changes

## Building on Flows
You should now have core concepts and key developer flows documented. Time to plan the actual implementation.

## Code Changes Needed

### ProgramTypes.fs Additions
**Probably needs**: Op/PatchOp, Patch, Session, Sync, basic validation types

**Questions**:
- Should details go in LibPackageManager instead?
- How do we keep PT.fs from getting bloated?
- What's the minimal type set?

### CLI Implementation  
**New capabilities needed**:
- Commands for sessions, patches, status, diff
- Display current patch/branch state
- Sync settings (manual/auto)
- Patch focus management

### VS Code Extension
**Don't implement fully**, but sketch:
- Virtual file system provider enhancements
- Package tree view integration  
- SCM integration components
- Webview panels for patch review
- Real-time update mechanisms

### Database Changes
**Some migrations needed**, but what?:
- Tables for Ops, Patches, Sessions?
- Sync state tracking?
- Branch dependency representation?

### Darklang Builtins
Functions for:
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

### Hashing & References
- There's a PR in progress about referencing by hash
- How does this relate to our patch system?
- Can we proceed without it?

## Sync Protocol Design

### Instance Communication
- HTTP-based protocol between instances
- Op validation and conflict detection
- Merge strategies
- Offline/online transition handling

### Initial Implementation
- Me and my coworker exchanging .patch files
- Maybe use a home server as 'central'
- Eventually scale to full server infrastructure

## Your Implementation Tasks

1. **Sketch type definitions** in F# (create draft .fs files)
2. **Plan database schema** (what tables, relationships?)
3. **Design sync protocol** (what messages, when?)
4. **Create implementation roadmap** (what order to build things?)
5. **Identify technical risks** (what could go wrong?)

## Before Major Decisions

Ask me about:
- Database design choices (SQL vs in-memory)
- Sync protocol complexity vs simplicity
- Development priorities (CLI first vs VS Code vs both)
- Testing strategy

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

## Implementation Phases

### Phase 1: Minimal Viable (For Meetings)
- Basic patch creation
- Simple sync between two developers  
- One complete flow working

### Phase 2: Core Mechanics
- Patch validation
- Conflict detection
- Session management

### Phase 3: Developer Experience
- VS Code integration
- Real-time updates
- Advanced conflict resolution

### Phase 4: Polish & Scale
- Performance optimization
- Community features
- Documentation

## Output Requirements

Create implementation documents with:
- Code sketches (.fs files with types)
- Database schema proposals
- Sync protocol specification  
- Development roadmap with phases
- Risk assessment and mitigation

## Special Considerations

### AI Development Support
- Multiple parallel sessions
- Clear package visibility
- Exploratory development patterns

### View Flexibility  
Support viewing code at different phases:
- WrittenTypes AST
- ProgramTypes AST
- Pretty-printed with custom formatting
- RuntimeTypes (Instructions)
- Custom views

**Focus**: What's the minimal implementation that would work for two developers? Don't over-engineer, but make it extensible.

---
*Next: After implementation planning, move to 05-meeting-prep.md*