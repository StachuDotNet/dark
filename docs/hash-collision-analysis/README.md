# Hash Collision Analysis: Package References and Debug Metadata

This directory contains comprehensive analysis of the package reference Hash collision issue discovered during the Guid→Hash migration, along with proposed solutions.

---

## Quick Summary

**The Problem**: Multiple types with identical structure (e.g., `type ID = Int64` in different modules) hash to the same value, causing collisions in reverse lookups (Hash → Location). This breaks pretty printing and error messages.

**Root Cause**: Hash is computed from content only (name + definition), not location (module path).

**Why It Matters**: Hash collisions are actually *features* (same Hash = structurally identical), not bugs. We just need additional metadata alongside Hash for human-readable output.

---

## Document Guide

### Start Here
1. **[HASH_PLUS_LOCATION_PROBLEM.md](./HASH_PLUS_LOCATION_PROBLEM.md)** - Problem space analysis
   - 6 specific problems identified
   - Key insight: Hash for WHAT, Location for WHERE
   - Read this first to understand the full scope

### Solution Options
2. **[SOLUTION_1_PACKAGE_REF.md](./SOLUTION_1_PACKAGE_REF.md)** - Add optional location to type references
   - Add `PackageRef = { id: Hash; location: Option<PackageLocation> }`
   - Best for error messages and graceful degradation
   - Handles type moves without breaking

3. **[SOLUTION_2_LOCATION_IN_HASH.md](./SOLUTION_2_LOCATION_IN_HASH.md)** - Include location in Hash computation
   - Change Hash to include module path
   - Simplest fix but defeats content-based addressing
   - Breaking change (all existing Hashes become invalid)

4. **[SOLUTION_3_SEPARATE_CONTENT_HASH.md](./SOLUTION_3_SEPARATE_CONTENT_HASH.md)** - Two hashes (identity + content)
   - Identity hash (from location) + Content hash (from structure)
   - Can detect duplicates AND verify content
   - More storage overhead, more complexity

5. **[SOLUTION_4_DEBUG_METADATA.md](./SOLUTION_4_DEBUG_METADATA.md)** - Debug metadata system (NEW!)
   - Keep RT lean (just Hash), store location in separate debug DB
   - PT has full location, RT has just Hash, debug DB bridges them
   - Best for runtime performance + rich debugging

### Broader Context
6. **[DEBUG_METADATA_SYSTEM.md](./DEBUG_METADATA_SYSTEM.md)** - Debug metadata architecture
   - How debug metadata fits in PT/RT split
   - Lifecycle: creation, recording, persistence, querying
   - Comparison with other languages (DWARF, source maps, etc.)
   - Implementation roadmap

7. **[SOLUTION_COMPARISON.md](./SOLUTION_COMPARISON.md)** - Side-by-side comparison
   - Problem-by-problem analysis
   - Implementation complexity
   - Migration paths
   - Decision matrix

---

## Solutions at a Glance

| Solution | RT Size | Error Messages | Handles Moves | Detect Duplicates | Complexity |
|----------|---------|----------------|---------------|-------------------|------------|
| **1. PackageRef** | Hash + Location | ✅ Best | ✅ Yes | ✅ Yes | ⚠️ Medium |
| **2. Location in Hash** | Hash | ⚠️ Partial | ❌ No | ❌ No | ✅ Low |
| **3. Two Hashes** | 2× Hash | ⚠️ Partial | ❌ No | ✅ Yes | ⚠️ Medium |
| **4. Debug Metadata** | Hash | ✅ Good | ✅ Yes | ✅ Yes | ⚠️ Medium-High |

---

## Recommended Approach

### Short Term (Fix Hash Collision Issue)
**Solution 4a: Minimal Debug Metadata**

1. Make `location` required in PT.PackageRef
2. Keep RT.Package as just Hash (lean)
3. Add simple DebugMetadata with Hash→Location mapping
4. Extract debug metadata during PT→RT conversion
5. Query debug metadata for error messages and pretty printing

**Why**: Fixes immediate issue, keeps runtime lean, foundation for future tooling.

### Long Term (Rich Developer Experience)
**Solution 4b: Rich Debug Metadata**

Expand debug metadata to include:
- Source positions (for LSP "go to definition")
- Source text (for tooltips)
- Dependency graphs (for refactoring tools)
- Profiling data (for performance analysis)

**Why**: Enables powerful developer tooling without bloating runtime.

---

## Key Insights

