# Darklang Developer Experience - TODO List

This is a living document. Update it as you work, add new discoveries, mark things complete, reorganize as needed.

## Phase 1: Research & Understanding (START HERE)

### Read and Understand Resources
- [ ] Read the blog post on blog.darklang.com about Darklang Classic
  https://blog.darklang.com/an-overdue-status-update/
- [ ] Read the most recent post on stachu.net about the vision
- [ ] Browse wip.darklang.com to understand the new direction
- [ ] Explore ~/code/darklang.com source to see how the website works
- [ ] Look through ~/vaults/Darklang Dev for relevant notes (note what you find useful)
- [ ] Check ~/vaults/Darklang Internal if needed

### Study Core Codebase
- [ ] Read ProgramTypes.fs - understand current type system
- [ ] Read RuntimeTypes.fs - understand execution model
- [ ] Read Interpreter.fs - understand how code runs
- [ ] Read CLI source (Cli.fs) - understand current CLI capabilities
- [ ] Read VS Code extension source - understand current integration
- [ ] Look at DB schema and migrations - understand data model
- [ ] Check out Ocean's hashing PR if relevant

### Initial Analysis
- [ ] List what Darklang Classic had that was good (immediate feedback, etc.)
- [ ] List what's currently missing for actual software development
- [ ] Think about: What's the minimal viable thing to share code between two developers?

## Phase 2: Design Exploration

