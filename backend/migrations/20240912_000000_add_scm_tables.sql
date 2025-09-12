/*
Add Source Control Management (SCM) tables with content-addressable architecture.

This migration adds the foundation for hash-based source control:
- Content storage: immutable, append-only, keyed by content hash
- Name resolution: mutable pointers from names to content hashes
- Patches: collections of operations that form coherent changes
- Sessions: persistent development contexts
- True separation of content from location
*/

-- Content storage (append-only, keyed by content hash)
CREATE TABLE IF NOT EXISTS
package_content_v0
( content_hash TEXT PRIMARY KEY -- Content-addressable key
, content_type TEXT NOT NULL CHECK (content_type IN ('function', 'type', 'value'))
, content_data BLOB NOT NULL -- Serialized PackageFn/PackageType/PackageValue
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_content_type ON package_content_v0(content_type);
CREATE INDEX IF NOT EXISTS idx_content_created_at ON package_content_v0(created_at DESC);

-- Name resolution (mutable pointers to content)
CREATE TABLE IF NOT EXISTS
package_names_v0
( owner TEXT NOT NULL
, modules TEXT NOT NULL -- JSON array of module path
, name TEXT NOT NULL
, current_hash TEXT NOT NULL -- Points to content_hash
, visibility TEXT NOT NULL DEFAULT 'public' CHECK (visibility IN ('public', 'internal', 'private'))
, description TEXT
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
, updated_at TEXT NOT NULL DEFAULT (datetime('now'))
, PRIMARY KEY (owner, modules, name)
, FOREIGN KEY (current_hash) REFERENCES package_content_v0(content_hash)
);

CREATE INDEX IF NOT EXISTS idx_names_hash ON package_names_v0(current_hash);
CREATE INDEX IF NOT EXISTS idx_names_owner ON package_names_v0(owner);
CREATE INDEX IF NOT EXISTS idx_names_updated_at ON package_names_v0(updated_at DESC);

-- Patches: collections of related operations
CREATE TABLE IF NOT EXISTS
patches_v0
( id TEXT PRIMARY KEY
, parent_patches TEXT -- JSON array of parent patch UUIDs
, name TEXT
, description TEXT NOT NULL
, author TEXT NOT NULL  
, intent TEXT -- JSON array of strings (TODOs, goals, etc.)
, tags TEXT -- JSON array of strings
, status TEXT NOT NULL CHECK (status IN ('draft', 'validated', 'applied', 'reverted'))
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
, updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_patches_status ON patches_v0(status);
CREATE INDEX IF NOT EXISTS idx_patches_author ON patches_v0(author);
CREATE INDEX IF NOT EXISTS idx_patches_created_at ON patches_v0(created_at DESC);

-- Operations within patches  
CREATE TABLE IF NOT EXISTS
patch_ops_v0
( id TEXT PRIMARY KEY
, patch_id TEXT NOT NULL
, sequence_num INTEGER NOT NULL -- Order within patch
, op_type TEXT NOT NULL -- Discriminator for Op union type
, op_data BLOB NOT NULL -- Serialized Op data (PT format)
, validation_status TEXT CHECK (validation_status IN ('pending', 'valid', 'invalid'))
, validation_errors TEXT -- JSON array of error messages
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
, FOREIGN KEY (patch_id) REFERENCES patches_v0(id) ON DELETE CASCADE
, UNIQUE (patch_id, sequence_num)
);

CREATE INDEX IF NOT EXISTS idx_patch_ops_patch_id ON patch_ops_v0(patch_id);
CREATE INDEX IF NOT EXISTS idx_patch_ops_type ON patch_ops_v0(op_type);
CREATE INDEX IF NOT EXISTS idx_patch_ops_validation ON patch_ops_v0(validation_status);

-- Development sessions  
CREATE TABLE IF NOT EXISTS
sessions_v0
( id TEXT PRIMARY KEY
, name TEXT NOT NULL
, base_patch_id TEXT NOT NULL
, current_patch_id TEXT -- The patch being worked on (nullable)
, config TEXT NOT NULL -- JSON SessionConfig
, status TEXT NOT NULL CHECK (status IN ('active', 'paused', 'archived'))
, last_activity TEXT NOT NULL DEFAULT (datetime('now'))
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
, FOREIGN KEY (base_patch_id) REFERENCES patches_v0(id)
, FOREIGN KEY (current_patch_id) REFERENCES patches_v0(id)
);

CREATE INDEX IF NOT EXISTS idx_sessions_status ON sessions_v0(status);
CREATE INDEX IF NOT EXISTS idx_sessions_activity ON sessions_v0(last_activity DESC);
CREATE INDEX IF NOT EXISTS idx_sessions_base_patch ON sessions_v0(base_patch_id);

-- Patch dependencies and relationships
CREATE TABLE IF NOT EXISTS
patch_dependencies_v0  
( patch_id TEXT NOT NULL
, depends_on_patch_id TEXT NOT NULL
, dependency_type TEXT NOT NULL CHECK (dependency_type IN ('parent', 'requires', 'conflicts'))
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
, PRIMARY KEY (patch_id, depends_on_patch_id)
, FOREIGN KEY (patch_id) REFERENCES patches_v0(id) ON DELETE CASCADE
, FOREIGN KEY (depends_on_patch_id) REFERENCES patches_v0(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_patch_deps_depends_on ON patch_dependencies_v0(depends_on_patch_id);
CREATE INDEX IF NOT EXISTS idx_patch_deps_type ON patch_dependencies_v0(dependency_type);

-- Note: Aliases are handled by having multiple rows in package_names_v0 
-- pointing to the same content_hash, so no separate aliases table needed.

-- Track which patches affect which package items (for impact analysis)
CREATE TABLE IF NOT EXISTS
patch_item_impact_v0
( patch_id TEXT NOT NULL
, item_hash TEXT NOT NULL -- Content hash affected
, item_type TEXT NOT NULL CHECK (item_type IN ('function', 'type', 'value'))
, impact_type TEXT NOT NULL CHECK (impact_type IN ('creates', 'updates_pointer', 'moves', 'aliases'))
, location_before TEXT -- JSON PackageLocation before change
, location_after TEXT -- JSON PackageLocation after change
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
, FOREIGN KEY (patch_id) REFERENCES patches_v0(id) ON DELETE CASCADE
, FOREIGN KEY (item_hash) REFERENCES package_content_v0(content_hash)
, PRIMARY KEY (patch_id, item_hash, impact_type)
);

CREATE INDEX IF NOT EXISTS idx_impact_hash ON patch_item_impact_v0(item_hash);
CREATE INDEX IF NOT EXISTS idx_impact_type ON patch_item_impact_v0(impact_type);

/*
Create initial "main" patch for existing data
*/
INSERT OR IGNORE INTO patches_v0 
  (id, parent_patches, name, description, author, intent, tags, status)
VALUES 
  ('00000000-0000-0000-0000-000000000000', '[]', 'main', 'Initial patch containing existing package data', 'system', '[]', '["initial", "main"]', 'applied');