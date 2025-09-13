# Darklang Developer Experience Design - Prompt Sequence

This directory contains a structured sequence of prompts to design and implement developer experience improvements for Darklang.

## How to Use This

1. **Work through prompts in order** (01 → 02 → 03 → 04 → 05)
2. **Complete each phase** before moving to the next
3. **Update TODOs.md** in the parent directory as you progress
4. **Ask for input** on crucial decisions (noted in each prompt)
5. **Create artifacts** as you go - don't just think, build things

## Prompt Sequence

### 01-research-context.md
**Goal**: Understand current state of Darklang
- Read key resources (blog posts, code)
- Assess what we have vs what we need
- Identify gaps and opportunities
- Create research summary

### 02-core-concepts.md  
**Goal**: Design architecture and key abstractions
- Define Ops, Patches, Sessions, Sync
- Sketch type definitions
- Plan validation and conflicts
- Consider database needs

### 03-developer-flows.md
**Goal**: Document user experience
- Design end-to-end developer workflows
- Plan CLI and VS Code integration
- Handle edge cases and errors
- Consider AI-assisted development

### 04-implementation-plan.md
**Goal**: Plan the actual code changes
- Specify code modifications needed
- Design database schema
- Plan sync protocol
- Create development roadmap

### 05-meeting-prep.md
**Goal**: Create meeting deliverables
- Prepare for coworker discussion (tomorrow)
- Create advisor presentation (Sunday)
- Draft demo plans
- Summarize recommendations

## Key Constraints

- **Main goal**: Enable two developers to share code
- **Time pressure**: Meetings tomorrow and Sunday
- **Start simple**: "Boring patches" like adding one function
- **Ask before big decisions**: Architecture, sync protocol, priorities
- **Make progress**: Better to have something concrete than perfect plans

## Success Criteria

By the end, you should have:
- Clear understanding of current Darklang state
- Architectural design for collaboration features
- Documented developer workflows
- Implementation roadmap
- Meeting-ready presentations and demos

## Output Artifacts

Each phase should produce concrete deliverables:
- Research summaries
- Type definitions and code sketches
- Flow documentation  
- Implementation specifications
- Presentation materials

**Remember**: This is about enabling actual software development in Darklang, not just planning. Focus on what would help two developers work together effectively.