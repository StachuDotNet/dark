# Database Design Summary

## Core Concept
Separate immutable content (functions/types/values) from mutable name pointers. Names can point to different content over time, but content never changes.

## Schema Design Options

### Option A: Simple Content-Addressable
```sql
-- Immutable content
CREATE TABLE content_store_v0 (
  content_hash TEXT PRIMARY KEY,
  content_type TEXT NOT NULL,  -- 'function', 'type', 'value'
  content_data BLOB NOT NULL,
  created_at TEXT NOT NULL
);

-- Mutable name pointers
CREATE TABLE name_pointers_v0 (
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,        -- JSON: ["MyApp", "User"]
  name TEXT NOT NULL,
  content_hash TEXT NOT NULL,
  visibility TEXT NOT NULL DEFAULT 'public',
  deprecated BOOLEAN NOT NULL DEFAULT 0,
  PRIMARY KEY (owner, modules, name),
  FOREIGN KEY (content_hash) REFERENCES content_store_v0(content_hash)
);
```

**Pros**: Simple, clean separation
**Cons**: No history tracking, can't query "what names has this content had?"

### Option B: Bidirectional Name-Item Relationships
```sql
-- Names with stable IDs
CREATE TABLE names_v0 (
  name_id INTEGER PRIMARY KEY,
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,
  name TEXT NOT NULL,
  current_item_hash TEXT NOT NULL,
  name_kind TEXT NOT NULL,
  visibility TEXT NOT NULL DEFAULT 'public',
  created_at TEXT NOT NULL,
  created_by INTEGER NOT NULL,
  UNIQUE (owner, modules, name)
);

-- Items remember their original name
CREATE TABLE items_v0 (
  item_hash TEXT PRIMARY KEY,
  item_kind TEXT NOT NULL,
  content_data BLOB NOT NULL,
  original_name_id INTEGER NOT NULL,
  original_name_text TEXT NOT NULL,  -- Denormalized for search
  created_at TEXT NOT NULL,
  created_by INTEGER NOT NULL,
  deprecated_at TEXT,
  deprecation_reason TEXT,
  FOREIGN KEY (original_name_id) REFERENCES names_v0(name_id)
);

-- Complete history of name->item mappings
CREATE TABLE name_history_v0 (
  history_id INTEGER PRIMARY KEY,
  name_id INTEGER NOT NULL,
  item_hash TEXT NOT NULL,
  active_from TEXT NOT NULL,
  active_to TEXT,  -- NULL = current
  change_reason TEXT NOT NULL,  -- 'initial', 'repoint', 'move', 'deprecate'
  changed_by INTEGER NOT NULL,
  operation_id TEXT
);

-- Track all relationships between items and names
CREATE TABLE item_associations_v0 (
  association_id INTEGER PRIMARY KEY,
  item_hash TEXT NOT NULL,
  name_id INTEGER NOT NULL,
  association_type TEXT NOT NULL,  -- 'original', 'pointed_to', 'moved_from'
  associated_from TEXT NOT NULL,
  associated_to TEXT
);
```

**Pros**: Full history, can query both directions, tracks item movement
**Cons**: More complex, more storage

## Structured Type Representation

Types follow ProgramTypes.fs structure and are stored as JSON:

### Built-in Types
```json
{"kind": "TString"}
{"kind": "TInt64"}
{"kind": "TBool"}
```

### List Types
```json
{
  "kind": "TList",
  "item": {
    "kind": "TCustomType",
    "name": {"Package": "user-type-uuid"},
    "typeArgs": []
  }
}
```

### Custom Types with Type Arguments
```json
{
  "kind": "TCustomType",
  "name": {"Package": "result-type-uuid"},
  "typeArgs": [
    {
      "kind": "TCustomType",
      "name": {"Package": "user-type-uuid"},
      "typeArgs": []
    },
    {
      "kind": "TString"
    }
  ]
}
```

### Function Types
```json
{
  "kind": "TFn",
  "arguments": [
    {"kind": "TString"},
    {"kind": "TInt64"}
  ],
  "ret": {"kind": "TBool"}
}
```

## Searchable Metadata Tables

```sql
CREATE TABLE searchable_functions_v0 (
  item_hash TEXT PRIMARY KEY,
  name_id INTEGER NOT NULL,
  current_name TEXT NOT NULL,
  original_name TEXT NOT NULL,
  all_names TEXT NOT NULL,  -- JSON: all names this has had
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,
  parameters TEXT NOT NULL,  -- JSON: [{"name": "x", "type": {...}}]
  return_type TEXT NOT NULL,  -- JSON: TypeReference
  dependencies_types TEXT,  -- JSON: ["uuid1", "uuid2"]
  dependencies_fns TEXT,  -- JSON: ["uuid3", "uuid4"]
  is_current BOOLEAN NOT NULL,
  is_deprecated BOOLEAN NOT NULL,
  visibility TEXT NOT NULL
);

CREATE TABLE searchable_types_v0 (
  item_hash TEXT PRIMARY KEY,
  name_id INTEGER NOT NULL,
  current_name TEXT NOT NULL,
  original_name TEXT NOT NULL,
  all_names TEXT NOT NULL,
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,
  type_kind TEXT NOT NULL,  -- 'record', 'enum', 'alias'
  fields TEXT,  -- JSON: record fields
  variants TEXT,  -- JSON: enum variants
  dependencies TEXT,  -- JSON: type UUIDs this depends on
  is_current BOOLEAN NOT NULL,
  is_deprecated BOOLEAN NOT NULL,
  visibility TEXT NOT NULL
);

CREATE TABLE searchable_values_v0 (
  item_hash TEXT PRIMARY KEY,
  name_id INTEGER NOT NULL,
  current_name TEXT NOT NULL,
  original_name TEXT NOT NULL,
  all_names TEXT NOT NULL,
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,
  value_type TEXT NOT NULL,  -- JSON: TypeReference
  dependencies TEXT,
  is_current BOOLEAN NOT NULL,
  is_deprecated BOOLEAN NOT NULL,
  visibility TEXT NOT NULL
);
```

