# Layer 5: PackageManager Analysis

## Current State

### PT PackageManager Interface
From ProgramTypes.fs (lines 821-847):

```fsharp
type PackageManager = {
  // Location → Hash lookups
  findType : (BranchIDOpt * PackageLocation) -> Ply<Option<FQTypeName.Package>>
  findValue : (BranchIDOpt * PackageLocation) -> Ply<Option<FQValueName.Package>>
  findFn : (BranchIDOpt * PackageLocation) -> Ply<Option<FQFnName.Package>>

  // Hash → Content lookups
  getType : FQTypeName.Package -> Ply<Option<PackageType.PackageType>>
  getValue : FQValueName.Package -> Ply<Option<PackageValue.PackageValue>>
  getFn : FQFnName.Package -> Ply<Option<PackageFn.PackageFn>>

  // Search
  search : BranchIDOpt * Search.SearchQuery -> Ply<Search.SearchResults>

  // Ops application
  applyOps : (BranchIDOpt * List<PackageOp>) -> Ply<unit>

  init : Ply<unit>
}
```

### SQL Implementation
From `PT/SQL/PM.fs`:

```fsharp
let pt : PT.PackageManager = {
  findType = withCache cachelessPM.findType
  findValue = withCache cachelessPM.findValue
  findFn = withCache cachelessPM.findFn

  getType = withCache cachelessPM.getType
  getFn = withCache cachelessPM.getFn
  getValue = withCache cachelessPM.getValue

  getTypeLocation = withCache cachelessPM.getTypeLocation  // NOT in PT.PackageManager type!
  getValueLocation = withCache cachelessPM.getValueLocation
  getFnLocation = withCache cachelessPM.getFnLocation

  search = cachelessPM.search
  applyOps = cachelessPM.applyOps
  init = uply { return () }
}
```

**Note**: The implementation has `getTypeLocation` etc., but the PT.PackageManager type doesn't include them!

### Find Implementation
From `PT/SQL/Types.fs` (lines 17-48):

```fsharp
let find
  ((branchID, location) : Option<PT.BranchID> * PT.PackageLocation)
  : Ply<Option<PT.FQTypeName.Package>> =
  uply {
    let modulesStr = String.concat "." location.modules

    return!
      Sql.query
        """
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
        """
      |> ...
  }
```

**Pattern**: Location → Hash via `locations` table

### Get Implementation
From `PT/SQL/Types.fs` (lines 51-59):

```fsharp
let get (id : PT.FQTypeName.Package) : Ply<Option<PT.PackageType.PackageType>> =
  uply {
    return!
      "SELECT pt_def FROM package_types WHERE id = @id"
      |> Sql.query
      |> Sql.parameters [ "id", Sql.uuid (Hash.toGuid id) ]
      |> Sql.executeRowOptionAsync (fun read -> read.bytes "pt_def")
      |> Task.map (Option.map BS.PT.PackageType.deserialize)
  }
```

**Pattern**: Hash → Content via `package_types` table

### GetLocation Implementation
From `PT/SQL/Types.fs` (lines 62-89):

```fsharp
let getLocation
  ((branchID, id) : Option<PT.BranchID> * PT.FQTypeName.Package)
  : Ply<Option<PT.PackageLocation>> =
  uply {
    return!
      Sql.query
        """
        SELECT owner, modules, name
        FROM locations
        WHERE item_id = @item_id
          AND item_type = 'type'
          AND deprecated_at IS NULL
          AND (branch_id IS NULL OR branch_id = @branch_id)
        ORDER BY created_at DESC
        LIMIT 1
        """
      |> ...
  }
```

**Pattern**: Hash → Location (reverse lookup)

### Current Database Schema
Based on the SQL queries:

**`locations` table**:
- `location_id` (uuid, primary key)
- `item_id` (uuid) - The hash of the item
- `branch_id` (uuid, nullable)
- `owner` (string)
- `modules` (string) - Concatenated with "."
- `name` (string)
- `item_type` ('type' | 'value' | 'fn')
- `created_at` (timestamp)
- `deprecated_at` (timestamp, nullable)

