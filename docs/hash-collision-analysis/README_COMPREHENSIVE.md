# Comprehensive Hash-Based Identity Architecture Analysis

This directory contains both an **initial tactical analysis** of hash collision issues and a **comprehensive architectural analysis** of what's required for true reference-by-hash identity in Darklang's package system.

## Two Analyses

### 1. Initial Tactical Analysis (README.md)
Documents the immediate hash collision problem discovered during Guid→Hash migration:
- Multiple types with identical structure hash to same value
- Breaks reverse lookups and pretty printing
- Proposed 4 tactical solutions
- See [README.md](./README.md) for details

**Status**: This was addressing a specific failing test and immediate issue.

### 2. Comprehensive Architectural Analysis (This Document)
Deep analysis of what true reference-by-hash requires across ALL layers:
- Layer-by-layer examination of entire system
- Three major architectural blockers identified
- Coordinated cross-layer changes required
- Complete 22-week migration path

**Status**: This is the full architectural thinking, not just fixing the current branch.

---

## Comprehensive Analysis Documents

### Executive Summary
**[SYNTHESIS_REF_BY_HASH.md](./SYNTHESIS_REF_BY_HASH.md)** - **START HERE**
- Executive summary of all findings
- Three major blockers and their solutions
- Cross-layer dependencies
- Complete migration path (22 weeks, 9 phases)
- Key design decisions
- Risk mitigation strategies
- Success metrics

### Layer-by-Layer Analysis

Each analysis follows the same structure:
- **Current State**: What exists now
- **Problems**: What's wrong with current approach
- **Requirements**: What ref-by-hash needs
- **Proposed Changes**: Specific architectural changes
- **Code Impacts**: What code would need to change
- **Open Questions**: Design decisions still needed

1. **[LAYER_1_PROGRAMTYPES_ANALYSIS.md](./LAYER_1_PROGRAMTYPES_ANALYSIS.md)** - Type system and expressions
   - Hash-based IDs but location-based assignment
   - Mutual recursion unsolved, ESelf limitations
   - Need: Hash + optional location, SCC for mutual recursion

2. **[LAYER_2_RUNTIMETYPES_ANALYSIS.md](./LAYER_2_RUNTIMETYPES_ANALYSIS.md)** - Execution types
   - Lossy format, no location info
   - Source vs runtime type confusion
   - Need: Metadata via ExecutionState, source maps

3. **[LAYER_3_HASHING_ANALYSIS.md](./LAYER_3_HASHING_ANALYSIS.md)** - Content hashing
   - Basic hashing with normalization
   - Mutual recursion impossible, no SCC
   - Need: SCC-based hashing, determinism validation

4. **[LAYER_4_PARSING_ANALYSIS.md](./LAYER_4_PARSING_ANALYSIS.md)** - WrittenTypes → ProgramTypes
   - Single-phase with location-based IDs
   - Chicken-and-egg problem
   - Need: Multi-phase parsing, SCC detection, ID rewriting

5. **[LAYER_5_PACKAGEMANAGER_ANALYSIS.md](./LAYER_5_PACKAGEMANAGER_ANALYSIS.md)** - Package lookup and storage
   - Basic find/get operations
   - Type signature mismatch, single location assumption
   - Need: Multiple locations per hash, bidirectional mapping

6. **[LAYER_6_SQL_SCHEMA_ANALYSIS.md](./LAYER_6_SQL_SCHEMA_ANALYSIS.md)** - Database schema
   - Basic tables with binary blobs
   - No foreign keys, no metadata table
   - Need: Proper constraints, metadata table, indexes

7. **[LAYER_7_BUILTINS_ANALYSIS.md](./LAYER_7_BUILTINS_ANALYSIS.md)** - F# builtin integration
   - PackageIDs.fs with hardcoded UUIDs
   - F# compile-time constraint problem
   - Need: Concept ID layer for stable references

---

## Three Major Architectural Blockers

### Blocker 1: Mutual Recursion Problem
**Issue**: Types that reference each other can't be hashed individually
```dark
type A = | Tag of B
type B = | Other of A
```

**Solution**: **Strongly Connected Component (SCC) Hashing**
- Detect cycles using Tarjan's algorithm
- Hash mutually recursive groups together
- Use placeholders for internal refs
- All members get same hash

**Impact**: Layers 3 (hashing), 4 (parsing), 1 (types)

### Blocker 2: Builtin References Problem
**Issue**: F# code (compile-time) needs to reference Darklang types (parse-time)

**Solution**: **Concept ID Layer**
```fsharp
// Stable IDs that never change
type ConceptID = "darklang-stdlib-option-v1"

// Runtime mapping: concept → current content hash
module ConceptRegistry =
  let getCurrentHash : ConceptID -> Hash
```

**Benefits**:
- F# references stable concept IDs
- Implementation can evolve (new hash)
- F# code doesn't need to change

**Impact**: Layer 7 (builtins), 1 (types), 5 (PM)

