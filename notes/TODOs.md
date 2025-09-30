# Development TODOs

### Research
- [ ] Read blog post about Darklang Classic developer experience
- [ ] Read latest vision posts on stachu.net  
- [ ] Study wip.darklang.com content and direction
- [ ] Review dev vault notes for relevant insights
- [ ] Analyze key codebase files (ProgramTypes.fs, RuntimeTypes.fs, CLI implementation)
- [ ] Create comprehensive research summary

### Core Concepts  
- [ ] Design minimal type definitions for Ops, Patches, Sessions, Sync
- [ ] Create complete "add function and share it" workflow
- [ ] Define validation requirements for patches
- [ ] Plan conflict resolution strategies
- [ ] Design SQLite database schema for local state

### Developer Flows
- [ ] Document detailed step-by-step user flows
- [ ] Design CLI and VS Code integration points
- [ ] Plan error handling and edge cases
- [ ] Consider AI-assisted development scenarios
- [ ] Create UI mockups and interface designs

### Implementation Planning
- [ ] Sketch F# type definitions and module structure
- [ ] Plan database schema and sync protocol
- [ ] Create phased development roadmap
- [ ] Identify technical risks and mitigations
- [ ] Define success criteria and testing approach
- [ ] Create `backend/src/LibPackageManager/DevCollab.fs` with core types
- [ ] Add patch/session/sync commands to CLI  
- [ ] Set up SQLite database with initial schema
- [ ] Create comprehensive demo scenario and walkthrough
- [ ] Complete all meeting preparation materials
- [ ] Demo basic flow: create patch, view patch, manual apply
- [ ] Gather feedback on architecture and approach
- [ ] Refine implementation plan based on discussion
- [ ] Divide work between developers
- [ ] Implement HTTP sync protocol
- [ ] Add patch validation logic
- [ ] Build conflict detection
- [ ] Create working demo for advisor presentation
- [ ] Complete CLI patch management system
- [ ] Build simple sync server
- [ ] Add session persistence
- [ ] Test with two developers
- [ ] VS Code extension integration  
- [ ] Advanced conflict resolution
- [ ] Performance optimization
- [ ] Documentation and testing

### 1. **Enhanced Package Management with Hash-Based References**
- [ ] Separate names from definitions in database schema
- [ ] Implement hash-based package references (builds on Ocean's PR work)
- [ ] Add branch tracking column to package items
- [ ] Create PackageName table separate from package content
- [ ] Enable multiple versions/branches to coexist

### 2. **Virtual File System Provider for VS Code**
- [ ] Implement VS Code virtual workspace integration
- [ ] Create custom document views for different package item types
- [ ] Enable editing of "virtual" .dark files that map to package items
- [ ] Support file watching and real-time updates
- [ ] Add URL schema support (`dark://patch/ID/function`)

### 3. **Session Transfer and Persistence**
- [ ] Implement session serialization to SQLite
- [ ] Enable session transfer between machines/instances
- [ ] Add session context preservation (open files, cursor positions)
- [ ] Create "attach to session" workflow for multiple editors
- [ ] Support environment variables per session

### 4. **Advanced Conflict Resolution**
- [ ] Model OpConflict and PatchOpConflict types
- [ ] Implement conflict validation during patch creation
- [ ] Add preview/testing capabilities for patches before merge
- [ ] Create conflict resolution strategies for different scenario types
- [ ] Enable manual conflict resolution with diff tools

### 5. **AI Agent Integration**
- [ ] Design multi-session AI workflow capabilities
- [ ] Create AI-friendly patch creation APIs
- [ ] Implement voice integration for CLI commands
- [ ] Add AI context preservation across sessions
- [ ] Design collaborative human-AI development flows

### 6. **Testing Integration**
- [ ] Add per-patch testing capabilities
- [ ] Implement test viewing and running in VS Code
- [ ] Create test result visualization in conflict resolution
- [ ] Enable testing of patches before merge
- [ ] Add regression testing for patch validation

### 7. **Enhanced Sync Protocol**
- [ ] Implement direct peer-to-peer patch sharing
- [ ] Add selective sync rules (local-only patches)
- [ ] Create offline-ready development mode
- [ ] Implement patch dependencies and merge ordering
- [ ] Add sync conflict detection and resolution

### 8. **Developer Portal (dev.darklang.com)**
- [ ] Create comprehensive flow documentation
- [ ] Build interactive glossary of terms
- [ ] Implement issue tracking (/bugs, /features, /facets)
- [ ] Add user experience documentation with examples
- [ ] Create contribution guidelines and onboarding

### 9. **Enhanced VS Code Integration**
- [ ] Add WebView panels for custom package visualization
- [ ] Implement SCM provider with branch visualization
- [ ] Create patch review interface in editor
- [ ] Add real-time collaboration indicators
- [ ] Implement MCP client integration for agents

### 10. **Values vs Globals Distinction**
- [ ] Model different sync mechanisms for different data types
- [ ] Implement "Globals" that have refactor expressions applied
- [ ] Create migration system for value transformations
- [ ] Add validation for value dependencies
- [ ] Design versioning strategy for different data types

### 11. **Tracing System Integration**
- [ ] Revive tracing capabilities for session context
- [ ] Add real-time trace viewing in editors
- [ ] Implement trace analysis for patch validation
- [ ] Create trace-based debugging workflows
- [ ] Add performance profiling per patch

### 12. **Package Search and Discovery**
- [ ] Implement fast keyboard-driven package exploration
- [ ] Add dependency tracking both directions
- [ ] Create package dependency visualization
- [ ] Implement semantic search across package items
- [ ] Add package recommendation system

### 13. **Multi-View Support**
- [ ] Enable viewing AST, pretty-printed, and runtime forms
- [ ] Add custom syntax highlighting per package item
- [ ] Implement custom pretty-printing rules
- [ ] Create contextual code navigation
- [ ] Add "focus modes" for different development tasks

### 14. **Advanced Editor Features**
- [ ] Implement CodeLens for patch information
- [ ] Add hover information for package items
- [ ] Create jump-to-definition across packages
- [ ] Implement find-all-references functionality
- [ ] Add refactoring tools for package reorganization

### 15. **Website Integration**
- [ ] Port darklang.com to Darklang itself as demo
- [ ] Create matter.darklang.com for live package browsing
- [ ] Add patch comparison and review interfaces
- [ ] Implement public package sharing
- [ ] Create community contribution workflows

## 💡 Key Insights from Brain Dump

### Design Principles to Maintain:
- **Server-First**: Keep logic in F#/Darklang, minimal client complexity
- **Editor Agnostic**: Standard protocols that work everywhere
- **Incremental**: Build on existing CLI/LSP foundation
- **Safe Collaboration**: Manual review; conflict detection, resolution