## Supporting Tables

```sql
CREATE TABLE users_v0 (
  user_id INTEGER PRIMARY KEY,
  username TEXT NOT NULL,
  auth_token TEXT
);

CREATE TABLE patches_v0 (
  id TEXT PRIMARY KEY,
  parent_patches TEXT,  -- JSON array
  description TEXT NOT NULL,
  author TEXT NOT NULL,
  intent TEXT,  -- JSON: goals, TODOs
  tags TEXT,  -- JSON array
  status TEXT NOT NULL,  -- 'draft', 'validated', 'applied', 'reverted'
  created_at TEXT NOT NULL
);

CREATE TABLE patch_ops_v0 (
  patch_id TEXT NOT NULL,
  op_index INTEGER NOT NULL,
  op_type TEXT NOT NULL,
  op_data BLOB NOT NULL,
  PRIMARY KEY (patch_id, op_index),
  FOREIGN KEY (patch_id) REFERENCES patches_v0(id)
);

CREATE TABLE sessions_v0 (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  owner TEXT NOT NULL,
  current_patch TEXT,
  pinned_patches TEXT,  -- JSON array
  workspace_state TEXT NOT NULL,
  environment_vars TEXT,
  transferable BOOLEAN NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL,
  last_activity TEXT NOT NULL
);

CREATE TABLE instances_v0 (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  instance_type TEXT NOT NULL,  -- 'local_cli', 'central_server', 'team_server'
  location_type TEXT NOT NULL,  -- 'local', 'remote'
  location_data TEXT NOT NULL,
  capabilities TEXT NOT NULL,  -- JSON array
  sync_mode TEXT NOT NULL,  -- 'manual', 'automatic', 'hybrid'
  last_sync_at TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE sync_operations_v0 (
  id TEXT PRIMARY KEY,
  instance_id TEXT NOT NULL,
  operation_type TEXT NOT NULL,  -- 'pull', 'push', 'merge'
  patches_affected TEXT,  -- JSON array
  success BOOLEAN NOT NULL,
  error_message TEXT,
  started_at TEXT NOT NULL,
  completed_at TEXT,
  FOREIGN KEY (instance_id) REFERENCES instances_v0(id)
);
```

## Key Operations

### AddContent
1. Hash content, store in items_v0
2. Create/update name in names_v0
3. Record in name_history_v0 and item_associations_v0
4. Extract metadata to searchable_* tables

### RepointName
1. Update current_item_hash in names_v0
2. Close old history entry, create new one
3. Update item_associations_v0
4. Mark old item as not current in searchable tables

### MoveItem
1. Create new name entry
2. Update item associations (item now has multiple names)
3. Record in history
4. Update searchable metadata

### DeprecateName
1. Set deprecated_at in items_v0
2. Update searchable tables
3. Record in history

## Powerful Queries

### Type-Based Search
```sql
-- Find functions returning Result<User, String>
SELECT * FROM searchable_functions_v0
WHERE json_extract(return_type, '$.kind') = 'TCustomType'
  AND json_extract(return_type, '$.name.Package') = 'result-uuid'
  AND json_extract(return_type, '$.typeArgs[0].name.Package') = 'user-uuid';

-- Find all functions using User type anywhere
SELECT * FROM searchable_functions_v0
WHERE dependencies_types LIKE '%"user-uuid"%';

-- Find functions taking lists
SELECT * FROM searchable_functions_v0
WHERE parameters LIKE '%"kind":"TList"%';
```

### History Queries
```sql
-- What has MyApp.User.validate pointed to over time?
SELECT h.*, i.original_name_text
FROM name_history_v0 h
JOIN items_v0 i ON h.item_hash = i.item_hash
WHERE h.name_id = (SELECT name_id FROM names_v0
                   WHERE owner = 'MyApp'
                   AND modules = '["User"]'
                   AND name = 'validate')
ORDER BY h.active_from DESC;

-- What names has this content had?
SELECT n.owner, n.modules, n.name, ia.association_type
FROM item_associations_v0 ia
JOIN names_v0 n ON ia.name_id = n.name_id
WHERE ia.item_hash = 'hash123';

-- Find renamed/moved functions
SELECT current_name, original_name, all_names
FROM searchable_functions_v0
WHERE current_name != original_name;
```

## Migration from Current Schema

```sql
-- Populate items from existing tables
INSERT INTO items_v0 (item_hash, item_kind, content_data, original_name_id, original_name_text, ...)
SELECT
  compute_hash(pt_def),
  'function',
  pt_def,
  ...
FROM package_functions_v0;

-- Similar for types and values
-- Then populate names, history, and searchable tables
```