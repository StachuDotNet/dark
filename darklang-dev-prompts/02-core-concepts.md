# Phase 2: Core Concepts & Architecture

## Building on Research
You should have completed the research phase and understand the current state of Darklang. Now we need to think through the core concepts for developer collaboration.

## The Challenge
We need to add a foundation of Source Control + Package Management (SCM+PM) with developer experience on top. Think of it as retrofitting a foundation under an existing house.

## Core Concepts to Design

### 1. Operations (Ops)
**Problem**: Classic had position-based ops (SetHandler, CreateDB, etc.). We need package-oriented operations.

**Questions to explore**:
- What ops make sense for our package manager?
- How do we handle versioned values vs mutable globals?
- Should we be able to pull ops between patches?

**Consider**: AddType, MoveType, AddFunction, EditValue, MigrateData, Deprecate, etc.

### 2. Patches  
**Concept**: Atomic transformations containing Ops.

**Must support**:
- Validation against local/dependent data
- Dependencies and merging
- Views of changes, Intent, TODOs
- "Patch rejected" flows

**Start simple**: Begin with "boring patches" like "add this function"

### 3. Sessions
**Vision**: Like tmux for development work.

**Commands to consider**:
- `dark session new --intent="work on Stdlib.List"`
- `session continue [name]`
- `session list`

**Questions**:
- What is a Session? Where does it live?
- How are sessions transferred between machines?
- Do sessions host LSP/MCP servers?

### 4. Instances & Sync
**Architecture**:
- Each CLI install = Instance
- Central server = Instance
- Future: Browser WASM = Instance

**Sync protocol needs**:
- Copying, negotiating, merging Ops
- Offline work support
- Both manual and automatic modes

### 5. Values vs Globals
**Problem**: Some values should be versioned, others are mutable globals/secrets.

**Question**: How do they sync differently? Should we split the concept?

## Your Design Tasks

1. **Sketch minimal type definitions** for each concept
2. **Design one complete flow** end-to-end (e.g., "add a function and share it")
3. **Identify validation needs** - when do patches get rejected?
4. **Consider conflicts** - what happens when two patches clash?
5. **Think about the database** - what needs to be stored?

## Key Constraints
- Make illegal states unrepresentable
- Support both manual and automatic modes
- Design for AI development (parallel sessions)
- Keep it simple enough for two developers to use

## Before Making Big Decisions
Ask me for input on:
- Core type structure choices
- Sync protocol approach
- Database schema decisions  
- Which flows to prioritize

## Output
Create concept documents with:
- Type sketches (even rough F# types)
- One complete developer flow
- List of validation requirements
- Open questions for discussion

**Focus**: What's the minimal thing that would let me and my coworker share code?

---
*Next: After core concepts are sketched, move to 03-developer-flows.md*