**`package_types` table**:
- `id` (uuid, primary key) - The hash
- `pt_def` (bytes) - Serialized PackageType
- `rt_def` (bytes) - Serialized RT.PackageType

**`package_values` table**:
- `id` (uuid, primary key)
- `pt_def` (bytes)
- `rt_dval` (bytes)

**`package_functions` table**:
- `id` (uuid, primary key)
- `pt_def` (bytes)
- `rt_instrs` (bytes)

**`package_ops` table**:
- `id` (uuid, primary key) - Hash of the op
- `branch_id` (uuid, nullable)
- `op_blob` (bytes) - Serialized PackageOp
- `applied` (bool)

### In-Memory Implementation
From `PT/InMemory.fs`:

```fsharp
let create (ops : List<PT.PackageOp>) : PT.PackageManager =
  // Builds maps from ops
  // find: walks locations map
  // get: looks up in content map
  // getLocation: reverse lookup in locations map
```

### Caching Layer
From `Caching.fs`:

```fsharp
let withCache<'a, 'b> (f : 'a -> Ply<Option<'b>>) : ('a -> Ply<Option<'b>>) =
  // LRU cache wrapper
```

## Problems

### 1. Type Signature Mismatch
**Problem**: PT.PackageManager type doesn't include `getTypeLocation` etc., but implementation does

**Code evidence**:
- PT.PackageManager type (ProgramTypes.fs): No getLocation methods
- PT.SQL.PM implementation: Has getTypeLocation, getValueLocation, getFnLocation
- Used in code (e.g., prettty printing, error messages)

**Issue**: Type system doesn't enforce these methods exist, but code depends on them

### 2. Single Location Assumption
**Problem**: Both `find` and `getLocation` return `Option<T>`, implying at most one result

**SQL evidence**:
```sql
ORDER BY created_at DESC
LIMIT 1
```

**Reality**: Multiple locations can have the same hash (hash collisions in the "many locations, one hash" sense)

**Issue**:
- Cannot represent "this hash exists at 3 locations"
- Cannot query "give me all locations for this hash"
- Loses information

### 3. Location → Hash Ambiguity
**Problem**: `locations` table can have multiple rows for same location

**Scenarios**:
- Same location in different branches
- Same location deprecated and recreated
- Same location moved (deprecated_at set on old, new row created)

**Current**: `ORDER BY created_at DESC LIMIT 1` picks newest

**Issue**: No way to query history or see all versions

### 4. Hash Collision Handling
**Problem**: What if two different types actually hash to same value?

**Current schema**: Primary key on `id` in `package_types`

**Behavior**: Later INSERT would fail (PK violation)

**Question**:
- Is this desired (prevent collisions)?
- Or should we store both somehow?
- Or is collision impossible (SHA256)?

### 5. Metadata Storage Unclear
**Problem**: Where does metadata live?

**Currently**:
- Description, deprecation stored in PT.PackageType
- Serialized into `pt_def` blob
- Part of the hash? (Depends on normalization)

**Questions**:
- Should metadata be separate table?
- One metadata per hash, or per location?
- Can different locations have different descriptions?

### 6. Branch Isolation
**Problem**: Branch handling is ad-hoc

**Current**:
- Ops can have `branch_id`
- Locations can have `branch_id`
- Queries filter by branch_id

**Issues**:
- What happens when branch merges?
- How to prevent conflicts?
- How to diff branches?

### 7. Search Not Hash-Aware
**Problem**: Search returns `LocatedItem<T>` which has entity and location

**But**: Doesn't expose hash

**Issue**: Can't tell if two search results are the same content at different locations

### 8. No Concept ID
**Problem**: No stable identity for "the concept of Option"

**Currently**: Hash is the only ID

**But**:
- If implementation changes, hash changes
- New hash, but conceptually still "Option"
- How to track evolution?
- How to deprecate/replace?

**Possible need**: Concept ID (stable) vs Implementation Hash (changes)

## Requirements for Ref-by-Hash

