# Layer 6: SQL Schema Analysis

## Current State

### Tables (Inferred from Code)

From the SQL queries in `PT/SQL/*.fs`:

**`locations` table**:
```sql
CREATE TABLE locations (
  location_id TEXT PRIMARY KEY,
  item_id BLOB NOT NULL,  -- Hash as Guid bytes
  branch_id TEXT,  -- UUID, nullable
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,  -- Dot-separated string: "Stdlib.List"
  name TEXT NOT NULL,
  item_type TEXT NOT NULL,  -- 'type' | 'fn' | 'value'
  created_at TEXT NOT NULL,  -- ISO timestamp
  deprecated_at TEXT  -- ISO timestamp, nullable
);
```

**`package_types` table**:
```sql
CREATE TABLE package_types (
  id BLOB PRIMARY KEY,  -- Hash as Guid bytes
  pt_def BLOB NOT NULL,  -- Serialized PT.PackageType
  rt_def BLOB NOT NULL  -- Serialized RT.PackageType
);
```

**`package_functions` table**:
```sql
CREATE TABLE package_functions (
  id BLOB PRIMARY KEY,
  pt_def BLOB NOT NULL,  -- Serialized PT.PackageFn
  rt_instrs BLOB NOT NULL  -- Serialized RT.PackageFn (Instructions)
);
```

**`package_values` table**:
```sql
CREATE TABLE package_values (
  id BLOB PRIMARY KEY,
  pt_def BLOB NOT NULL,  -- Serialized PT.PackageValue
  rt_dval BLOB NOT NULL  -- Serialized RT.Dval
);
```

**`package_ops` table**:
```sql
CREATE TABLE package_ops (
  id BLOB PRIMARY KEY,  -- Hash of the op itself
  branch_id TEXT,  -- UUID, nullable
  op_blob BLOB NOT NULL,  -- Serialized PackageOp
  applied BOOLEAN NOT NULL  -- Has this op been applied?
);
```

### Query Patterns

**Find (Location → Hash)**:
```sql
-- PT/SQL/Types.fs:24-36
SELECT item_id
FROM locations
WHERE owner = @owner
  AND modules = @modules
  AND name = @name
  AND item_type = 'type'
  AND deprecated_at IS NULL
  AND (branch_id IS NULL OR branch_id = @branch_id)
ORDER BY created_at DESC
LIMIT 1
```

**Get (Hash → Content)**:
```sql
-- PT/SQL/Types.fs:54
SELECT pt_def FROM package_types WHERE id = @id
```

**GetLocation (Hash → Location)**:
```sql
-- PT/SQL/Types.fs:68-76
SELECT owner, modules, name
FROM locations
WHERE item_id = @item_id
  AND item_type = 'type'
  AND deprecated_at IS NULL
  AND (branch_id IS NULL OR branch_id = @branch_id)
ORDER BY created_at DESC
LIMIT 1
```

### Op Playback Pattern

From `PT/SQL/OpPlayback.fs`:

1. **Insert op into package_ops**:
```sql
INSERT OR IGNORE INTO package_ops (id, branch_id, op_blob, applied)
VALUES (@id, @branch_id, @op_blob, @applied)
```

2. **Apply AddType**: INSERT OR REPLACE into package_types

3. **Apply SetTypeName**:
   - UPDATE locations SET deprecated_at = now() WHERE item_id = @id
   - INSERT INTO locations (new row)

### Indexing (Not Explicitly Defined)
No CREATE INDEX statements visible in code.

Likely needed:
- `locations(owner, modules, name, item_type)`
- `locations(item_id, item_type)`
- `locations(branch_id)`

## Problems

### 1. No Foreign Key Constraints
**Problem**: No referential integrity between tables

**Missing constraints**:
- `locations.item_id` → `package_types.id` (or functions/values)
- `locations.branch_id` → `branches.id` (if branches table exists)
- `package_ops.branch_id` → `branches.id`

**Impact**:
- Can have location pointing to non-existent hash
- Can delete content while locations remain
- Orphaned records
- Data inconsistency

### 2. Modules as Concatenated String
**Problem**: `modules` column is dot-separated string: "Stdlib.List"

**Issues**:
- Cannot query "all items in Stdlib" efficiently
- Cannot query "all items in Stdlib.List.SubModule"
- String manipulation needed for hierarchy
- Prefix matching requires LIKE with wildcards (slow)

**Better**: Separate table or JSON array

### 3. No Explicit Indexes
**Problem**: No CREATE INDEX statements

