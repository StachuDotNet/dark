# Darklang Developer Experience Research Summary

## Executive Summary
Darklang is transitioning from a browser-based editor to a CLI and file-based development model. The core challenge is enabling two developers to share code effectively while maintaining Darklang's unique advantages: instant deployment, trace-driven development, and function-level package management.

## Key Insights from Research

### What Made Darklang Classic Good
1. **Deployless Development**: Instant deployment with 50ms push times
2. **Trace-Driven Development**: Live values and debugging directly in editor
3. **Integrated Workflow**: All-in-one platform for backends
4. **No Infrastructure Management**: Zero config for deployment

### Current State Assessment

#### What We Have
- **Language Core**: Strong type system (ProgramTypes.fs/RuntimeTypes.fs), execution engine
- **CLI Implementation**: 
  - Interactive REPL with command history and tab completion
  - Package navigation system (`nav`, `ls`, `tree`, `view` commands)
  - Script execution (`run`, `eval` commands) 
  - Installation/update management
  - Written in Darklang itself (self-hosting)
- **Package Manager**: 
  - Function-level versioning, immutable packages
  - Package navigation with module/type/function browsing
  - Search capabilities across packages
- **VS Code Extension**: Syntax highlighting, LSP support (in development)
- **Automatic Build System**: Background compilation via ./scripts/build/compile

#### Critical Missing Pieces  
1. **Source Control Integration**: No way to share patches/branches between developers
2. **Developer Identity**: No login/auth system for CLI or extension (hardcoded suggestions in notes: ocean/oak, stachu/uhcats)
3. **Branch/Patch Indication**: Can't tell which version you're working on in CLI
4. **Persistent Working Context**: CLI state (current location, user) doesn't persist across sessions
5. **Developer-to-Developer Sharing**: No mechanism to push/pull changes between developers

### Biggest Gaps vs Traditional Development
- **No git-like workflow** for sharing code changes
- **No local development story** - everything requires server connection
- **No clear "workspace" concept** for organizing code
- **Missing dev-to-dev collaboration** primitives

## Recommended Path Forward

### Phase 1: Minimal Viable Sharing (For Tomorrow's Meeting)
Create simplest possible system for two developers to share code:

1. **Simple Auth System**
   - Hardcoded logins (ocean/oak, stachu/uhcats) 
   - Store in local SQLite

2. **Basic Patch System**
   - Each change creates a "patch" with UUID
   - Patches stored in shared DB
   - Simple CLI commands: `dark patch create`, `dark patch list`, `dark patch apply`

3. **Persistent CLI State**
   - SQLite DB storing current branch/patch
   - Show current context in CLI prompt
   - Remember user between sessions

### Phase 2: Core Developer Flows
1. **Write Code**: Edit .dark files in VS Code with LSP support
2. **Share Code**: Push patches to shared repository
3. **Review Code**: View and apply others' patches
4. **Deploy Code**: Promote patches to production

### Phase 3: Full Integration
- Browser-based editor option
- AI-assisted development
- Visual trace debugging
- Cross-platform CLI distribution

## Concrete Artifacts Needed

### For Coworker Meeting (Tomorrow)
1. Demo script showing basic sharing workflow
2. Mockup of CLI commands for patch management
3. Simple auth flow diagram

### For Advisor Meeting (Sunday)
1. Architecture diagram of sharing system
2. Timeline for implementation phases
3. Comparison chart: Darklang vs traditional dev tools
4. Risk assessment and mitigation strategies

## Technical Implementation Notes

### Current CLI Architecture
- **Self-hosted in Darklang**: CLI is written in Darklang itself (packages/darklang/cli)
- **Interactive REPL**: Full command system with history, completion, navigation
- **Package-centric**: Navigation treats packages like a filesystem (modules as directories)
- **Stateless Sessions**: Each CLI run starts fresh, no persistent context

### Simplest Path Using Existing Infrastructure
1. **Leverage PackageManager**: Already has versioning, use for patches
2. **Extend CLI in Darklang**: Add patch/auth commands to existing Darklang CLI code
3. **SQLite for State**: Simple, local-first database for user/branch state
4. **CRDT Concepts**: Use for conflict-free sharing (mentioned in vault notes)
5. **Online-first**: Start with server connection required (simpler than offline)

### Key Design Decisions Needed
1. **Online-only vs offline-first?** (Recommend: online-only initially)
2. **Git integration vs custom SCM?** (Recommend: custom, simpler)
3. **CLI-first vs VS Code-first?** (Recommend: CLI-first, simpler)

## Next Steps
1. Build minimal auth system (2 hours)
2. Create patch create/apply commands (4 hours)
3. Add persistent state to CLI (2 hours)
4. Create demo script (1 hour)
5. Prepare meeting materials (2 hours)

## Conclusion
The path forward is clear: start with the absolute minimum to enable sharing between two developers, then iterate. Focus on "boring patches" (adding a single function) rather than complex merge scenarios. This approach gets us unstuck and provides concrete progress for meetings while laying groundwork for the full vision.