### Core Concepts
- [ ] Sketch out what Ops might look like (not Classic's position-based ones)
- [ ] Think about Patches - how do they work? what do they contain?
- [ ] Design Sessions concept - what are they? where do they live?
- [ ] Figure out Instances & Sync - how do different installations communicate?
- [ ] Consider Traces - worth bringing back from Classic?

### Package Management Questions
- [ ] How to separate names from definitions?
- [ ] What's the difference between Values and Globals?
- [ ] How does package search work with branches?
- [ ] Should we use SQL or in-memory projections?

### Developer Flows to Document
- [ ] Basic flow: edit function, save, share with coworker
- [ ] How does someone write a new function from scratch?
- [ ] How does AI work on multiple features in parallel?
- [ ] What happens when patches conflict?
- [ ] How do you review someone else's changes?

## Phase 3: Artifact Creation

### Tiny Drafts First
- [ ] Draft minimal Op types in a sketch file
- [ ] Write one simple developer flow end-to-end
- [ ] Create basic sync protocol outline
- [ ] Sketch ProgramTypes.fs additions (just types, no implementation)

### Documentation/Specs
- [ ] Document at least 3 complete developer flows
- [ ] Write spec for patch validation
- [ ] Create conflict resolution approach
- [ ] List open questions for meetings

### Code Changes (if time)
- [ ] ProgramTypes.fs - add basic types?
- [ ] CLI - new commands?
- [ ] VS Code - specs/todos?
- [ ] Database migrations sketch?

### Website Work
- [ ] Outline dev.darklang.com structure
- [ ] List what content to pull from Darklang Dev vault
- [ ] Sketch /flows page
- [ ] Design /glossary entries

## Phase 4: Meeting Preparation

### For Tomorrow (Coworker)
- [ ] Summary of research findings
- [ ] List of proposed approaches
- [ ] Key open questions
- [ ] One concrete thing that works (even tiny)

### For Sunday (Advisor)
- [ ] Presentation outline
- [ ] Demo plan (MCP servers? basic patch creation?)
- [ ] Clear next steps
- [ ] Blog post draft (even rough)

## Open Questions to Track

Add questions here as you discover them:
- What's a Session really?
- How do patches get transferred between machines?
- Should patches be Exprs that produce Ops?
- Can we work on multiple branches at once?
- How fast does patch validation need to be?
- What about local-only patches that never sync?
- How do environment variables work per-session?
- Do sessions host LSP/MCP servers?
- What's the simplest thing that would let Ocean and I share code?

## Ideas to Explore Later

Park ideas here that aren't urgent:
- Port cronchecker and qw to Darklang
- Use stachu.net or darklang.com as test project
- Listener.exe concept
- SSH demo flow
- reMarkable folder/docs integration
- Voice integration approaches
- FRP abstractions for the platform

## Progress Notes

Add notes here as you work:
- 2024-09-12 16:00 - Started Phase 1 research after git reset disaster
- 2024-09-12 16:10 - **KEY DISCOVERY**: SCM infrastructure already exists!
  - ProgramTypes.fs has complete Op taxonomy (AddFunctionContent, UpdateNamePointer, etc.)
  - PackageLocation.T type separates content from location  
  - Patch, Session, Conflict types all implemented
  - Database migration 20240912_000000_add_scm_tables.sql exists
  - Design documents scm_design_draft.md and scm_artifacts_summary.md exist
- 2024-09-12 16:15 - Read Darklang Classic blog: good immediate feedback, but restrictive editor
- 2024-09-12 16:20 - Read wip.darklang.com: "Next-gen Package Manager" with immutable, content-addressable features
- 2024-09-12 16:25 - CLI analysis: Basic execution via executeCliCommand function, but no SCM commands visible yet
- 2024-09-12 16:30 - **ANALYSIS COMPLETE**: Found what's missing for software development:

## What EXISTS (Infrastructure Ready):
- ✅ Complete Op taxonomy in ProgramTypes.fs (AddFunctionContent, UpdateNamePointer, CreatePatch, etc.)
- ✅ Database schema with content-addressable architecture
- ✅ PackageLocation.T type separating content from names
- ✅ Patch, Session, Conflict types fully defined
- ✅ Basic CLI with package navigation (nav, ls, view, tree)
- ✅ Package manager integration
- ✅ Design documents and architectural specs

## What's MISSING (Gap Analysis):
- ❌ **No SCM CLI commands**: No patch, session, or sync commands in CLI registry
- ❌ **No Op implementation**: Types exist but no actual functions to execute Ops
- ❌ **No patch validation**: checkEdit function not implemented
- ❌ **No sync protocol**: Instance-to-instance communication missing
- ❌ **No content hashing**: Functions to generate content hashes missing
- ❌ **No LibPackageManager SCM integration**: No functions to apply Ops to database
- ❌ **No session management**: No way to create/switch/manage sessions
- ❌ **No VS Code SCM integration**: Status bar, pending changes, etc.

## THE CORE GAP: 
The architecture exists but there's **NO IMPLEMENTATION** of the SCM operations. 
The simplest thing for Ocean and you to share code would be:
1. Implement basic Op execution (AddFunctionContent + CreateName)
2. Add CLI commands: `dark patch new`, `dark fn create`, `dark sync push/pull`
3. Basic content hashing for functions
4. Simple session management

- 2024-09-12 17:00 - **PHASE 1-3 COMPLETE**: Created comprehensive artifacts:

## ARTIFACTS CREATED TODAY:
✅ **implementation_plan.md** - 3-phase roadmap with concrete next steps
✅ **LibSCM_draft.fs** - Complete F# implementation blueprint (200+ lines)
✅ **cli_scm_commands_draft.dark** - CLI SCM commands spec (300+ lines)  
✅ **meeting_prep_coworker.md** - Tomorrow's meeting preparation with demo plan
✅ **meeting_prep_advisor.md** - Sunday's advisor meeting with strategic deep-dive

## BREAKTHROUGH INSIGHT:
The architecture is COMPLETE. All types, database schema, and design docs exist.
The gap is pure IMPLEMENTATION - no missing architecture, just need to code it.

**Most Important**: We moved from analysis paralysis to concrete, implementable artifacts with clear next steps.

---

Remember:
- Start with boring patches (just "add this function")
- The main goal is sharing code between two developers
- Don't get stuck in analysis paralysis - make tiny progress
- It's OK to edit code directly even if we don't keep it
- Update this list as you go!