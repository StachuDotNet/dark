# Hash-Based Artifact References: Code Review

## Branch: ocean/ref-by-hash

### Summary
This branch implements a transition from UUID-based to hash-based artifact identification for packages. The implementation is partially complete but has significant inconsistencies that need addressing.

## What's Done

### 1. Core Type Changes
- **ProgramTypes.fs & RuntimeTypes.fs**: Successfully converted FQTypeName, FQValueName, and FQFnName to use `Hash` instead of UUIDs
  - `type Package = Hash` defined in all FQ modules
  - Hash field added to PackageType, PackageValue, and PackageFn records

### 2. Database Schema Migration
- **20250825_145219_add_hash_columns.sql**: Adds hash columns to existing tables
- **20250902_000000_remove_package_id_columns.sql**: Drops UUID-based tables and recreates with hash as primary key
  - package_types_v0, package_values_v0, package_functions_v0 now use hash TEXT as PK
  - Indexes created on (owner, modules, name) for name-based lookups

### 3. Hashing Implementation
- **LibPackageManager/Hashing.fs**: Implements content-based hashing using SHA-256
  - Canonical representations for PackageType, PackageValue, PackageFn
  - Handles self-references to avoid circular dependencies during hash calculation
  - Serializes to deterministic JSON before hashing

### 4. Binary Serialization Updates
- **LibBinarySerialization/Serializers/**: Updated to include hash in serialized format
  - PT and RT serializers write/read hash field
  - Hash stored alongside the artifact data

## What's Inconsistent/Missing

### 1. Critical: Dual Hashing Systems
- **LibPackageManager/Hashing.fs**: Uses content-based SHA-256 hashing
- **LibExecution/PackageIDs.fs**: Uses either:
  - Fixed hash strings for known Darklang packages
  - Name-based SHA-256 hashing for unknown packages (lines 986-1007)
  
This creates a fundamental inconsistency where the same package could have different hashes depending on the code path.

### 2. Hash Generation During Parsing
- **LibParser/WrittenTypesToProgramTypes.fs**: Uses `PackageIDs.Type/Value/Fn.hashForName` (lines 534, 556, 596)
- This generates name-based hashes during parsing, not content-based hashes
- Content hashes are only calculated when inserting into the database (LibPackageManager/Inserts.fs)

### 3. Hash Preservation Issues
- **LocalExec/LoadPackagesFromDisk.fs** (lines 65-87): Complex logic to preserve original hashes during re-parsing
- **LibParser/TestModule.fs** and **Canvas.fs**: Similar hash preservation logic
- This suggests the hash calculation isn't deterministic or happens at the wrong time

### 4. Missing Hash Algorithm Flexibility
- Hardcoded to SHA-256, no abstraction for algorithm selection
- No versioning for hash format changes
- No truncation or short ID generation implemented

### 5. Incomplete Migration Strategy
- No backfill mechanism for existing artifacts without hashes
- No compatibility layer for older serialization formats
- Database migrations drop existing tables (!), losing all data

## TODOs Found in Code

1. **LibExecution/PackageIDs.fs:214**: "TODO: where do these actually belong? are they used, even?"
2. **LibPackageManager/PackageManager.fs:14**: "TODO: bring back eager loading"
3. **LibParser/WrittenTypesToProgramTypes.fs:553, 567**: "TODO: assert OK" for name validation

## Recommendations

### Immediate Fixes Needed

1. **Unify Hash Calculation**
   - Remove name-based hashing from PackageIDs.fs
   - Calculate content hashes immediately after parsing, before first storage
   - Store hash in the parsed artifact structure

2. **Fix Hash Generation Timing**
   - Calculate hash in WrittenTypesToProgramTypes.toPT functions
   - Pass calculated hash through the pipeline
   - Remove hash preservation/adjustment logic

3. **Add Migration Safety**
   - Create new tables instead of dropping existing ones
   - Add backfill migration to calculate hashes for existing artifacts
   - Implement dual-read during transition period

### Design Improvements

1. **Hash Algorithm Abstraction**
   ```fsharp
   type HashAlgorithm = 
     | SHA256 
     | BLAKE3_256
     | XXH3_128
   
   type HashedArtifact = {
     algorithm: HashAlgorithm
     version: int
     hash: Hash
   }
   ```

2. **Short ID Generation**
   - Implement Crockford base32 encoding
   - Generate 8-12 character prefixes for UI display
   - Store full hash in DB, compute short IDs on demand

3. **Collision Detection**
   - Add unique constraint on hash columns
   - Implement collision detection during insert
   - Log and monitor for actual collisions

## Next Steps

1. Fix the dual hashing system issue (critical)
2. Implement proper migration strategy with data preservation
3. Add comprehensive tests for hash calculation determinism
4. Implement short ID generation for UI
5. Add collision detection and monitoring