### R1: Multiple Locations per Hash
API should support one-to-many hash→locations:

```fsharp
type PackageManager = {
  ...existing...

  // NEW: Get ALL locations for a hash
  getTypeLocations : Hash -> Ply<List<PackageLocation>>
  getValueLocations : Hash -> Ply<List<PackageLocation>>
  getFnLocations : Hash -> Ply<List<PackageLocation>>

  // NEW: Get preferred/canonical location
  getPreferredTypeLocation : BranchIDOpt * Hash -> Ply<Option<PackageLocation>>
  getPreferredValueLocation : BranchIDOpt * Hash -> Ply<Option<PackageLocation>>
  getPreferredFnLocation : BranchIDOpt * Hash -> Ply<Option<PackageLocation>>
}
```

### R2: Explicit Location vs Hash Separation
Clarify the mappings:

```fsharp
// Current (conflated)
findType : Location -> Option<Hash>
getType : Hash -> Option<PackageType>

// Clearer
type LocationMapping = {
  location : PackageLocation
  hash : Hash
  branchId : Option<BranchID>
  createdAt : Instant
  deprecatedAt : Option<Instant>
}

findTypeMapping : BranchIDOpt * PackageLocation -> Ply<Option<LocationMapping>>
findTypeHash : BranchIDOpt * PackageLocation -> Ply<Option<Hash>>  // Shortcut

getTypeContent : Hash -> Ply<Option<PackageType>>
getTypeLocations : Hash -> Ply<List<LocationMapping>>
```

### R3: Concept Identity Layer
Add concept IDs above hashes:

```fsharp
type ConceptID = Guid  // Stable forever

type ConceptMapping = {
  conceptId : ConceptID
  currentHash : Hash
  allHashes : List<Hash>  // Historical
  preferredLocation : PackageLocation
}

type PackageManager = {
  ...existing...

  // NEW: Concept layer
  findConcept : PackageLocation -> Ply<Option<ConceptID>>
  getConceptMapping : ConceptID -> Ply<Option<ConceptMapping>>
  getConceptHistory : ConceptID -> Ply<List<Hash>>  // Evolution over time
}
```

**Use case**: "The Result type" has concept ID X, but has had hashes A, B, C over time as implementation evolved.

### R4: Metadata Management
Separate metadata from content:

```fsharp
type Metadata = {
  description : string
  deprecated : Deprecation<...>
  author : Option<string>
  since : Option<Version>
  tags : List<string>
}

type PackageManager = {
  ...existing...

  // NEW: Metadata
  getTypeMetadata : Hash -> Ply<Option<Metadata>>
  getValueMetadata : Hash -> Ply<Option<Metadata>>
  getFnMetadata : Hash -> Ply<Option<Metadata>>

  setTypeMetadata : Hash * Metadata -> Ply<unit>
  // etc.
}
```

**Database**: Separate `metadata` table:
- `item_hash` (foreign key)
- `item_type` ('type' | 'value' | 'fn')
- `description` (text)
- `deprecated` (json?)
- ...

### R5: Branch Operations
Explicit branch management:

```fsharp
type BranchInfo = {
  id : BranchID
  name : string
  parentBranch : Option<BranchID>
  createdAt : Instant
  mergedAt : Option<Instant>
}

type PackageManager = {
  ...existing...

  // NEW: Branch operations
  createBranch : BranchID * name : string * parent : Option<BranchID> -> Ply<unit>
  mergeBranch : BranchID -> Ply<unit>
  deleteBranch : BranchID -> Ply<unit>

  // NEW: Branch queries
  getBranchInfo : BranchID -> Ply<Option<BranchInfo>>
  listBranches : unit -> Ply<List<BranchInfo>>

  // NEW: Diff
  diffBranches : BranchID * BranchID -> Ply<BranchDiff>
}
```

### R6: Search Enhancements
Include hash in search results:

