# Darklang Collaborative Development System: Complete Implementation Prompt

## Executive Summary

Transform Darklang from a language+platform with disconnected pieces into a fully integrated collaborative development environment. The goal is to bridge the gap between our existing backend (F# types, package management) and modern developer workflows through VS Code integration, session-based collaboration, and content-addressable package management.

## Current State → Target State

**What We Have:**
- F# backend with type system and execution engine
- Basic CLI with package browsing
- LSP server with language features
- Packages stored in database

**What We Need:**
- Session-based collaboration with patch management
- VS Code extension with custom UI components
- Content-addressable package system with hash-based references
- Sync protocol for sharing changes between developers
- Conflict detection and resolution system

## Core Architecture Principles

### 1. **Darklang-First Development**
- Core logic in F# (ProgramTypes.fs, LibMatter) and Darklang packages
- Minimal JavaScript in VS Code extension (just UI bridging)
- LSP protocol for editor-agnostic functionality

### 2. **Content-Addressable Everything**
- Immutable content referenced by hash
- Mutable names that point to specific hashes
- Separation between names and their content enables versioning

### 3. **Session-Centric Workflows**
- Sessions contain multiple patches and preserve working context
- Patches are collections of Ops that can be shared and applied
- Local-first with explicit sync to remote instances

### 4. **Virtual File System**
- VS Code shows packages via `dark://` URLs, not file system
- Different URL patterns for browsing, editing, history, patches
- Custom tree views for packages, patches, sessions

## Key Design Insights from Analysis

### Package Management Evolution
Based on `ProgramTypes.fs.md` and feedback, we need:

```fsharp
// Separate names from content - names are mutable pointers
type PackageName = {
  id: NameId
  location: PackageLocation.T  // Darklang.Stdlib.List.map
  currentHash: string          // Points to current version
  visibility: Public | Private
}

// Content is immutable and hash-addressed
type PackageContent = {
  hash: string
  contentType: Function | Type | Value
  content: bytes              // Serialized PackageFn/PackageType/PackageValue
  nameId: NameId             // Forever tied to original name
  createdAt: DateTime
  author: UserId
}
```

### Operation Types
Refined from feedback - specific Ops for each content type:

```fsharp
type Op =
  // Content Operations (immutable)
  | AddFunctionContent of hash: string * content: PackageFn.PackageFn * nameId: NameId
  | AddTypeContent of hash: string * content: PackageType.PackageType * nameId: NameId
  | AddValueContent of hash: string * content: PackageValue.PackageValue * nameId: NameId

  // Name Operations (mutable pointers)
  | CreateName of location: PackageLocation.T * initialHash: string * visibility: Visibility
  | UpdateNamePointer of nameId: NameId * oldHash: string * newHash: string
  | MoveName of oldLocation: PackageLocation.T * newLocation: PackageLocation.T

  // Special Operations
  | DeprecateContent of hash: string * reason: string * replacement: string option
  | MoveModule of oldPath: List<string> * newPath: List<string>

  // Mutable Value Operations (for config/settings that change over time)
  | UpdateMutableValue of nameId: NameId * newValue: Dval * preserveType: bool
```

### VS Code Integration Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        VS Code Editor                          │
│  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐  │
│  │  Custom Views   │ │ Virtual Files   │ │   Status Bar    │  │
│  │  - Packages     │ │ dark://package/ │ │ Session | Patch │  │
│  │  - Patches      │ │ dark://edit/    │ │ Sync | Conflicts│  │
│  │  - Sessions     │ │ dark://history/ │ │                 │  │
│  └─────────────────┘ └─────────────────┘ └─────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                               │ LSP + Extensions
┌─────────────────────────────────────────────────────────────────┐
│                 Enhanced Darklang LSP Server                   │
│  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐  │
│  │  Standard LSP   │ │  Collaboration  │ │  Real-time      │  │
│  │  Features       │ │  Extensions     │ │  WebSocket      │  │
│  └─────────────────┘ └─────────────────┘ └─────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                               │ Direct calls
┌─────────────────────────────────────────────────────────────────┐
│                    Darklang CLI + LibMatter                    │
│  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐  │
│  │ Session/Patch   │ │ Conflict Res.   │ │   SQLite DB     │  │
│  │ Management      │ │ & Validation    │ │   Operations    │  │
│  └─────────────────┘ └─────────────────┘ └─────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

## Implementation TODO List

### Phase 1: Core Infrastructure (Week 1-2)

#### 1.1 Update ProgramTypes.fs with Collaboration Types
**File: `backend/src/ProgramTypes.fs`**

- [ ] Add `NameId` and `PackageName` types separating names from content
- [ ] Add `Op` types with specific operations for Functions/Types/Values
- [ ] Add `Patch` type with metadata, validation, and dependency tracking
- [ ] Add `Session` type with context preservation and patch management
- [ ] Add `Instance` type for local/remote sync targets
- [ ] Add `Conflict` types for detection and resolution
- [ ] Add mutable value support for configs/feature flags

#### 1.2 Create LibMatter Implementation
**File: `backend/src/LibPackageManager/LibMatter.fs`**

- [ ] Implement content hashing functions
- [ ] Create Op execution engine with validation
- [ ] Build session management with context preservation
- [ ] Implement patch creation, validation, and application
- [ ] Create conflict detection algorithms
- [ ] Add database operations for all new types

#### 1.3 Database Schema Design
**File: `backend/src/LibPackageManager/LibMatterDb.fs`**

- [ ] Design schema separating names from content
- [ ] Create tables for sessions, patches, ops, conflicts
- [ ] Implement sync state tracking
- [ ] Add user/instance management
- [ ] Create indexes for performance

#### 1.4 CLI Command Extensions
**Files: `packages/darklang/cli/`**

- [ ] `packages/darklang/cli/session.dark` - Session management commands
- [ ] `packages/darklang/cli/patch.dark` - Patch creation and management
- [ ] `packages/darklang/cli/sync.dark` - Instance synchronization
- [ ] `packages/darklang/cli/conflicts.dark` - Conflict resolution UI
- [ ] Update `packages/darklang/cli/packages/` for session-aware browsing

### Phase 2: VS Code Integration (Week 3-4)

#### 2.1 Virtual File System Provider
**File: `vscode/src/providers/virtualFileSystemProvider.ts`**

- [ ] Implement `dark://` URL scheme handler
- [ ] Support patterns: `package/`, `edit/`, `history/`, `patch/`, `compare/`
- [ ] Create content resolvers calling LSP server
- [ ] Handle edit transitions and patch creation
- [ ] Add query parameter support for view modes

#### 2.2 Custom Tree View Providers
**Files: `vscode/src/providers/`**

- [ ] `packageTreeProvider.ts` - Session-aware package browser
- [ ] `patchTreeProvider.ts` - Patch management with expandable operations
- [ ] `sessionTreeProvider.ts` - Session switching and transfer
- [ ] `conflictTreeProvider.ts` - Visual conflict resolution
- [ ] Integration with LSP for real-time updates

#### 2.3 Command Implementations
**Files: `vscode/src/commands/`**

- [ ] `sessionCommands.ts` - Create, switch, suspend, resume sessions
- [ ] `patchCommands.ts` - Create, apply, share patches
- [ ] `syncCommands.ts` - Push/pull with conflict handling
- [ ] `conflictCommands.ts` - Resolution strategies and UI
- [ ] `packageCommands.ts` - Enhanced browsing and search

#### 2.4 Status Bar and Notifications
**File: `vscode/src/ui/statusBarManager.ts`**

- [ ] Show current session, patch count, sync status
- [ ] Display conflict indicators and user info
- [ ] Clickable actions for quick operations
- [ ] Real-time updates via WebSocket

### Phase 3: Collaboration Features (Week 5-6)

#### 3.1 Sync Protocol Implementation
**File: `backend/src/LibPackageManager/SyncProtocol.fs`**

- [ ] HTTP API for patch push/pull operations
- [ ] Conflict detection during sync
- [ ] Instance authentication and authorization
- [ ] Partial sync and dependency resolution

#### 3.2 Enhanced LSP Server
**File: `packages/darklang/languageServer/`**

- [ ] Add collaboration extensions to LSP protocol
- [ ] Implement custom request handlers for VS Code features
- [ ] WebSocket support for real-time notifications
- [ ] Session context awareness in language features

#### 3.3 Conflict Resolution System
**File: `backend/src/LibPackageManager/ConflictResolution.fs`**

- [ ] Strategy pattern for different conflict types
- [ ] Interactive resolution with user input
- [ ] Preview/testing of resolutions before applying
- [ ] Rollback capabilities for failed merges

### Phase 4: Demo Artifacts (Week 7)

#### 4.1 VS Code Extension Demo Components

**Package Management Demo:**
```typescript
// vscode/demos/packageBrowser.ts
// Fake VS Code tree view showing:
// 📦 Packages
//   ├── 🏢 Darklang.Stdlib
//   │   ├── 📁 List
//   │   │   ├── 🔧 map (modified in session)
//   │   │   └── 🔧 filter
//   │   └── 📁 String
// With click handlers opening dark:// URLs
```

**Patch Management Demo:**
```typescript
// vscode/demos/patchTree.ts
// Show expandable patch operations:
// 📝 Patches
//   ├── 🎯 Current: Fix List.map edge cases
//   │   ├── ✏️ Modified: Darklang.Stdlib.List.map
//   │   └── ➕ Added: Darklang.Stdlib.List.mapWithIndex
//   ├── 📄 Drafts (2)
//   └── 📨 Incoming (1)
```

**Session Transfer Demo:**
```typescript
// vscode/demos/sessionTransfer.ts
// WebView for exporting/importing sessions
// Shows JSON payload with patches and context
```

#### 4.2 CLI Interaction Demos

**Session Workflow Demo:**
```bash
# Create demo script showing complete workflow
dark session create "fix-list-functions" --intent "Fix edge cases in List module"
dark patch create "Handle empty lists in map function"
# Show editing in VS Code with dark:// URLs
dark patch ready
dark sync push to:main-instance
```

**Conflict Resolution Demo:**
```bash
# Show conflict detection and resolution
dark sync pull
# Conflicts detected...
dark conflicts list
dark conflicts show c1 --details
dark conflicts resolve c1 --strategy rename-both
```

#### 4.3 Mock Data and Scenarios

**Demo Database:**
```sql
-- Create realistic demo data
INSERT INTO package_names VALUES
  ('name1', 'Darklang.Stdlib.List.map', 'hash123', 'public'),
  ('name2', 'MyApp.User.validate', 'hash456', 'private');

INSERT INTO patches VALUES
  ('patch1', 'user1', 'Fix List.map edge cases', '[...]', 'ready'),
  ('patch2', 'user2', 'Add user validation', '[...]', 'draft');
```

**Demo Scenarios:**
- Two developers working on same function (conflict)
- Session transfer between machines
- Package search with session context
- Patch review and approval workflow

### Phase 5: Integration and Testing (Week 8)

#### 5.1 End-to-End Testing
- [ ] Multi-user collaboration scenarios
- [ ] Session persistence across restarts
- [ ] Sync failure recovery
- [ ] Large patch performance testing

#### 5.2 Documentation and Tutorials
- [ ] Developer workflow documentation
- [ ] VS Code extension setup guide
- [ ] CLI command reference
- [ ] Architecture explanation

## Artifact Specifications

### 1. ProgramTypes.fs Updates
Comprehensive type definitions that extend existing types with collaboration support. Include detailed documentation for each type explaining the collaboration model.

### 2. LibMatter Implementation
Full F# implementation with:
- Content addressable storage operations
- Session and patch management
- Conflict detection algorithms
- Database integration layer

### 3. VS Code Extension Codebase
Complete TypeScript implementation with:
- Virtual file system provider for dark:// URLs
- Custom tree view providers for all collaboration features
- Command implementations calling LSP server
- Status bar integration and notifications

### 4. CLI Command Extensions
Darklang packages implementing:
- Session lifecycle management
- Patch creation and application
- Sync operations with conflict handling
- Enhanced package browsing with session context

### 5. Demo Scenarios and Mock Data
Working demonstrations of:
- Complete developer collaboration workflows
- VS Code UI components with realistic data
- Conflict resolution scenarios
- Session transfer capabilities

### 6. Database Schema
Production-ready SQLite schema supporting:
- Separation of names and content
- Session and patch tracking
- Sync state management
- Performance optimization

## Success Criteria

### Technical Milestones
- [ ] Developer can create session, make changes, create patch, and sync
- [ ] Two developers can collaborate on same function with conflict resolution
- [ ] VS Code extension provides rich UI for all collaboration features
- [ ] Session state persists across restarts and machine transfers
- [ ] Package browsing works seamlessly with session context

### Demo Requirements
- [ ] 10-minute demo showing complete workflow from session creation to patch merge
- [ ] VS Code extension with working tree views and virtual file system
- [ ] CLI interactions that feel natural and powerful
- [ ] Conflict scenario with clear resolution path
- [ ] Evidence that approach scales to larger codebases

### User Experience Goals
- [ ] Developer workflow feels natural and non-intrusive
- [ ] VS Code integration leverages platform strengths without fighting it
- [ ] CLI commands are discoverable and well-documented
- [ ] Error messages are helpful and actionable
- [ ] Performance is acceptable for real-world usage

## Technical Considerations

### Architecture Decisions
- **Language Split**: F# for core logic, Darklang for CLI/user-facing features, minimal TypeScript for VS Code bridging
- **Storage**: SQLite for local state, HTTP for sync, content-addressable immutable storage
- **Editor Integration**: LSP protocol for cross-editor compatibility, VS Code extensions for rich UI

### Performance Requirements
- Package browsing should be instant (< 100ms)
- Patch creation should be near-instant
- Sync operations should handle reasonable payloads (< 10MB)
- UI should remain responsive during background operations

### Security Considerations
- User authentication for sync operations
- Instance-based privacy (don't sync private work to public instances)
- Validation of all incoming patches and ops
- Rollback capabilities for problematic changes

This comprehensive prompt synthesizes all the research and documentation into a clear development path from current state to fully functional collaborative development environment for Darklang.