**Impact**:
- Slow queries on common access patterns
- Full table scans
- Poor performance as data grows

**Needed indexes**:
- `idx_locations_find` on (owner, modules, name, item_type, deprecated_at)
- `idx_locations_reverse` on (item_id, item_type, deprecated_at)
- `idx_locations_branch` on (branch_id)
- `idx_ops_branch` on (branch_id, applied)

### 4. Single Location per Hash Assumption
**Problem**: Queries use `LIMIT 1` implying one result

**Reality**: Multiple locations can map to same hash

**Current workaround**: `ORDER BY created_at DESC` picks newest

**Issues**:
- Cannot see all locations for a hash efficiently
- Application must make N queries to find all
- No atomic "get all locations" operation

### 5. No Metadata Table
**Problem**: Metadata (description, deprecation) embedded in pt_def blobs

**Issues**:
- Cannot query by description (e.g., search)
- Cannot update metadata without re-serializing entire item
- Metadata tied to hash, but might want per-location metadata
- Cannot track metadata history

### 6. Branch Isolation Incomplete
**Problem**: Branch handling via nullable branch_id column

**Issues**:
- No branch metadata (name, parent, created_at, etc.)
- No branch hierarchy tracking
- Cannot query "all branches"
- Cannot track merge state
- Cannot implement proper branch semantics

### 7. Op Deduplication but No History
**Problem**: `package_ops` table has primary key on op hash

**Behavior**: `INSERT OR IGNORE` means duplicate ops are silently skipped

**Issues**:
- Cannot track when ops were applied
- Cannot track who applied them
- Cannot see op history
- Cannot implement undo/redo
- Cannot track op ordering

### 8. Binary Blob Storage
**Problem**: All content stored as serialized binary blobs

**Pros**:
- Fast deserialization
- Compact storage
- Version control via serialization format

**Cons**:
- Cannot query by content fields
- Cannot index on content properties
- Cannot do SQL-level validation
- Harder to debug/inspect
- Schema changes require reprocessing all blobs

### 9. UUID Storage as BLOB
**Problem**: Hashes stored as BLOB (16 bytes of Guid)

**Issues**:
- Not human-readable in SQL tools
- Cannot easily copy-paste hashes
- Conversion overhead (Hash ↔ Guid ↔ bytes)
- Alternative: TEXT with hex encoding

### 10. No Timestamps on Content
**Problem**: package_types/functions/values have no timestamp columns

**Issues**:
- Cannot query "when was this created?"
- Cannot query "when was this last modified?"
- Cannot implement caching TTLs
- Cannot track content age

## Requirements for Ref-by-Hash

### R1: Proper Foreign Keys
Establish referential integrity:

```sql
-- If using unified content table:
CREATE TABLE package_items (
  id BLOB PRIMARY KEY,
  item_type TEXT NOT NULL,
  pt_def BLOB NOT NULL,
  rt_def BLOB NOT NULL,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  CHECK (item_type IN ('type', 'fn', 'value'))
);

CREATE TABLE locations (
  location_id TEXT PRIMARY KEY,
  item_id BLOB NOT NULL,
  item_type TEXT NOT NULL,
  ...,
  FOREIGN KEY (item_id) REFERENCES package_items(id) ON DELETE CASCADE
);
```

Or if keeping separate tables:
```sql
-- Would need conditional foreign key based on item_type, which SQL doesn't support well
-- Better: Use triggers or application-level enforcement
```

### R2: Module Hierarchy Support
**Option A**: Normalize modules
```sql
CREATE TABLE modules (
  module_id TEXT PRIMARY KEY,  -- "Darklang.Stdlib.List"
  parent_module_id TEXT,  -- "Darklang.Stdlib"
  name TEXT NOT NULL,  -- "List"
  FOREIGN KEY (parent_module_id) REFERENCES modules(module_id)
);

CREATE TABLE locations (
  ...,
  module_id TEXT NOT NULL,
  FOREIGN KEY (module_id) REFERENCES modules(module_id)
);
```

**Option B**: Use JSON or array type
```sql
CREATE TABLE locations (
  ...,
  modules TEXT NOT NULL,  -- JSON array: ["Stdlib", "List"]
  modules_path TEXT AS (json_extract(modules, '$')) STORED
  -- Or virtual column for querying
);

CREATE INDEX idx_modules_path ON locations(modules_path);
```