```fsharp
type LocatedItem<'T> = {
  entity : 'T
  hash : Hash  // NEW
  location : PackageLocation
}

type Search.SearchResults = {
  submodules : List<List<string>>
  types : List<LocatedItem<PackageType.PackageType>>
  values : List<LocatedItem<PackageValue.PackageValue>>
  fns : List<LocatedItem<PackageFn.PackageFn>>
}
```

### R7: History and Versioning
Track evolution:

```fsharp
type VersionHistory = {
  hash : Hash
  previousHash : Option<Hash>
  createdAt : Instant
  changedBy : Option<string>
  changeDescription : Option<string>
}

type PackageManager = {
  ...existing...

  // NEW: History
  getTypeHistory : ConceptID -> Ply<List<VersionHistory>>
  getValueHistory : ConceptID -> Ply<List<VersionHistory>>
  getFnHistory : ConceptID -> Ply<List<VersionHistory>>
}
```

## Proposed Changes

### 1. Enhanced PackageManager Type
```fsharp
type PackageManager = {
  // === Basic Operations ===

  // Location → Hash
  findType : BranchIDOpt * PackageLocation -> Ply<Option<Hash>>
  findValue : BranchIDOpt * PackageLocation -> Ply<Option<Hash>>
  findFn : BranchIDOpt * PackageLocation -> Ply<Option<Hash>>

  // Hash → Content
  getType : Hash -> Ply<Option<PackageType>>
  getValue : Hash -> Ply<Option<PackageValue>>
  getFn : Hash -> Ply<Option<PackageFn>>

  // === Location Management ===

  // Hash → All Locations
  getTypeLocations : Hash -> Ply<List<PackageLocation>>
  getValueLocations : Hash -> Ply<List<PackageLocation>>
  getFnLocations : Hash -> Ply<List<PackageLocation>>

  // Hash → Preferred Location (for a branch)
  getTypeLocation : BranchIDOpt * Hash -> Ply<Option<PackageLocation>>
  getValueLocation : BranchIDOpt * Hash -> Ply<Option<PackageLocation>>
  getFnLocation : BranchIDOpt * Hash -> Ply<Option<PackageLocation>>

  // === Metadata ===

  getTypeMetadata : Hash -> Ply<Option<Metadata>>
  getValueMetadata : Hash -> Ply<Option<Metadata>>
  getFnMetadata : Hash -> Ply<Option<Metadata>>

  // === Search ===

  search : BranchIDOpt * Search.SearchQuery -> Ply<Search.SearchResults>

  // === Operations ===

  applyOps : BranchIDOpt * List<PackageOp> -> Ply<unit>

  // === Initialization ===

  init : Ply<unit>
}
```

### 2. Enhanced Database Schema
```sql
-- Core content tables (unchanged)
CREATE TABLE package_types (
  id BLOB PRIMARY KEY,  -- Hash as bytes
  pt_def BLOB NOT NULL,
  rt_def BLOB NOT NULL
);

CREATE TABLE package_functions (
  id BLOB PRIMARY KEY,
  pt_def BLOB NOT NULL,
  rt_instrs BLOB NOT NULL
);

CREATE TABLE package_values (
  id BLOB PRIMARY KEY,
  pt_def BLOB NOT NULL,
  rt_dval BLOB NOT NULL
);

-- Location mappings (enhanced)
CREATE TABLE locations (
  location_id TEXT PRIMARY KEY,  -- Unique per location assignment
  item_hash BLOB NOT NULL,  -- References package_types/functions/values
  branch_id TEXT,  -- NULL for main branch
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,  -- Dot-separated
  name TEXT NOT NULL,
  item_type TEXT NOT NULL,  -- 'type' | 'fn' | 'value'
  created_at TEXT NOT NULL,
  deprecated_at TEXT,  -- NULL if active

  -- Indexes
  FOREIGN KEY (item_hash, item_type) REFERENCES ... (conceptual)
);

CREATE INDEX idx_locations_lookup ON locations(owner, modules, name, item_type, deprecated_at);
CREATE INDEX idx_locations_reverse ON locations(item_hash, item_type, deprecated_at);

-- Metadata (NEW)
CREATE TABLE metadata (
  item_hash BLOB NOT NULL,
  item_type TEXT NOT NULL,
  description TEXT,
  deprecated_json TEXT,  -- JSON encoding of Deprecation
  author TEXT,
  since TEXT,  -- Version string
  tags TEXT,  -- JSON array

  PRIMARY KEY (item_hash, item_type)
);

-- Concept IDs (FUTURE - not MVP)
CREATE TABLE concepts (
  concept_id TEXT PRIMARY KEY,
  item_type TEXT NOT NULL,
  current_hash BLOB NOT NULL,
  preferred_location TEXT NOT NULL  -- owner.modules.name
);

CREATE TABLE concept_history (
  concept_id TEXT NOT NULL,
  hash BLOB NOT NULL,
  valid_from TEXT NOT NULL,
  valid_to TEXT,  -- NULL if current
  FOREIGN KEY (concept_id) REFERENCES concepts(concept_id)
);
```