### Blocker 3: Hash-Location Mapping Problem
**Issue**:
- One hash → many locations (same content, different names)
- One location → many hashes over time (evolution)

**Solution**: **Bidirectional Many-to-Many Mapping**

**SQL Schema**:
```sql
-- Multiple locations per hash
CREATE TABLE locations (
  location_id PRIMARY KEY,
  item_hash BLOB,  -- Many locations can have same hash
  owner TEXT, modules TEXT, name TEXT,
  deprecated_at TEXT  -- Track history
);
```

**APIs**:
```fsharp
type PackageManager = {
  // Location → Hash
  findType : Location -> Option<Hash>

  // Hash → Content
  getType : Hash -> Option<PackageType>

  // Hash → All Locations (NEW)
  getTypeLocations : Hash -> List<Location>

  // Hash → Preferred Location (NEW)
  getTypeLocation : BranchIDOpt * Hash -> Option<Location>
}
```

**Impact**: Layers 5 (PM), 6 (SQL), 1 (types)

---

## Migration Path Summary

| Phase | Timeline | Focus | Deliverable |
|-------|----------|-------|-------------|
| 0. Preparation | Weeks 1-2 | Foundation | Test suite, feature flags, metrics |
| 1. Hashing Layer | Weeks 3-4 | SCC hashing | SCC detection, group hashing |
| 2. ProgramTypes | Weeks 5-6 | Location fields | Hash + location in types |
| 3. SQL Schema | Weeks 7-8 | Multi-location | Indexes, metadata table |
| 4. PackageManager | Weeks 9-10 | Bidirectional API | getLocations methods |
| 5. Parsing | Weeks 11-13 | Multi-phase | 3-phase parsing |
| 6. Concept IDs | Weeks 14-15 | Stable refs | Concept registry |
| 7. RuntimeTypes | Weeks 16-17 | Metadata access | Source maps |
| 8. Integration | Weeks 18-20 | E2E testing | Full stack validated |
| 9. Cleanup | Weeks 21-22 | Remove old | Legacy code removed |

**Total**: 22 weeks (5-6 months)

**Each phase** is independently testable and deployable with feature flags.

---

## Key Design Decisions

### 1. SCC Granularity
**Decision**: All mutually recursive items get same hash

**Rationale**: Simpler than per-item hashes with cycle markers. Makes it clear they're a unit.

### 2. Builtin References
**Decision**: Use concept ID layer (not auto-generation)

**Rationale**: More stable, easier to reason about, less build complexity.

### 3. Metadata Storage
**Decision**: Separate metadata table (not embedded in PT blobs)

**Rationale**: Allows updating metadata without reserializing, enables search.

### 4. Parsing Strategy
**Decision**: 3-phase parsing (parse → hash → rewrite)

**Rationale**: Clean separation, handles SCCs correctly, enables content hashing.

### 5. Name in Hash
**Decision**: Include names in hashes

**Rationale**: `type Foo = Int64` and `type Bar = Int64` should be different.

### 6. Error Handling
**Decision**: Strict validation, fail early

**Rationale**: Invalid state shouldn't propagate to runtime.

### 7. Location Preference
**Decision**: Newest (ORDER BY created_at DESC)

**Rationale**: Simplest for now. Can add explicit preference later.

---

## Success Metrics

### Correctness
✅ Parse same file 1000x → identical hashes (no stabilization)
✅ Mutual recursion works correctly
✅ All builtins resolve to correct types
✅ No hash collisions (SHA256 collision = bug)

### Performance
✅ Parse time < 2x current (with caching)
✅ Hash computation < 10ms per item
✅ Query latency < 100ms p95
✅ Startup time < 5s

### Quality
✅ Error messages include names (not hashes)
✅ Source maps enable debugging
✅ All tests pass

### Adoption
✅ All stdlib uses content hashing
✅ All builtins use concept IDs
✅ 0 uses of location-based IDs
✅ Migration guide exists

---

## Reading Guides

### For Executives/PMs
1. SYNTHESIS_REF_BY_HASH.md - Focus on timeline, risks, success metrics
2. This README - Overview of approach

### For Architects/Tech Leads
1. SYNTHESIS_REF_BY_HASH.md - Full picture
2. LAYER_3_HASHING_ANALYSIS.md - SCC algorithm details
3. LAYER_7_BUILTINS_ANALYSIS.md - Concept ID design
4. LAYER_4_PARSING_ANALYSIS.md - Multi-phase parsing

### For Backend Developers
1. SYNTHESIS_REF_BY_HASH.md - Overview
2. Your specific layer(s) - Detailed requirements
3. SYNTHESIS migration path - Your phase details

### For Database/Infra Engineers
1. LAYER_6_SQL_SCHEMA_ANALYSIS.md - Schema design
2. LAYER_5_PACKAGEMANAGER_ANALYSIS.md - API design
3. SYNTHESIS Phase 3-4 - Your deliverables

### For Those New to the Codebase
1. This README - High-level overview
2. SYNTHESIS Executive Summary - Problem and solution
3. Pick one layer to understand deeply
4. Expand from there