**Option C**: Keep string but add computed columns
```sql
CREATE TABLE locations (
  ...,
  modules TEXT NOT NULL,  -- "Stdlib.List"
  modules_tsvector TEXT AS (/* text search vector */)
);
```

**Recommendation**: Option A (normalized) for correctness, or Option C (computed) for simplicity

### R3: Comprehensive Indexes
```sql
-- Find: Location → Hash
CREATE INDEX idx_locations_find ON locations(
  owner, modules, name, item_type, deprecated_at, branch_id
);

-- Reverse: Hash → Locations
CREATE INDEX idx_locations_reverse ON locations(
  item_id, item_type, deprecated_at, branch_id
);

-- Branch queries
CREATE INDEX idx_locations_branch ON locations(branch_id, created_at);

-- Op queries
CREATE INDEX idx_ops_branch_applied ON package_ops(branch_id, applied);
CREATE INDEX idx_ops_created ON package_ops(created_at);

-- Module hierarchy queries
CREATE INDEX idx_locations_modules ON locations(modules);
```

### R4: Metadata Table
Separate metadata from content:

```sql
CREATE TABLE metadata (
  item_id BLOB NOT NULL,
  item_type TEXT NOT NULL,
  location TEXT,  -- Optional: per-location metadata
  description TEXT,
  deprecated_json TEXT,  -- JSON encoding of Deprecation
  author TEXT,
  since TEXT,  -- Version string
  tags TEXT,  -- JSON array
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now')),

  PRIMARY KEY (item_id, item_type, COALESCE(location, '')),
  FOREIGN KEY (item_id) REFERENCES package_items(id)
);

CREATE INDEX idx_metadata_search ON metadata(description);
CREATE INDEX idx_metadata_tags ON metadata(tags);
```

### R5: Branch Management Tables
```sql
CREATE TABLE branches (
  branch_id TEXT PRIMARY KEY,
  name TEXT NOT NULL UNIQUE,
  parent_branch_id TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  created_by TEXT,
  merged_at TEXT,
  merged_into TEXT,
  state TEXT NOT NULL CHECK (state IN ('active', 'merged', 'abandoned')),

  FOREIGN KEY (parent_branch_id) REFERENCES branches(branch_id)
);

-- Update locations to use branch_id FK
ALTER TABLE locations
  ADD FOREIGN KEY (branch_id) REFERENCES branches(branch_id);

-- Update ops to use branch_id FK
ALTER TABLE package_ops
  ADD FOREIGN KEY (branch_id) REFERENCES branches(branch_id);
```

### R6: Op History and Audit
```sql
CREATE TABLE package_ops (
  op_id TEXT PRIMARY KEY,  -- Unique per application (hash + timestamp?)
  op_hash BLOB NOT NULL,  -- Content hash of op
  branch_id TEXT,
  op_blob BLOB NOT NULL,
  applied BOOLEAN NOT NULL DEFAULT FALSE,

  -- NEW: Audit fields
  applied_at TEXT,
  applied_by TEXT,
  order_index INTEGER NOT NULL,  -- Sequence within branch

  FOREIGN KEY (branch_id) REFERENCES branches(branch_id)
);

CREATE INDEX idx_ops_hash ON package_ops(op_hash);
CREATE INDEX idx_ops_branch_order ON package_ops(branch_id, order_index);
CREATE INDEX idx_ops_applied ON package_ops(applied, applied_at);
```

This allows:
- Same op applied multiple times (different op_id)
- Tracking who applied when
- Ordering guarantee
- Audit trail

### R7: Content Timestamps
```sql
CREATE TABLE package_types (
  id BLOB PRIMARY KEY,
  pt_def BLOB NOT NULL,
  rt_def BLOB NOT NULL,

  -- NEW
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now')),
  content_size INTEGER AS (length(pt_def)) STORED
);

-- Similar for functions and values
```

### R8: Concept IDs (Future)
```sql
CREATE TABLE concepts (
  concept_id TEXT PRIMARY KEY,
  item_type TEXT NOT NULL,
  current_hash BLOB NOT NULL,
  canonical_location TEXT NOT NULL,  -- "Darklang.Stdlib.Option"
  created_at TEXT NOT NULL DEFAULT (datetime('now')),

  FOREIGN KEY (current_hash) REFERENCES package_items(id),
  CHECK (item_type IN ('type', 'fn', 'value'))
);

CREATE TABLE concept_history (
  concept_id TEXT NOT NULL,
  hash BLOB NOT NULL,
  valid_from TEXT NOT NULL,
  valid_to TEXT,  -- NULL if current
  change_description TEXT,

  PRIMARY KEY (concept_id, hash, valid_from),
  FOREIGN KEY (concept_id) REFERENCES concepts(concept_id),
  FOREIGN KEY (hash) REFERENCES package_items(id)
);
```