1. **Hash collisions are features, not bugs**
   - Same Hash = structurally identical types
   - This is useful information to preserve
   - Don't defeat content-based addressing

2. **Separate identity from integrity**
   - Hash verifies WHAT it is (structure)
   - Location tells WHERE it is (name/path)
   - These are separate concerns

3. **PT/RT split is architectural advantage**
   - PT can be rich (all info for tooling)
   - RT can be lean (minimal for execution)
   - Debug metadata bridges the gap

4. **Debug metadata is derivable**
   - Can always rebuild from source
   - Not required for correctness
   - Graceful degradation if missing

---

## Current Status

### What's Working
- 9,178 out of 9,179 tests passing (99.99%)
- Guid→Hash migration mostly complete
- Package IDs are now content-based Hashes

### What's Broken
- 1 failing test: "nested module declaration"
- Three identical `type ID = Int64` in nested modules
- All hash to same value, collision in typeIdToLoc mapping
- Pretty printer shows all types at last location (MyModule3)

### What's Next
- Review proposed solutions
- Choose architectural direction
- Implement chosen solution
- Fix failing test
- Verify all tests pass

---

## Implementation Considerations

### If Choosing Solution 1 (PackageRef)
- Update all FQTypeName/FQFnName/FQValueName to use PackageRef
- Thread location through parser and name resolution
- Update serialization (binary + JSON)
- Add multi-valued reverse lookup to PackageManager
- Update pretty printer to use location hints

### If Choosing Solution 4 (Debug Metadata)
- Add location field to PT.PackageRef (required)
- Create DebugMetadata type and storage
- Thread debugDB through PT2RT conversion
- Extract and record debug info during conversion
- Update error formatters and pretty printers to query debugDB
- Add builtin functions for debug metadata access

### Common Requirements
- Update PT2RT conversion process
- Modify error formatting
- Enhance pretty printing
- Update test expectations
- Document new architecture

---

## Related Darklang Concepts

### Content-Based Addressing
- Hash computed from structure (name + definition)
- Same structure → same Hash
- Enables deduplication, verification, caching

### Package Manager Architecture
- Forward lookup: Location → Hash (`typeLocMap`)
- Reverse lookup: Hash → PackageType (`types`)
- Missing: Hash → Location (this is what we're adding)

### PT/RT Split
- PT = Program Types (source representation)
- RT = Runtime Types (execution representation)
- PT→RT conversion happens during compilation
- RT is optimized for execution, PT for analysis

---

## Questions to Consider

1. **Is runtime performance critical?**
   - If yes: Prefer Solution 4 (lean RT)
   - If no: Solution 1 is simpler

2. **Will types move between modules frequently?**
   - If yes: Need graceful handling (Solutions 1 or 4)
   - If no: Solution 2 might suffice

3. **How important is developer tooling (LSP, refactoring)?**
   - Critical: Solution 4 (foundation for rich tooling)
   - Nice-to-have: Solution 1 (location available)
   - Don't care: Solution 2 (simplest)

4. **What's the migration/implementation timeline?**
   - Need quick fix: Solution 2 (change hash computation)
   - Can invest time: Solution 4 (build it right)
   - Prefer gradual: Solution 1 (optional location)

---

## File Locations

### This Analysis
- `/home/stachu/code/dark/docs/hash-collision-analysis/`

### Relevant Code
- **Parser**: `packages/darklang/languageTools/writtenTypesToProgramTypes.dark`
- **Package Manager**: `backend/src/LibPackageManager/PackageManager.fs`
- **PT Types**: `backend/src/LibExecution/ProgramTypes.fs`
- **RT Types**: `backend/src/LibExecution/RuntimeTypes.fs`
- **PT2RT**: `backend/src/LibExecution/ProgramTypesToRuntimeTypes.fs`
- **Hash Computation**: `backend/src/LibSerialization/Hashing/ContentHash.fs`
- **Pretty Printer**: `packages/darklang/prettyPrinter/`

### Failing Test
- `backend/tests/Tests/NewParser.Tests.fs` line ~382
- Test: "nested module declaration"
- Issue annotated with explanation

---

## Next Steps

1. Review all documents in this directory
2. Discuss architectural implications
3. Choose solution approach
4. Create implementation plan
5. Implement chosen solution
6. Fix failing test
7. Verify all tests pass
8. Document final architecture

---

## Questions or Feedback?

This analysis was created to support decision-making around the Hash collision issue. If you have questions, spot issues, or want to propose alternative solutions, please discuss them in the context of these documents.
