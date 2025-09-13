# Presentation Outline: Darklang Developer Collaboration

## Slide 1: Title
**Enabling Developer Collaboration in Darklang**
- Adding the foundation we need
- Stachu Korick
- September 2025

## Slide 2: The Problem
**We can't share code**
- Darklang has evolved: Browser → CLI/Files
- Lost: Real-time collaboration
- Missing: Any way for two devs to share changes
- Result: **Analysis paralysis**

## Slide 3: What We Have Today
**Strong Foundation**
- ✅ Language & type system
- ✅ Execution engine  
- ✅ Package manager
- ✅ CLI (self-hosted in Darklang!)
- ✅ VS Code extension
- ❌ **No collaboration mechanism**

## Slide 4: Research Insights
**What Made Classic Good**
- Instant deployment (50ms)
- Trace-driven development
- No infrastructure management
- **But**: Locked to browser, no standard tools

**New Approach**: Keep the magic, use standard tools

## Slide 5: Core Concepts
**Simple but Powerful**

```
Ops → Patches → Sessions → Sync
```

- **Op**: AddFunction, UpdateType (atomic changes)
- **Patch**: Collection of ops (logical change)
- **Session**: Work context (persistent state)
- **Sync**: Share patches between developers

## Slide 6: Architecture Diagram
```
┌─────────────┐     ┌─────────────┐
│  Stachu CLI │────▶│   Server    │◀────│  Ocean CLI  │
│  + SQLite   │     │  + Patches  │     │  + SQLite   │
└─────────────┘     └─────────────┘     └─────────────┘
       ↓                                        ↓
┌─────────────┐                        ┌─────────────┐
│   VS Code   │                        │   VS Code   │
└─────────────┘                        └─────────────┘
```

## Slide 7: Developer Flow
**Adding a Function**

1. `dark session new --intent "Add List.filterMap"`
2. `dark add function filterMap` → Opens editor
3. Write code with LSP support
4. `dark eval "List.filterMap [1;2;3] ..."` → Test
5. `dark patch ready` → Validate
6. `dark sync push` → Share

## Slide 8: Live Demo
**See It Working**
- Two developers
- One adds function
- Other receives and uses it
- Simple, fast, effective

[Switch to terminal for demo]

## Slide 9: Conflict Handling
**Smart but Simple**
- Detect conflicts early
- Offer clear options
- Preserve both versions
- Manual resolution when needed

Example: Two devs edit same function
- System detects conflict
- Shows diff
- Developer chooses resolution

## Slide 10: Implementation Plan
**Phased Approach**

**Phase 1** (This week):
- Core types ✅
- Basic CLI commands
- Simple sync

**Phase 2** (Next week):
- Validation
- Conflict detection
- Session persistence

**Phase 3** (Week 3-4):
- VS Code integration
- Polish & testing
- Documentation

## Slide 11: Technical Decisions
**Pragmatic Choices**

- **SQLite locally**: Simple, reliable
- **HTTP sync**: No WebSocket complexity (yet)
- **Patches, not branches**: Simpler mental model
- **Online-first**: Assume connection (for now)
- **Manual sync default**: User controls when

## Slide 12: Future Vision
**This Enables**
- AI pair programming (parallel sessions)
- Instant deployment (per-patch)
- Community packages
- Enterprise collaboration
- Educational platform

## Slide 13: Why This Matters
**Unblocking Development**
- Solves immediate need (we can share code!)
- Foundation for everything else
- Proves Darklang can be collaborative
- Gets us unstuck and moving

## Slide 14: Success Metrics
**Definition of Done**
- ✓ Two devs can share functions
- ✓ Patches apply cleanly
- ✓ Conflicts detected
- ✓ State persists
- ✓ Basic but complete

## Slide 15: Open Questions
**Need Input On**
1. Sync: Automatic or manual?
2. Conflicts: How much automation?
3. Priority: CLI or VS Code first?
4. Timeline: Is 2-4 weeks realistic?

## Slide 16: Next Steps
**Immediate Actions**
- Today: Finish core implementation
- Tomorrow: Test with coworker
- This week: Build Phase 1
- Next week: Polish and document
- Two weeks: Community preview

## Slide 17: Call to Action
**What I Need**
- Feedback on approach
- Help with implementation
- Testing and bug reports
- Patience while we build

## Slide 18: Thank You
**Questions?**

Contact: stachu@darklang.com
Repo: github.com/darklang/dark
Discord: discord.gg/darklang

---

## Backup Slides

### B1: Why Not Git?
- Git is file-based, we're function-based
- Git branches are heavyweight
- Merge conflicts are painful
- We can do better with package-native version control

### B2: CRDT Investigation
- Researched CRDTs for conflict-free sharing
- Adds complexity for v1
- Can add later if needed
- Patches are simpler starting point

### B3: Performance Considerations
- Patches are small (usually 1 function)
- SQLite is fast enough for local state
- HTTP sync is simple and debuggable
- Can optimize later

### B4: Security Model
- Authentication: Simple for now (hardcoded users)
- Authorization: All patches visible (open source)
- Encryption: HTTPS for transport
- Future: Proper auth, private packages

### B5: Error Recovery
- Local SQLite backup
- Patches are immutable once pushed
- Can always rebuild from patch history
- "Quarantine" for bad patches

---

## Speaker Notes

### Key Messages
1. We're stuck without collaboration
2. This is the minimal solution
3. It's designed to extend
4. We can build this quickly

### Tone
- Confident but realistic
- Excited about possibilities
- Honest about limitations
- Focused on shipping

### Anticipate Questions
- "Why build vs buy?" → Nothing fits our model
- "What about scale?" → Start small, design for growth
- "Security concerns?" → Open source first, security later
- "Timeline risks?" → Scope creep, complexity

### Demo Backup Plan
- Screenshots if live fails
- Recorded terminal session
- Conceptual walkthrough
- Code examples ready