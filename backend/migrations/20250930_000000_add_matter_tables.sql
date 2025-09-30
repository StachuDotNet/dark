-- ===================================
-- Package Management System Tables
-- ===================================

-- Content storage (immutable, content-addressed)
CREATE TABLE IF NOT EXISTS matter_content_v0 (
  hash TEXT PRIMARY KEY,
  content_type TEXT NOT NULL CHECK (content_type IN ('function', 'type', 'value')),
  content BLOB NOT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_matter_content_type ON matter_content_v0(content_type);

-- Name pointers (mutable pointers to content)
CREATE TABLE IF NOT EXISTS matter_names_v0 (
  id UUID PRIMARY KEY DEFAULT (uuid()),
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,
  name TEXT NOT NULL,
  hash TEXT NOT NULL REFERENCES matter_content_v0(hash),
  deprecated BOOLEAN NOT NULL DEFAULT FALSE,
  deprecation_reason TEXT,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE(owner, modules, name)
);

CREATE INDEX idx_matter_names_location ON matter_names_v0(owner, modules, name);
CREATE INDEX idx_matter_names_hash ON matter_names_v0(hash);

-- Instances
CREATE TABLE IF NOT EXISTS matter_instances_v0 (
  id UUID PRIMARY KEY DEFAULT (uuid()),
  name TEXT NOT NULL UNIQUE,
  instance_type TEXT NOT NULL CHECK (instance_type IN ('LocalCLI', 'HttpServer')),
  last_sync_at TIMESTAMP,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Sessions
CREATE TABLE IF NOT EXISTS matter_sessions_v0 (
  id UUID PRIMARY KEY DEFAULT (uuid()),
  name TEXT NOT NULL,
  intent TEXT NOT NULL,
  owner TEXT NOT NULL,
  current_patch_id UUID,
  state TEXT NOT NULL CHECK (state IN ('Active', 'Suspended', 'Completed')),
  workspace_state BLOB,  -- Serialized workspace state
  started_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  last_active_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_matter_sessions_owner ON matter_sessions_v0(owner);
CREATE INDEX idx_matter_sessions_state ON matter_sessions_v0(state);

-- Patches
CREATE TABLE IF NOT EXISTS matter_patches_v0 (
  id UUID PRIMARY KEY DEFAULT (uuid()),
  intent TEXT NOT NULL,
  author TEXT NOT NULL,
  status TEXT NOT NULL CHECK (status IN ('Draft', 'Ready', 'Applied', 'Rejected')),
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  metadata BLOB  -- Serialized patch metadata (todos, tags, etc)
);

CREATE INDEX idx_matter_patches_author ON matter_patches_v0(author);
CREATE INDEX idx_matter_patches_status ON matter_patches_v0(status);

-- Operations
CREATE TABLE IF NOT EXISTS matter_ops_v0 (
  id UUID PRIMARY KEY DEFAULT (uuid()),
  patch_id UUID NOT NULL REFERENCES matter_patches_v0(id),
  sequence_num INTEGER NOT NULL,
  op_type TEXT NOT NULL,
  op_data BLOB NOT NULL,  -- Serialized Op.T
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE(patch_id, sequence_num)
);

CREATE INDEX idx_matter_ops_patch ON matter_ops_v0(patch_id);

-- Patch dependencies
CREATE TABLE IF NOT EXISTS matter_patch_dependencies_v0 (
  patch_id UUID NOT NULL REFERENCES matter_patches_v0(id),
  depends_on_patch_id UUID NOT NULL REFERENCES matter_patches_v0(id),
  PRIMARY KEY (patch_id, depends_on_patch_id)
);

-- Session patches (which patches belong to which session)
CREATE TABLE IF NOT EXISTS matter_session_patches_v0 (
  session_id UUID NOT NULL REFERENCES matter_sessions_v0(id),
  patch_id UUID NOT NULL REFERENCES matter_patches_v0(id),
  added_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (session_id, patch_id)
);

CREATE INDEX idx_matter_session_patches_session ON matter_session_patches_v0(session_id);
CREATE INDEX idx_matter_session_patches_patch ON matter_session_patches_v0(patch_id);