## Proposed Changes

### Schema Evolution
```sql
-- Version 1: Current state (inferred)
-- Version 2: Add indexes, metadata table
-- Version 3: Add branch management
-- Version 4: Add concept IDs

-- Migration strategy: Incremental, backwards compatible

-- Phase 1: Add indexes
CREATE INDEX IF NOT EXISTS idx_locations_find ...;
CREATE INDEX IF NOT EXISTS idx_locations_reverse ...;
-- etc.

-- Phase 2: Add metadata table
CREATE TABLE IF NOT EXISTS metadata (...);
-- Migrate metadata from pt_def blobs
INSERT INTO metadata
  SELECT id, 'type', NULL, /* extract from pt_def */, ...
  FROM package_types;

-- Phase 3: Add branch table
CREATE TABLE IF NOT EXISTS branches (...);
-- Create default branch for existing data
INSERT INTO branches (branch_id, name, state)
  VALUES (NULL, 'main', 'active');  -- NULL represents main branch

-- Phase 4: Add concept IDs
CREATE TABLE IF NOT EXISTS concepts (...);
CREATE TABLE IF NOT EXISTS concept_history (...);
```

### Complete Schema (MVP)
```sql
-- ===== Core Content =====

CREATE TABLE package_types (
  id BLOB PRIMARY KEY,
  pt_def BLOB NOT NULL,
  rt_def BLOB NOT NULL,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE package_functions (
  id BLOB PRIMARY KEY,
  pt_def BLOB NOT NULL,
  rt_instrs BLOB NOT NULL,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE package_values (
  id BLOB PRIMARY KEY,
  pt_def BLOB NOT NULL,
  rt_dval BLOB NOT NULL,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ===== Branches =====

CREATE TABLE branches (
  branch_id TEXT PRIMARY KEY,
  name TEXT NOT NULL UNIQUE,
  parent_branch_id TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  created_by TEXT,
  merged_at TEXT,
  state TEXT NOT NULL DEFAULT 'active' CHECK (state IN ('active', 'merged', 'abandoned')),

  FOREIGN KEY (parent_branch_id) REFERENCES branches(branch_id)
);

-- Insert main branch (NULL represents main)
INSERT INTO branches (branch_id, name, state)
  VALUES (NULL, 'main', 'active');

-- ===== Locations =====

CREATE TABLE locations (
  location_id TEXT PRIMARY KEY,
  item_id BLOB NOT NULL,
  branch_id TEXT,
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,  -- Dot-separated for now
  name TEXT NOT NULL,
  item_type TEXT NOT NULL CHECK (item_type IN ('type', 'fn', 'value')),
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  deprecated_at TEXT,

  FOREIGN KEY (branch_id) REFERENCES branches(branch_id)
  -- Note: Cannot easily add FK to item_id due to multiple tables
);

CREATE INDEX idx_locations_find ON locations(
  owner, modules, name, item_type, deprecated_at, branch_id
);

CREATE INDEX idx_locations_reverse ON locations(
  item_id, item_type, deprecated_at
);

CREATE INDEX idx_locations_branch ON locations(branch_id, created_at);

-- ===== Metadata =====

CREATE TABLE metadata (
  item_id BLOB NOT NULL,
  item_type TEXT NOT NULL CHECK (item_type IN ('type', 'fn', 'value')),
  description TEXT NOT NULL DEFAULT '',
  deprecated_json TEXT,
  author TEXT,
  since TEXT,
  tags_json TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now')),

  PRIMARY KEY (item_id, item_type)
);

CREATE INDEX idx_metadata_description ON metadata(description);

-- ===== Operations =====

CREATE TABLE package_ops (
  op_id TEXT PRIMARY KEY,  -- UUID
  op_hash BLOB NOT NULL,  -- Content hash
  branch_id TEXT,
  op_blob BLOB NOT NULL,
  applied BOOLEAN NOT NULL DEFAULT TRUE,
  applied_at TEXT NOT NULL DEFAULT (datetime('now')),
  applied_by TEXT,
  order_index INTEGER NOT NULL,

  FOREIGN KEY (branch_id) REFERENCES branches(branch_id)
);

CREATE INDEX idx_ops_hash ON package_ops(op_hash);
CREATE INDEX idx_ops_branch ON package_ops(branch_id, order_index);
CREATE INDEX idx_ops_applied ON package_ops(applied, applied_at);
```

