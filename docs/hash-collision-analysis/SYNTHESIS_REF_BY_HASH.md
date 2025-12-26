# Synthesis: Reference-by-Hash Architecture for Darklang

## Executive Summary

This document synthesizes the analysis of all seven layers of the Darklang package system to provide a comprehensive architectural plan for true reference-by-hash identity.

**Core Finding**: Moving to true ref-by-hash requires coordinated changes across all layers, with three major architectural challenges:

1. **Mutual Recursion**: Types/functions that reference each other cannot be hashed individually
2. **Compile-Time References**: F# builtins need stable IDs before Darklang code is parsed
3. **Multi-Location Identity**: Same content (hash) can exist at multiple locations (names)

## Three Major Blockers

### Blocker 1: Mutual Recursion Problem

**The Issue**:
```dark
type A = | Tag of B
type B = | Other of A
```

To hash A, we need the hash of B. To hash B, we need the hash of A. Circular dependency.

**Solution**: Strongly Connected Component (SCC) Hashing

**Algorithm**:
1. Build dependency graph of all type/function definitions
2. Detect SCCs using Tarjan's algorithm
3. For each SCC:
   - If single node: hash normally
   - If multiple nodes: hash as group with placeholders for internal refs
4. All members of an SCC get the same hash

**Example**:
```fsharp
// Detect SCC: {A, B}
let sccHash = hashSCC [typeA; typeB]

// Both A and B get sccHash
typeA.id = sccHash
typeB.id = sccHash

// When hashing, internal refs use placeholder:
// "type A = | Tag of SCC#B"
// "type B = | Other of SCC#A"
```

**Impact**: Requires changes to hashing layer, parser, and all layers that process types.

### Blocker 2: Builtin References Problem

**The Issue**:
F# code (compiled at build time) needs to reference Darklang types (parsed at runtime). Cannot use content hashes because content doesn't exist at F# compile time.

**Current State**: `PackageIDs.fs` has hardcoded UUIDs pretending to be hashes.

**Solution**: Concept ID Layer

**Architecture**:
```fsharp
// Stable concept IDs (never change)
type ConceptID = string  // "darklang-stdlib-option-v1"

// Runtime mapping: concept → current hash
module ConceptRegistry =
  let private _concepts : Map<ConceptID, Hash>

  let getCurrentHash (concept : ConceptID) : Hash

  let initialize (pm : PackageManager) : Ply<unit>
    // Look up each concept by location
    // Register location → hash mapping
```

**Usage**:
```fsharp
// F# builtins reference concepts
let optionConcept = "darklang-stdlib-option-v1"

// At runtime, resolve to current hash
let optionHash = ConceptRegistry.getCurrentHash optionConcept
```

**Benefits**:
- F# references stable IDs
- Option implementation can change (new hash)
- Concept ID points to new hash
- F# code doesn't need to change

**Impact**: New layer between F# and Darklang, affects builtins, parser, package manager.

### Blocker 3: Hash-Location Mapping Problem

**The Issue**:
One hash can exist at multiple locations:
- Original: `Darklang.Stdlib.List.map`
- Moved: `Darklang.List.map`
- Both point to same hash (same content)

One location can have multiple hashes over time:
- `Darklang.Stdlib.Option` v1 → hash abc
- `Darklang.Stdlib.Option` v2 → hash def

**Current State**: Schema and APIs assume one-to-one mapping.

**Solution**: Bidirectional Many-to-Many Mapping

**Schema**:
```sql
CREATE TABLE locations (
  location_id TEXT PRIMARY KEY,
  item_hash BLOB NOT NULL,  -- Many locations → one hash
  owner TEXT, modules TEXT, name TEXT,
  deprecated_at TEXT  -- Track location history
);

-- Same hash at multiple locations
SELECT owner, modules, name
FROM locations
WHERE item_hash = @hash AND deprecated_at IS NULL;

-- Location history
SELECT item_hash, deprecated_at
FROM locations
WHERE owner = @owner AND modules = @modules AND name = @name
ORDER BY created_at DESC;
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

**Impact**: Schema changes, API additions, all query code must handle multiple results.

## Cross-Layer Dependencies

### Dependency Graph

```
Layer 7 (Builtins)
    ↓ needs stable refs