---

## Key Architectural Insights

1. **Hash is for identity, location is for display**
   - Hash identifies WHAT (content)
   - Location identifies WHERE (name)
   - Store both, use each for its purpose

2. **Mutual recursion requires SCC-based hashing**
   - Cannot hash items individually if they reference each other
   - Must detect cycles and hash groups
   - Fundamental to content-based addressing

3. **Metadata should be separated from content**
   - Fixing typos in descriptions shouldn't change hashes
   - Separate table/storage for metadata
   - Bind metadata to hash, not location

4. **F# cannot use pure content hashes**
   - F# compiled before Darklang parsed
   - Need stable layer above content hashes
   - Concept IDs decouple F# from content evolution

5. **Bidirectional mapping is essential**
   - One hash → many locations (aliases, moves)
   - One location → many hashes (evolution)
   - Need efficient queries both ways

6. **Multi-phase parsing is mandatory**
   - Phase 1: Parse with placeholders
   - Phase 2: Compute content hashes (with SCC)
   - Phase 3: Rewrite all references
   - Can't compute hash without content, can't reference without hash

7. **Each layer has different needs**
   - PT: Rich info for tooling (location, metadata, etc.)
   - RT: Lean for execution (just hash)
   - SQL: Optimized for queries (indexes, normalization)
   - Parsing: Multi-phase for correctness
   - Builtins: Stable references (concepts)

---

## Relationship to Tactical Analysis

The **initial tactical analysis** (README.md) identified hash collisions as an immediate problem and proposed 4 solutions:

1. PackageRef with optional location
2. Include location in hash computation
3. Two hashes (identity + content)
4. Debug metadata system

**Comprehensive analysis findings**:
- Hash collisions are actually correct behavior (same content = same hash)
- The real issues are:
  - **Mutual recursion** (Blocker 1) - Can't hash individually
  - **Builtin references** (Blocker 2) - F# needs stable IDs
  - **Multi-location mapping** (Blocker 3) - One hash, many names
- Solutions 1 & 4 from tactical analysis are partially correct but incomplete
- Need coordinated changes across all layers
- 22-week migration path vs quick tactical fix

**Recommendation**: The comprehensive architectural approach is the right path forward. The tactical fixes address symptoms; this addresses root causes.

---

## Risk Mitigation

### Technical Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| SCC detection bugs | Incorrect hashes, breakage | Comprehensive tests, validation, fuzzing |
| Performance degradation | Slow parsing, poor UX | Benchmarking, caching, optimization |
| Data migration failures | Data loss | Test on copies, backups, rollback plan |
| Concept ID overhead | Manual work | Tooling, validation, conventions |
| Breaking changes | Existing code breaks | Test suite, feature flags, gradual migration |

### Process Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| Scope creep | Never ship | Strict phasing, MVP mindset |
| Team coordination | Conflicts | Clear ownership, sync meetings |
| Hidden dependencies | Surprises | This analysis, code review |
| Timeline slip | Delayed benefits | Buffer, monitoring, adjustment |

---

## Open Questions (High Priority)

1. **SCC Hash Stability**: How to handle algorithm changes? Version hashes?
2. **Concept ID Namespace**: Who creates concept IDs? How to prevent collisions?
3. **Multi-Location Preference**: Beyond "newest", what factors matter?
4. **Performance**: Is 3-phase parsing fast enough for interactive editing?
5. **Error Messages**: How detailed when hash not found?

**Decision needed**: Before starting respective phases

---

## Next Steps

1. ✅ Review comprehensive analysis (you are here)
2. Schedule architecture review meeting
3. Discuss three major blockers and proposed solutions
4. Validate timeline and resource allocation
5. Get stakeholder buy-in
6. Begin Phase 0 (preparation):
   - Set up test suite
   - Create feature flag system
   - Establish metrics
7. Kick off Phase 1 (hashing layer)

---

## Contributing to This Analysis

When adding or updating analysis:

**Structure**: Each layer follows same template:
- Current State → Problems → Requirements → Proposed Changes → Code Impacts → Open Questions

**Cross-references**: Link to other layers where dependencies exist

**Examples**: Use code examples from actual codebase

**Questions**: Document open questions for later resolution

**Updates**: Keep SYNTHESIS in sync with layer changes

---

## Questions and Feedback

This is **deep architectural work**. The goal is to **get it right, not fast**.

Key questions to ask:
- What did I miss?
- Where are hidden assumptions?
- What's the blast radius if we get this wrong?
- How can we validate incrementally?
- What's the rollback plan?
- Are 22 weeks realistic?

Concerns, alternative proposals, and critical questions are **strongly encouraged**.

---

## Document History

- **Nov 20, 2024**: Initial tactical analysis of hash collision issue
- **Nov 21, 2024**: PackageIDs analysis and implementation plan
- **Nov 24, 2024**: Comprehensive 7-layer architectural analysis

## Authors

Analysis conducted by Claude (Anthropic) under direction of Darklang team.

Reviewed by: [TBD]

Approved by: [TBD]
