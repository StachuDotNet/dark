# Darklang SCM+PM Design Draft

## Core Concepts Overview

### Operations (Ops)
An Op represents any atomic change to the package manager state. All changes flow through Ops to ensure consistency and enable patch-based development.

### Patches
A Patch is a collection of Ops that forms a coherent unit of change. Patches can be:
- Created, validated, applied, and reverted
- Synced between instances
- Depend on other patches
- Contain metadata (author, description, intent, TODOs)

### Sessions 
A Session represents a development context where patches are created and applied. Sessions are:
- Persistent and stored in the database
- Syncable between machines/instances
- Associated with specific patches or branches
- Contain environment variables and configuration

## Op Taxonomy

Based on content-addressable architecture with append-only storage:

```fsharp
type PackageLocation = {
  owner: string
  modules: List<string>  
  name: string
}

type Op =
  // == Content Management (Append-Only) ==
  
  // -- Functions --
  | AddFunctionContent of 
    contentHash: string * 
    definition: PackageFn.PackageFn
  
  // -- Types --  
  | AddTypeContent of 
    contentHash: string * 
    definition: PackageType.PackageType
  
  // -- Values/Constants --
  | AddValueContent of 
    contentHash: string * 
    definition: PackageValue.PackageValue
  
  // == Name Management (Mutable Pointers) ==
  
  | CreateName of 
    location: PackageLocation * 
    initialHash: string
  
  | UpdateNamePointer of 
    location: PackageLocation * 
    newHash: string
  
  | MoveName of 
    fromLocation: PackageLocation * 
    toLocation: PackageLocation
  
  | DeleteName of location: PackageLocation
  
  // == Aliasing (Multiple Names → Same Content) ==
  
  | CreateAlias of 
    aliasLocation: PackageLocation * 
    targetHash: string
  
  // == Module/Namespace Management ==
  
  | CreateModule of 
    location: PackageLocation * 
    description: string
  
  // == Access Control & Metadata ==
  
  | SetVisibility of 
    location: PackageLocation * 
    visibility: Visibility // Public, Internal, Private
  
  | SetDescription of 
    location: PackageLocation * 
    description: string
  
  // == Migration Support ==
  
  | AddMigration of 
    fromHash: string * 
    toHash: string * 
    migrationExpr: Expr // Function that transforms old data to new format
  
  // == Branch/Patch Management Ops ==
  
  | CreatePatch of 
    id: uuid * 
    parentPatches: List<uuid> * 
    metadata: PatchMetadata
  
  | MergePatch of 
    patchId: uuid * 
    targetPatchId: uuid * 
    conflictResolutions: List<ConflictResolution>
  
  | RevertPatch of patchId: uuid
  
  // == Session Management Ops ==
  
  | CreateSession of 
    id: uuid * 
    name: string * 
    basePatch: uuid * 
    config: SessionConfig
  
  | UpdateSession of 
    id: uuid * 
    config: SessionConfig
  
  | AttachToPatch of 
    sessionId: uuid * 
    patchId: uuid
  
  | DeleteSession of id: uuid

// Supporting types
and EntityType = Function | Type | Value | Module
and Visibility = Public | Internal | Private

and PatchMetadata = {
  name: Option<string>
  description: string
  author: string
  intent: List<string> // TODOs, goals, etc.
  tags: List<string>
  createdAt: DateTime
  updatedAt: DateTime
}

and SessionConfig = {
  environmentVars: Map<string, string>
  workingDirectory: Option<string>
  preferences: Map<string, string>
}
```

## Database Schema Extensions

### New Tables for Content-Addressable SCM