### 3. Implementation Enhancements
```fsharp
// PT/SQL/Types.fs
module Types =
  // Get all locations for a hash
  let getLocations (id : Hash) : Ply<List<PT.PackageLocation>> =
    uply {
      return!
        Sql.query
          """
          SELECT owner, modules, name
          FROM locations
          WHERE item_hash = @hash
            AND item_type = 'type'
            AND deprecated_at IS NULL
          ORDER BY created_at DESC
          """
        |> Sql.parameters [ "hash", Sql.bytes (Hash.toBytes id) ]
        |> Sql.executeAsync (fun read ->
          { owner = read.string "owner"
            modules = (read.string "modules").Split('.') |> Array.toList
            name = read.string "name" })
    }

  // Get metadata
  let getMetadata (id : Hash) : Ply<Option<PT.Metadata>> =
    uply {
      return!
        Sql.query
          """
          SELECT description, deprecated_json, author, since, tags
          FROM metadata
          WHERE item_hash = @hash AND item_type = 'type'
          """
        |> Sql.parameters [ "hash", Sql.bytes (Hash.toBytes id) ]
        |> Sql.executeRowOptionAsync (fun read ->
          { description = read.string "description"
            deprecated = deserializeDeprecation (read.string "deprecated_json")
            author = read.stringOrNone "author"
            since = read.stringOrNone "since"
            tags = deserializeTags (read.string "tags") })
    }
```

### 4. Op Playback Updates
```fsharp
// PT/SQL/OpPlayback.fs
let applyOp (branchID : Option<PT.BranchID>) (op : PT.PackageOp) : Task<unit> =
  task {
    match op with
    | PT.PackageOp.AddType typ ->
      // Store content
      do! applyAddType typ
      // Store metadata separately
      do! applyMetadata typ.id typ.description typ.deprecated

    | PT.PackageOp.SetTypeName(id, loc) ->
      do! applySetName branchID id loc "type"

    // ...
  }

let private applyMetadata
  (hash : Hash)
  (description : string)
  (deprecated : PT.Deprecation<...>)
  : Task<unit> =
  Sql.query
    """
    INSERT OR REPLACE INTO metadata (item_hash, item_type, description, deprecated_json)
    VALUES (@hash, @type, @description, @deprecated)
    """
  |> Sql.parameters [
    "hash", Sql.bytes (Hash.toBytes hash)
    "type", Sql.string "type"
    "description", Sql.string description
    "deprecated", Sql.string (serializeDeprecation deprecated)
  ]
  |> Sql.executeStatementAsync
```

### 5. Search Updates
```fsharp
module Search =
  type SearchResults = {
    submodules : List<List<string>>
    types : List<LocatedItem<PackageType.PackageType>>
    values : List<LocatedItem<PackageValue.PackageValue>>
    fns : List<LocatedItem<PackageFn.PackageFn>>
  }

  and LocatedItem<'T> = {
    entity : 'T
    hash : Hash  // NEW
    location : PackageLocation
  }

  let search
    (branchID : Option<PT.BranchID>)
    (query : PT.Search.SearchQuery)
    : Ply<PT.Search.SearchResults> =
    // Update queries to include item_hash in SELECT
    // Populate hash field in LocatedItem
    ...
```

