# Phase 1: Research & Understanding Darklang Context

## Your Mission
You're helping design the developer experience for Darklang - a software language and platform. I'm experiencing analysis paralysis and need concrete artifacts for meetings tomorrow (coworker) and Sunday (advisor).

## The Core Problem
We have disparate pieces of a language and platform, but we need to pull them together into cohesive developer experiences. It's like lifting a house to add a foundation while trying to reconnect the plumbing.

**Primary Goal**: Enable two developers (me and my coworker) to actually write and share software in Darklang.

## What You Need to Research

### Essential Reading
1. **Blog post**: https://blog.darklang.com/an-overdue-status-update/ - about Darklang Classic
2. **Vision post**: Latest post on stachu.net about our current vision
3. **Website**: Browse wip.darklang.com to understand new direction
4. **Dev vault**: Look through ~/vaults/Darklang Dev for relevant notes

### Codebase to Study
- **ProgramTypes.fs** - our type system
- **RuntimeTypes.fs** - how things execute  
- **Interpreter.fs** - the execution engine
- **Cli.fs** - the current CLI runner
- **VS Code extension** - current integration
- **Database schema** - our data model

### What We Have Today
- **Darklang Classic (Previous)**: Online-only, edit in place, great immediate feedback
- **Current Implementation**: Language definition, execution engine, basic CLI, partial VS Code integration, database-backed package manager
- **Build System**: Automatic compilation via ./scripts/build/compile (don't manually rebuild)

### Critical Missing Pieces
- Actual developer flows (write and share software start to finish)
- Source control support
- Way to indicate current branch/patch in CLI
- Persistent CLI state across instances
- Package search that understands branches

## Your Research Tasks

1. **Read the resources** and create a summary document
2. **Study the codebase** and note:
   - Current capabilities
   - Key abstractions  
   - Where the gaps are
3. **Compare to "normal" language development** - what do we have vs what developers expect?
4. **Identify the simplest path** to sharing code between two developers

## Questions to Answer
- What made Darklang Classic's developer experience good?
- What are the biggest gaps compared to traditional development?
- What would be the minimal viable implementation to share code?
- Where are we strong vs weak compared to other languages?

## Output
Create a research summary document that covers:
- Key insights from each resource
- Current state assessment
- Biggest opportunities
- Recommended next steps

**Remember**: The goal isn't perfection, it's progress. I need concrete things to discuss in meetings. Focus on what would actually help two developers share code.

---
*Next: Once you complete this research, move to 02-core-concepts.md*