```sql
-- Content storage (append-only, keyed by content hash)
CREATE TABLE package_content_v0 (
  content_hash TEXT PRIMARY KEY, -- Content-addressable key
  content_type TEXT NOT NULL CHECK (content_type IN ('function', 'type', 'value')),
  content_data BLOB NOT NULL, -- Serialized PackageFn/PackageType/PackageValue
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Name resolution (mutable pointers to content)
CREATE TABLE package_names_v0 (
  owner TEXT NOT NULL,
  modules TEXT NOT NULL, -- JSON array of module path
  name TEXT NOT NULL,
  current_hash TEXT NOT NULL, -- Points to content_hash
  visibility TEXT NOT NULL DEFAULT 'public' CHECK (visibility IN ('public', 'internal', 'private')),
  description TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now')),
  PRIMARY KEY (owner, modules, name),
  FOREIGN KEY (current_hash) REFERENCES package_content_v0(content_hash)
);

-- Patches: collections of related operations
CREATE TABLE patches_v0 (
  id TEXT PRIMARY KEY,
  parent_patches TEXT, -- JSON array of parent patch UUIDs
  name TEXT,
  description TEXT NOT NULL,
  author TEXT NOT NULL,
  intent TEXT, -- JSON array of strings
  tags TEXT, -- JSON array of strings
  status TEXT NOT NULL CHECK (status IN ('draft', 'validated', 'applied', 'reverted')),
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Operations within patches
CREATE TABLE patch_ops_v0 (
  id TEXT PRIMARY KEY,
  patch_id TEXT NOT NULL,
  sequence_num INTEGER NOT NULL, -- Order within patch
  op_type TEXT NOT NULL, -- Discriminator for Op union type
  op_data BLOB NOT NULL, -- Serialized Op data
  validation_status TEXT CHECK (validation_status IN ('pending', 'valid', 'invalid')),
  validation_errors TEXT, -- JSON array of error messages
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  FOREIGN KEY (patch_id) REFERENCES patches_v0(id),
  UNIQUE (patch_id, sequence_num)
);

-- Development sessions
CREATE TABLE sessions_v0 (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  base_patch_id TEXT NOT NULL,
  current_patch_id TEXT, -- The patch being worked on
  config TEXT NOT NULL, -- JSON SessionConfig
  status TEXT NOT NULL CHECK (status IN ('active', 'paused', 'archived')),
  last_activity TEXT NOT NULL DEFAULT (datetime('now')),
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  FOREIGN KEY (base_patch_id) REFERENCES patches_v0(id),
  FOREIGN KEY (current_patch_id) REFERENCES patches_v0(id)
);
```

## Key Design Decisions

### 1. True Content-Addressable Architecture
- Package content keyed by content hash (no artificial UUIDs)
- Immutable content storage - every version preserved forever
- Names are mutable pointers to immutable content
- Perfect deduplication and caching by content hash

### 2. Separation of Content from Location
- Package items exist independently of where they're named
- Easy moves/renames just update name pointers
- Same content can exist under multiple names (aliasing)
- Version history emerges from Op sequence, not DB structure

### 3. Append-Only Content, Mutable Names
- Content table is append-only (never UPDATE, only INSERT)
- Names table is mutable (UPDATE current_hash as needed)
- Version chains tracked through patch history, not explicit supersession
- Enables true immutability with practical name management

## Basic Developer Flows

### Flow 1: Create and Share a Simple Function

```bash
# Start a new session
dark session new --name "add-string-reverse" --base main

# Create a patch for this work
dark patch new --name "Add String.reverse function" 

# Create the function (generates AddFunctionContent + CreateName ops)
dark fn create Darklang.Stdlib.String reverse "Reverses a string"

# Edit function implementation in VS Code
# Validation happens automatically

# Sync to central server
dark sync push
```

### Flow 2: Content-Addressable Development

```bash
# Check content hash of a function
dark hash function Auth.JWT.validateToken
# Output: 2a7f4e9b8c3d1a5f...

# See what names point to this content
dark names show 2a7f4e9b8c3d1a5f
# Output: 
# Auth.JWT.validateToken
# Security.Token.validate (alias)

# Update implementation (creates new hash, updates name pointer)
dark fn update Auth.JWT.validateToken
# Old content still exists at old hash for history
```

This design provides a complete foundation for content-addressable source control with rich developer workflows, building naturally on Darklang's existing architecture.