## Code Impacts

### Files to Change

**Immediate**:
- `LibExecution/ProgramTypes.fs`:
  - Update PackageManager type with new methods
  - Add Metadata type
  - Update LocatedItem to include hash

- `LibPackageManager/PT/SQL/*.fs`:
  - Implement getLocations methods
  - Implement getMetadata methods
  - Update search to include hashes

- Database migration:
  - Add metadata table
  - Update locations table if needed
  - Create indexes

**Second Order**:
- All PM usage:
  - Update to use new methods where appropriate
  - Handle multiple locations where relevant
  - Use metadata API instead of accessing PT fields directly

**Testing**:
- Test multiple locations for same hash
- Test metadata storage/retrieval
- Test search includes hashes
- Test location lookups

### Migration Strategy

**Phase 1: Add new methods (backwards compatible)**
- Add getLocations, getMetadata to PM interface
- Implement in SQL and InMemory
- Old code continues using existing methods

**Phase 2: Migrate callers**
- Update code to use new methods
- Deprecate old patterns
- Test thoroughly

**Phase 3: Schema migration**
- Add metadata table
- Migrate metadata from pt_def blobs
- Update indexes

**Phase 4: Clean up**
- Remove deprecated methods
- Simplify schema if possible

## Open Questions

### Q1: Preferred Location Algorithm
**Question**: How to choose "preferred" location when multiple exist?

**Options**:
- Newest (ORDER BY created_at DESC)
- Owner preference (Darklang > user)
- Shortest name
- Explicit preference column

**Recommendation**: Newest for now, explicit preference column later

### Q2: Location History
**Question**: Should we keep deprecated locations forever?

**Current**: Keep with deprecated_at set

**Pros**:
- Historical record
- Can undo moves
- Audit trail

**Cons**:
- Table grows forever
- Clutters queries

**Recommendation**: Keep forever, add indexes for perf

### Q3: Metadata Conflicts
**Question**: What if same hash gets different metadata?

**Scenario**: Hash X is at locations A and B. User sets description at A. What about B?

**Options**:
- Metadata tied to hash (shared)
- Metadata tied to location (separate)
- Metadata tied to hash with location overrides

**Recommendation**: Metadata tied to hash (shared), with optional location-specific overrides later

### Q4: Concept ID Timing
**Question**: When to implement concept IDs?

**Options**:
- Now (MVP)
- Later (when needed)
- Never (not needed)

**Recommendation**: Later. Start with just hashes, add concepts when we need to track evolution.

### Q5: Branch Merge Strategy
**Question**: How to handle conflicts when merging branches?

**Scenario**: Branch A and B both have Darklang.Stdlib.Option but different implementations (different hashes).

**Options**:
- Error (require manual resolution)
- Last write wins
- Keep both, let user choose
- Semantic merge (if compatible)

**Recommendation**: Error for MVP. Require manual resolution.

### Q6: Search Performance
**Question**: Will including hashes in search slow it down?

**Answer**: Unlikely - hash is already in locations table

**Mitigation**: Index on item_hash if needed

### Q7: Metadata Size Limits
**Question**: Should we limit description/metadata size?

**Concerns**:
- Large descriptions bloat database
- Could be abused

**Recommendation**: Soft limit (1KB warning, 10KB hard limit) with good error messages

## Summary

**Key Insights**:
1. Need multiple locations per hash (one-to-many)
2. Metadata should be separate from content
3. Concept IDs might be needed but not for MVP
4. Branch management needs explicit APIs
5. Search should expose hashes

**Biggest Risks**:
1. Schema migration complexity
2. Performance of multi-location queries
3. Branch merge conflicts
4. Metadata consistency
5. Migration path for existing data

**Next Steps**:
1. Update PT.PackageManager type signature
2. Implement getLocations methods
3. Add metadata table and APIs
4. Update search to include hashes
5. Test on real data
6. Document new patterns
