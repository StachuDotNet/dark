# Phase 3: Developer Flows & User Experience

## Building on Concepts
You should now have core concepts sketched out (Ops, Patches, Sessions, Sync). Now we design the actual developer experience.

## The Human Question
How do people write software? They:
- Think and plan todos
- Write code, run code, test
- Ship and switch between efforts  
- Search, debug, review traces
- Communicate with team
- Handle source control
- Worry about software

**Our job**: How does Darklang tackle this? What can we offload to other systems?

## Priority Flows to Document

### Flow 1: Basic Function Addition (START HERE)
The most boring patch - adding one function.

**Steps to design**:
1. Developer writes a new function
2. Function gets saved/validated  
3. Creates a patch
4. Shares patch with coworker
5. Coworker reviews and merges
6. Function is now available to both

### Flow 2: Development Setup
- Install CLI locally
- Set up VS Code extension
- Configure LSP/MCP  
- Get "GREAT feedback" when editing
- Connect to development server

### Flow 3: Conflict Resolution
What happens when:
- Two developers edit the same function?
- A patch depends on another unreliable patch?
- Tests fail during patch validation?

### Flow 4: AI-Assisted Development
- AI working on multiple sessions
- Human jumping between AI sessions
- Voice integration possibilities
- Parallel feature development

## Environment Considerations

### CLI Experience
- Show current branch/patch: "viewing reality at patch #89ab4e"
- Commands for patches, sessions, status, diff
- Totally CLI-based development possible
- Settings for manual/auto sync

### VS Code Experience  
- Virtual file system provider
- Package tree view
- SCM integration (pending changes, status bar)
- Webview panels for patch review
- Real-time updates for traces/code changes
- Custom views per document type

### Cross-Environment
- Leave session in CLI, pick up in VS Code
- Shared state between installations
- Seamless transitions

## Your Flow Design Tasks

1. **Document the basic function flow** step-by-step
2. **Design the "patch rejected" experience** 
3. **Sketch conflict resolution UI** (both CLI and VS Code)
4. **Plan the setup experience** for new developers
5. **Consider AI integration points**

## Questions to Address
- How fast does patch validation need to be?
- What happens during network failures?
- How do you test someone else's patch?
- Can users work on multiple branches simultaneously?
- What's the emergency "undo everything" experience?

## Special Considerations

### Sunday Demo Flow
Plan something like:
- Go to matter.darklang.com
- Navigate to a value/presentation
- Show different views (pretty-printed, AST, runtime)
- Demonstrate WebView rendering

### Testing Per Patch
Can a user test a website with a specific patch applied? How?

## Output Requirements

Create flow documentation with:
- Step-by-step user actions
- System responses at each step
- Error/edge cases
- UI mockups or descriptions
- Technical requirements

**For each flow, include**:
- Context/scenario
- Prerequisites
- Success criteria
- Failure modes

## Before Implementation Decisions
Ask me about:
- Which flows are highest priority
- UI/UX preferences for conflict resolution
- Balance between CLI and VS Code features
- Testing and validation approaches

**Remember**: Focus on flows that would actually help me and my coworker share code. Keep it minimal but complete.

---
*Next: After documenting key flows, move to 04-implementation-plan.md*