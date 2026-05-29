# Specifications Summary

Quick reference for Darklang branch-based development system.

**For complete overview**: See [CORE_CONCEPTS.md](CORE_CONCEPTS.md)
**For implementation**: See [IMPLEMENTATION_SUMMARY.md](IMPLEMENTATION_SUMMARY.md)

## Core Architecture

```
User (VS Code)
  ↕ LSP Protocol
LSP Server (F#)
  ↕ Function Calls
LibMatter (F#)
  ↕ SQL Queries
SQLite Database
```

## Key Components

### 1. ProgramTypes (F#)
Core types for package management:
- **Location** - Namespace paths (Darklang.Stdlib.List.map)
- **PackageOp** - Operations (AddFunction, AddType, AddValue, MoveItem, DeprecateFn/Type/Value)
- **Patch** - Ordered collections of ops
- **Branch** - Development branches containing patches
- **Instance** - Running environments (local/remote)
- **PackageFn/Type/Value** - Package items with UUIDs

### 2. LibMatter (F#)
Package management layer:
- **Op Validation** - Check ops for conflicts before execution
- **Op Execution** - Apply ops to update package tables and locations
- **Branch Projection** - Overlay branch changes on merged state
- **Package Manager** - Search, find, get package items
- **Conflict Detection** - Find merge conflicts between branches

**Database**:
- **Source of truth**: package_fns/types/values + locations (merged state)
- **Branch deltas**: branches, patches, ops (applied on top of merged state)
- Locations table: branch-aware, maps locations → IDs
- Package tables: no branch column, query by ID only

### 3. LSP Server (F#)
Language server with custom extensions:
- **Standard LSP**: Hover, completion, definition, diagnostics
- **darklang/branch/\***: Create, switch, merge branches
- **darklang/patch/\***: Create, validate, add ops to patches
- **darklang/package/\***: Search, get, browse packages
- **darklang/conflict/\***: List, resolve conflicts

### 4. VS Code Extension (TypeScript)
Thin UI layer routing through LSP:
- **Tree Views**: Branch (patches), Packages (browse), Current (status)
- **Webview Panels**: Home, Patch Review, Conflict Resolution, Branches Manager
- **Commands**: Branch, patch, package, conflict operations
- **Status Bar**: Show current instance and branch
- **URL Routing**: dark:/// scheme for virtual documents

### 5. CLI (F#)
Command-line interface:
- **Branch**: create, switch, merge, abandon, list
- **Patch**: create, view, export, import, list
- **Package**: search, view
- **Conflict**: list, resolve
- **Instance**: switch, list

## Development Flow

1. **Create Branch** - Isolate changes from instance
2. **Edit Code** - Open package items, make changes
3. **Generate Ops** - Parse edits, create AddFunction/AddType/AddValue/MoveItem/Deprecate ops
4. **Organize into Patches** - Group related ops
5. **Validate** - Check for conflicts, type errors
6. **Merge** - Apply location changes to instance (ops → locations table)
7. **Sync** (optional) - Share branches with team

## Key Innovations

### ID + Location Separation
- **ID (UUID)**: Stable reference, stored in package tables
- **Location**: Human-readable path, stored in locations table
- Package items have NO location field - location separate
- Same ID can exist at multiple locations (aliases)
- Move/rename only touches locations table

### Branch-Aware Locations Table
- **Only locations table** has `branch_id` column
- Package tables (package_fns/types/values) have NO branch column
- `branch_id NULL` = merged/global, non-null = branch-specific
- Queries overlay branch changes: `(branch_id = ? OR branch_id IS NULL)`
- Simpler: Look up ID in locations (branch-aware), fetch from package_fns by ID (no branch)

### Source of Truth: Snapshot + Delta
- **Package tables** are source of truth for merged state
- **Branches/patches/ops** are deltas applied on top
- Fast instance initialization (no replay needed)
- See source-of-truth-rethink.md for details

## Key Decisions

1. **Multiple branches**: Supported - locations table with branch_id handles routing
2. **Unparseable code**: Allowed in branches (warnings), blocked on merge (see unparseable-code.md)
3. **Validation**: Warning-only in branch, enforced on merge
4. **Function updates**: Always new ID (simpler than versioning)

See open-questions.md for remaining design decisions.

## Implementation Phases

### Phase 1: Core Foundation
- Finalize ProgramTypes.fs
- Implement LibMatter core (ops, DB schema)
- Basic LSP server (branch/patch CRUD)
- Minimal VS Code extension (connect to LSP, basic views)

### Phase 2: Editing & Validation
- Parse Darklang code on save
- Generate ops from edits
- Op validation (conflicts, type checking)
- Conflict detection UI

### Phase 3: Branch Merging
- Merge branch to instance
- Clear branch-specific DB rows
- Handle merge conflicts
- Post-merge cleanup

### Phase 4: Sync & Collaboration
- HTTP server for branch/patch distribution
- Pull patches from remote
- Detect remote conflicts
- Merge remote changes

### Phase 5: Polish
- CLI improvements
- Advanced search
- Performance optimization
- Documentation

## File Changes from Current State

### Remove
- Old LibPackageManager hash-based types
- Op.T (throwaway, use PackageOp instead)
- WorkspaceState.T from Branch type
- Fake data in VS Code extension
- UpdateFunction op (use DeprecateFn + AddFunction)

### Add
- `specs/` directory (this document)
- LibMatter/ (new F# library)
- LSP custom protocol extensions
- Branch-aware DB schema
- VS Code LSP integration

### Modify
- ProgramTypes.fs (use PackageOp, remove workspace from Branch)
- VS Code extension (route through LSP instead of fake data)
- CLI (add branch commands)
- DB schema (locations table with branch_id, package tables without)

## Success Criteria

Users can:
1. Create isolated development branches
2. Edit functions/types/values in VS Code
3. See changes grouped into patches
4. Merge patches to local instance after validation
5. Resolve conflicts when they occur
6. Search and browse packages
7. Use CLI for automation

System provides:
1. Deterministic package state from ops
2. Conflict detection before merge
3. Branch isolation (can't break instance while developing)
4. Audit trail (all changes recorded)
5. Sync capability (for team collaboration)

## Next Steps

1. Read and review all specs
2. Identify gaps or inconsistencies
3. Iterate on design decisions
4. Get feedback on open questions
5. Begin Phase 1 implementation
