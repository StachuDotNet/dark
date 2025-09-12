# Darklang SCM Implementation Plan

## Current Status (2024-09-12)

**Architecture: ✅ COMPLETE**
- Op taxonomy defined in ProgramTypes.fs 
- Database schema ready
- Design documents written
- Content-addressable architecture designed

**Implementation: ❌ MISSING**
- No Op execution functions
- No CLI SCM commands
- No content hashing
- No session management

## Phase 1: Minimal Viable SCM (Next 2 weeks)

**Goal**: Enable Ocean and Stachu to share code via basic patches

### 1.1 Core Op Implementation
```fsharp
// In LibPackageManager/SCM.fs (new file)
module LibPackageManager.SCM

// Generate content hash for functions
let hashFunction (fn: PackageFn.PackageFn) : string

// Execute basic Ops
let executeOp (op: ProgramTypes.Op.T) : Result<unit, string>
  | AddFunctionContent(hash, fn) -> insert into package_content_v0
  | CreateName(location, hash) -> insert into package_names_v0
  | UpdateNamePointer(location, newHash) -> update package_names_v0

// Create and validate patches
let createPatch (ops: List<Op.T>) (metadata: Patch.Metadata) : Patch.T
let validatePatch (patch: Patch.T) : Result<unit, ValidationError>
```

### 1.2 Basic CLI Commands
```bash
dark patch new "Add String.reverse function"
dark fn create Darklang.Stdlib.String reverse "Reverses a string"  
dark patch show [patch-id]
dark session new --name "my-work" --base main
dark session status
```

### 1.3 CLI Registry Updates
```fsharp
// Add to packages/darklang/cli/core.dark Registry.allCommands():
("patch", "Manage patches", [], Patches.execute, Patches.help, Patches.complete)
("session", "Manage sessions", [], Sessions.execute, Sessions.help, Sessions.complete) 
("sync", "Sync with remote", [], Sync.execute, Sync.help, Sync.complete)
```

### 1.4 Simple Content Hashing
```fsharp
// Hash based on function definition content
let hashPackageFn (fn: PackageFn.PackageFn) : string =
  let serialized = serialize fn
  SHA256.hash serialized |> toHex
```

## Phase 2: Enhanced Workflow (Month 2)

### 2.1 Session Management
- `dark session list`
- `dark session switch [name]`
- Session-aware CLI prompt showing current session
- Persist sessions in database

### 2.2 Basic Sync
- `dark sync push` - upload current session patches
- `dark sync pull` - download remote patches  
- Simple HTTP protocol for patch exchange
- File-based sharing initially (Ocean + Stachu exchange .patch files)

### 2.3 Patch Validation
- Implement `checkEdit` function
- Type checking for new functions
- Dependency validation
- Test execution for patch validation

## Phase 3: Production Ready (Months 3-4)

### 3.1 Conflict Resolution
- Implement Conflict types
- Manual conflict resolution UI in CLI
- Merge strategies

### 3.2 VS Code Integration
- Status bar showing current session/patch
- SCM view with pending changes
- Commands palette integration

### 3.3 Full Sync Protocol
- Central server support
- Efficient delta sync
- Offline/online transitions

## Concrete Next Steps (This Week)

### For Tomorrow's Coworker Meeting:

1. **Demo Plan**: Show the complete architecture + gap analysis
2. **Implementation Proposal**: Phase 1 plan focusing on basics
3. **Key Decision**: Should we implement Op execution in F# or Darklang?

### For Sunday's Advisor Meeting:

1. **Progress Demo**: Show working patch creation + basic sync
2. **Technical Deep-dive**: Content-addressable architecture benefits  
3. **Roadmap**: Clear phases toward production system

## Critical Questions

1. **Where should Op execution live?** LibPackageManager vs separate LibSCM?
2. **Content hashing strategy?** SHA256 of serialized PT vs custom scheme?
3. **Session storage?** Database vs local files vs hybrid?
4. **Sync protocol?** HTTP+JSON vs custom binary vs git-style?

## Success Metrics

**Week 1**: Ocean can create a function, generate a patch, share with Stachu
**Week 2**: Stachu can apply Ocean's patch, modify it, share back
**Month 1**: Both can work on different features simultaneously without conflicts
**Month 2**: VS Code integration makes it feel like "normal" development

---

**Bottom Line**: The foundation is solid. Now we need to build the implementation bridge between the beautiful architecture and actual developer workflows.