## Code Impacts

### Migration Plan

**Phase 1: Baseline**
- Document current schema
- Add migration framework
- Version current schema as v1

**Phase 2: Add indexes**
- Create indexes on existing tables
- No data migration needed
- Verify performance improvement

**Phase 3: Add metadata table**
- Create metadata table
- Extract metadata from pt_def blobs
- Update OpPlayback to write to metadata table
- Update queries to read from metadata table

**Phase 4: Add branches table**
- Create branches table
- Insert 'main' branch
- Update FK constraints
- Test branch operations

**Phase 5: Enhance ops table**
- Add audit columns
- Update OpPlayback to populate
- Update queries

**Phase 6: Add concept IDs (future)**
- Create concepts tables
- Generate concept IDs for existing items
- Implement concept tracking logic

### Code Changes Required

**New**: Database migration framework
```fsharp
module DatabaseMigrations =
  type Migration = {
    version : int
    name : string
    up : unit -> Task<unit>
    down : unit -> Task<unit>
  }

  let migrations = [
    { version = 1; name = "Baseline"; ... }
    { version = 2; name = "Add indexes"; ... }
    { version = 3; name = "Add metadata table"; ... }
    // etc.
  ]

  let getCurrentVersion : unit -> Task<int>
  let migrate : unit -> Task<unit>
```

**Update**: OpPlayback
```fsharp
let applyAddType (typ : PT.PackageType) : Task<unit> =
  task {
    // Insert content
    do! insertIntoPackageTypes typ.id typ.declaration

    // Insert metadata
    do! insertMetadata typ.id "type" typ.description typ.deprecated
  }
```

**Update**: Query functions
```fsharp
let getType (id : Hash) : Ply<Option<PT.PackageType>> =
  uply {
    // Get content
    let! decl = queryPackageTypes id

    // Get metadata
    let! metadata = queryMetadata id "type"

    return
      Option.map2
        (fun d m -> { declaration = d; id = id; metadata = m })
        decl
        metadata
  }
```

## Open Questions

### Q1: Normalize vs Denormalize
**Question**: Should we fully normalize (modules table, etc.) or keep simple?

**Tradeoff**:
- Normalized: Correct, but complex
- Denormalized: Simple, but harder to query

**Recommendation**: Start denormalized with good indexes, normalize if needed for performance

### Q2: Blob Storage Alternatives
**Question**: Should we extract commonly-queried fields from blobs?

**Example**: Extract function parameter types for type-based search

**Recommendation**: No for MVP. Keep blobs opaque. Add when needed for specific queries.

### Q3: Content Deduplication
**Question**: If two identical types exist at different locations, do we store content once?

**Current**: Yes - same hash = same content

**Issue**: If we update metadata, does it affect all locations?

**Recommendation**: Metadata per-hash (shared), with optional per-location overrides

### Q4: Archive Strategy
**Question**: Should we ever delete old content?

**Recommendation**: No. Disk is cheap. Keep everything. Add `archived` flag if needed.

### Q5: Multi-Tenancy
**Question**: How to support multiple users/workspaces in schema?

**Current**: No separation

**Future**: Add workspace_id column to all tables?

**Recommendation**: Not for MVP. Can add later without breaking changes.

### Q6: Backup and Replication
**Question**: How to handle backup/restore with binary blobs?

**Recommendation**: Standard SQLite backup mechanisms. Blobs are fine.

### Q7: Performance at Scale
**Question**: Will this schema handle 100K+ packages?

**Concerns**:
- Index size
- Blob storage size
- Query performance

**Mitigation**:
- Indexes are critical
- Regular VACUUM
- Consider partitioning if needed

**Recommendation**: Prototype with large dataset before production

## Summary

**Key Insights**:
1. Need explicit indexes for performance
2. Metadata should be separate table
3. Branch management needs proper tables
4. Foreign keys for referential integrity
5. Audit trail in ops table

**Biggest Risks**:
1. Migration complexity
2. Performance at scale
3. Module hierarchy queries
4. Referential integrity with multi-table content
5. Blob storage overhead

**Next Steps**:
1. Create migration framework
2. Document current schema explicitly
3. Add indexes as Phase 1
4. Test performance
5. Add metadata table as Phase 2
6. Iterate on branch management