Layer 5 (PackageManager)
    ↓ provides lookup
Layer 1 (ProgramTypes)
    ↓ defines structure
Layer 3 (Hashing)
    ↓ computes IDs
Layer 4 (Parsing)
    ↓ creates instances
Layer 6 (SQL Schema)
    ↓ stores data
Layer 2 (RuntimeTypes)
    ↓ executes code
```

**Critical Path**:
1. Hashing (Layer 3) must handle SCCs
2. ProgramTypes (Layer 1) must store hash + location
3. Parser (Layer 4) must detect SCCs and compute hashes
4. PackageManager (Layer 5) must handle multi-location
5. SQL Schema (Layer 6) must support bidirectional mapping
6. Builtins (Layer 7) must use concept IDs
7. RuntimeTypes (Layer 2) must access metadata for display

### Dependency Matrix

| Layer | Depends On | Required By |
|-------|-----------|-------------|
| 1. ProgramTypes | None (fundamental) | All |
| 2. RuntimeTypes | PT | Execution, builtins |
| 3. Hashing | PT, Binary serialization | Parser, PM |
| 4. Parsing | PT, Hashing, PM | PM |
| 5. PackageManager | PT, SQL | Parser, execution, builtins |
| 6. SQL Schema | PT | PM |
| 7. Builtins | RT, PM | Execution |

## Migration Path

### Phase 0: Preparation (Weeks 1-2)
**Goal**: Set foundation without breaking existing system

**Tasks**:
1. Document current state comprehensively ✅ (this document)
2. Write tests for current behavior (regression suite)
3. Set up feature flags for gradual rollout
4. Create measurement/monitoring infrastructure

**Deliverables**:
- Test suite covering current behavior
- Feature flag system
- Metrics for hash computation time, query performance

### Phase 1: Hashing Layer (Weeks 3-4)
**Goal**: Add SCC hashing capability

**Tasks**:
1. Implement SCC detection (Tarjan's algorithm)
2. Implement SCC hashing with placeholders
3. Add normalization options (semantic vs structural)
4. Comprehensive tests (determinism, cycles, etc.)

**Changes**:
- `LibSerialization/Hashing/ContentHash.fs`: Add SCC module
- Tests: SCC detection, hashing correctness

**Validation**:
- Hash same type 1000x → same result
- Mutually recursive types hash successfully
- No infinite loops

**Risks**:
- SCC detection bugs
- Non-deterministic serialization
- Performance overhead

### Phase 2: ProgramTypes Enhancement (Weeks 5-6)
**Goal**: Store location alongside hash

**Tasks**:
1. Add optional `location` field to `TCustomType`, `EFnName`, etc.
2. Add `getLocations` methods to PackageManager interface
3. Update pattern matches across codebase (mechanical, tedious)

**Changes**:
- `LibExecution/ProgramTypes.fs`: Add location fields
- All code that constructs TypeReference/Expr: Provide location
- All code that pattern matches: Handle location parameter

**Validation**:
- All existing tests pass
- Location info preserved through parse → PT → display

**Risks**:
- Missing a pattern match (runtime crash)
- Location info lost somewhere
- Memory overhead of location storage

### Phase 3: SQL Schema (Weeks 7-8)
**Goal**: Support multiple locations per hash

**Tasks**:
1. Create database migration framework
2. Add indexes to `locations` table
3. Add `metadata` table
4. Update `package_ops` for audit trail
5. Add `branches` table (if needed)

**Changes**:
- NEW: `backend/src/LibDB/Migrations.fs`
- `LibPackageManager/PT/SQL/*.fs`: Schema changes
- Migration scripts

**Validation**:
- Migrations run cleanly on test data
- Queries perform acceptably
- Data integrity maintained

**Risks**:
- Migration failures on real data
- Performance degradation
- Index overhead

### Phase 4: PackageManager API (Weeks 9-10)
**Goal**: Implement bidirectional mapping

**Tasks**:
1. Implement `getTypeLocations` methods
2. Implement `getPreferredLocation` methods
3. Implement `getMetadata` methods
4. Update search to include hashes

**Changes**:
- `LibPackageManager/PackageManager.fs`: New methods
- `LibPackageManager/PT/SQL/*.fs`: Implementations
- `LibPackageManager/PT/InMemory.fs`: Implementations

**Validation**:
- Find hash by location works
- Find locations by hash returns all
- Preferred location logic correct

**Risks**:
- Performance of multi-location queries
- Correctness of preferred location selection

### Phase 5: Parsing (Weeks 11-13)
**Goal**: Multi-phase parsing with content hashing

**Tasks**:
1. Implement Phase 1: Parse with placeholder IDs
2. Implement Phase 2: Compute content hashes (with SCC)
3. Implement Phase 3: Rewrite IDs
4. Integrate with existing parser

**Changes**:
- NEW: `LibParser/MultiPhaseParser.fs`
- NEW: `LibParser/SCCDetector.fs`
- NEW: `LibParser/IDRewriter.fs`
- `LibParser/WrittenTypesToProgramTypes.fs`: Use multi-phase

**Validation**:
- Parse same file 2x → identical hashes (no stabilization)
- Mutual recursion parses correctly
- All IDs are content hashes

**Risks**:
- Performance (3 passes)
- Complexity
- ID rewriting bugs (missing a spot)

### Phase 6: Concept IDs (Weeks 14-15)
**Goal**: Stable references for builtins

**Tasks**:
1. Design concept ID format and conventions
2. Implement `ConceptRegistry`
3. Update `PackageIDs.fs` to use concepts
4. Update builtin libraries incrementally
5. Add initialization and validation

**Changes**:
- NEW: `LibExecution/ConceptRegistry.fs`
- `LibExecution/PackageIDs.fs`: Add concept ID layer
- `BuiltinExecution/Libs/*.fs`: Use concepts (gradual)

**Validation**:
- All builtins reference valid concepts
- Concept → hash resolution works
- Performance acceptable

**Risks**:
- Bootstrapping complexity
- Performance of runtime lookup
- Migration of all builtins

### Phase 7: RuntimeTypes (Weeks 16-17)
**Goal**: Clean separation, metadata access

**Tasks**:
1. Remove `NameResolution` wrapper from RT
2. Add `MetadataProvider` to ExecutionState
3. Add source maps to Instructions
4. Update error messages to use metadata

**Changes**:
- `LibExecution/RuntimeTypes.fs`: Remove Error cases
- `LibExecution/ProgramTypesToRuntimeTypes.fs`: Strict validation
- `LibExecution/Execution.fs`: Metadata provider

**Validation**:
- PT→RT fails on unresolved names
- Error messages include readable names
- Source maps enable debugging

**Risks**:
- Breaking changes to Dval
- Performance of metadata lookups

### Phase 8: Integration and Testing (Weeks 18-20)
**Goal**: End-to-end testing and optimization

**Tasks**:
1. Integration tests for full stack
2. Performance testing and optimization
3. Load testing with large packages
4. Migration of existing data
5. Documentation

**Validation**:
- All tests pass
- Performance acceptable
- Existing packages work
- New packages use content hashing

**Risks**:
- Performance bottlenecks discovered late
- Data migration issues
- Unexpected interactions

### Phase 9: Cleanup (Weeks 21-22)
**Goal**: Remove old code

**Tasks**:
1. Remove location-based ID generation
2. Remove stabilization code
3. Remove deprecated APIs
4. Update documentation
5. Final polish

**Deliverables**:
- Clean codebase
- Updated docs
- Migration guide

## Implementation Strategy

### Development Approach

**Principle**: Each phase should be independently testable and deployable.

**Pattern**:
1. Implement new capability alongside old
2. Add feature flag to toggle between
3. Test both paths in parallel
4. Gradually migrate callers
5. Remove old path once migration complete

**Example**:
```fsharp
module PackageManager =
  let find location =
    if FeatureFlags.useContentHashing then
      findWithContentHash location
    else
      findWithLocationHash location  // Old path
```

### Testing Strategy

**Unit Tests**: Each layer independently
- Hashing: Determinism, SCC detection, normalization
- Parser: Multi-phase, ID rewriting
- PM: Multi-location, metadata
- etc.

**Integration Tests**: Cross-layer
- Parse → hash → store → retrieve
- F# builtin → concept → location → hash → content
- Error flow with source maps

**Performance Tests**:
- Hash computation time
- SCC detection overhead
- Multi-phase parsing latency
- Database query performance
- End-to-end throughput

**Correctness Tests**:
- Hash stability (same input → same hash)
- Mutual recursion correctness
- Concept resolution correctness
- Bidirectional mapping integrity

### Rollout Strategy

**Phase 1**: Internal testing
- Use on development instances
- Test suite only
- No production impact

**Phase 2**: Opt-in beta
- Feature flag enables for specific users
- Monitor closely
- Rollback capability

**Phase 3**: Gradual rollout
- Increase % of traffic
- Monitor performance and errors
- Adjust as needed

**Phase 4**: Full deployment
- All traffic uses new system
- Remove old code
- Declare victory

## Key Design Decisions

### Decision 1: Concept IDs vs Auto-Generation

**Options**:
- A: Manual concept IDs (chosen)
- B: Auto-generate PackageIDs from source

**Rationale**: Manual concept IDs provide stable layer, easier to reason about, less build complexity. Auto-generation is tempting but fragile.

### Decision 2: SCC Granularity

**Options**:
- A: All mutually recursive items get same hash (chosen)
- B: Each item gets unique hash with cycle markers

**Rationale**: Same hash is simpler, makes it clear they're a unit. Unique hashes would require complex cycle detection at usage sites.

### Decision 3: Metadata Storage

**Options**:
- A: Separate metadata table (chosen)
- B: Keep in PT blobs
- C: Hybrid (content in blobs, searchable fields extracted)

**Rationale**: Separate table allows updating metadata without reserializing, enables search, cleaner separation.

### Decision 4: Location Preference Algorithm

**Options**:
- A: Newest (ORDER BY created_at DESC) (chosen for now)
- B: Explicit preference column
- C: Owner priority (Darklang > user)

**Rationale**: Newest is simplest. Can add explicit preference later if needed.

### Decision 5: Multi-Phase Parsing

**Options**:
- A: 3-phase parsing (chosen)
- B: Single pass with fixup
- C: Iterative refinement

**Rationale**: 3-phase is clean separation of concerns. Single pass is too complex. Iterative is overkill.

### Decision 6: Name Inclusion in Hash

**Options**:
- A: Include names (chosen)
- B: Exclude names
- C: Configurable

**Rationale**: Including names means `type Foo = Int64` and `type Bar = Int64` are different, which matches user intuition.

### Decision 7: Error Handling Strategy

**Options**:
- A: Strict validation, fail early (chosen)
- B: Lenient, allow unresolved refs
- C: Warning-based

**Rationale**: Strict validation prevents invalid state from propagating. Errors should surface at parse time, not runtime.

## Performance Considerations

### Expected Overhead

**Hashing**:
- SCC detection: O(V + E) per dependency graph
- Hash computation: ~1ms per definition
- Acceptable for parse time, not hot path

**Multi-phase parsing**:
- 3x parse overhead → ~3x total parse time
- Mitigated by:
  - Caching
  - Parallelization
  - Incremental parsing

**Queries**:
- Multi-location lookup: 1 query returns N results
- Reverse lookup: Indexed, should be fast
- Metadata lookup: Separate query, cacheable

**Concept resolution**:
- Map lookup: O(1)
- Cache aggressively: ~0 overhead after init

### Optimization Strategies

**Caching**:
- Parse results (keyed by file content hash)
- Concept → hash mapping (static)
- Type → location mapping (warm cache)
- Metadata (rarely changes)

**Parallelization**:
- Parse files in parallel
- Compute hashes in parallel (independent SCCs)
- Database queries in parallel

**Incremental**:
- Re-parse only changed files
- Re-hash only affected SCCs
- Invalidate cache selectively

**Indexing**:
- Critical: All queries have appropriate indexes
- Monitor: Slow query log
- Optimize: Add indexes as needed

## Risk Mitigation

### Technical Risks

**Risk 1: SCC Detection Bugs**
- **Impact**: Incorrect hashes, system breakage
- **Mitigation**:
  - Comprehensive tests
  - Validate against known correct implementations
  - Fuzzing with randomly generated dependency graphs

**Risk 2: Performance Degradation**
- **Impact**: Slow parsing, poor UX
- **Mitigation**:
  - Benchmark at each phase
  - Set performance budgets
  - Optimize hot paths
  - Cache aggressively

**Risk 3: Data Migration Failures**
- **Impact**: Lost data, corruption
- **Mitigation**:
  - Test migrations on copies of production data
  - Backup before migration
  - Rollback plan
  - Incremental migration

**Risk 4: Concept ID Management Overhead**
- **Impact**: Manual work, errors
- **Mitigation**:
  - Tooling to assist
  - Validation in CI
  - Clear naming conventions
  - Documentation

### Process Risks

**Risk 5: Scope Creep**
- **Impact**: Never ship
- **Mitigation**:
  - Strict phasing
  - MVP mindset
  - Defer nice-to-haves

**Risk 6: Breaking Changes**
- **Impact**: Existing code breaks
- **Mitigation**:
  - Comprehensive test suite
  - Feature flags for rollback
  - Gradual migration
  - Compatibility shims

**Risk 7: Team Coordination**
- **Impact**: Conflicts, wasted work
- **Mitigation**:
  - Clear ownership of phases
  - Regular sync meetings
  - Shared design doc (this doc)
  - Code review

## Success Metrics

### Correctness Metrics
- ✅ All tests pass
- ✅ Hash stability: Parse same file 1000x → identical hashes
- ✅ No stabilization needed (hashes are stable by content)
- ✅ Mutual recursion works
- ✅ Builtins resolve correctly

### Performance Metrics
- ✅ Parse time < 2x current (goal: ~same after caching)
- ✅ Hash computation < 10ms per item
- ✅ Query latency < 100ms p95
- ✅ Startup time < 5s

### Quality Metrics
- ✅ No hash collisions detected
- ✅ All concept IDs resolve
- ✅ Error messages include names (not hashes)
- ✅ Source maps enable debugging

### Adoption Metrics
- ✅ All stdlib uses content hashing
- ✅ All builtins use concept IDs
- ✅ 0 uses of location-based IDs (removed)
- ✅ Migration guide exists and is followed

## Open Questions (High Priority)

### Q1: SCC Hash Stability Across Versions
**Question**: If we change the SCC hashing algorithm, all hashes change. How to handle?

**Options**:
- Version the hash algorithm
- Accept breaking change
- Maintain compatibility layer

**Decision needed**: Before Phase 1

### Q2: Concept ID Namespace
**Question**: Who can create concept IDs? How to prevent collisions?

**Options**:
- Darklang-owned only
- User namespaces (like package locations)
- UUID-based (guaranteed unique)

**Decision needed**: Before Phase 6

### Q3: Multi-Location Preferred Algorithm
**Question**: Beyond "newest", what other factors matter?

**Considerations**:
- User preference
- Semantic versioning
- Deprecation status
- Owner authority

**Decision needed**: Before Phase 4

### Q4: Performance Acceptable?
**Question**: Is 3-phase parsing fast enough for interactive editing?

**Needs**:
- Prototype and measure
- Incremental parsing strategy
- Caching strategy

**Decision needed**: After Phase 5 prototype

### Q5: Error Messages
**Question**: How detailed should error messages be when hash not found?

**Options**:
- Just hash (terrible)
- Hash + location hint
- Hash + all known locations
- Hash + concept ID (if applicable)

**Decision needed**: Phase 7

## Conclusion

True reference-by-hash for Darklang's package system is achievable but requires coordinated changes across all layers. The three major blockers—mutual recursion, builtin references, and multi-location mapping—each have known solutions that have been validated in other systems.

**Estimated Timeline**: 22 weeks (5-6 months) for full implementation and rollout.

**Key Success Factors**:
1. Disciplined phased approach
2. Comprehensive testing at each phase
3. Performance monitoring throughout
4. Clear ownership and coordination
5. Willingness to iterate based on learnings

**Recommendation**: Proceed with Phase 0 (preparation) immediately. The foundation is solid, the path is clear, and the benefits—true content-addressed code, stable references, and principled identity—are worth the investment.
