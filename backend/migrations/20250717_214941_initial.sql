-- note that the system_migration_v0 table is also crated by Migrations.fs
CREATE TABLE IF NOT EXISTS
system_migrations_v0
( name TEXT PRIMARY KEY
, execution_date TEXT NOT NULL -- timestamp
, sql TEXT NOT NULL
);


CREATE TABLE IF NOT EXISTS
accounts_v0
-- TODO include name
-- and update references (i.e. in package_types) to be based on id
( id TEXT PRIMARY KEY -- UUID stored as text
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

--------------------
-- Stuff that belongs in "package space"
--------------------
-- [1] = the whole thing serialized as binary, in ProgramTypes form (via custom binary serialization)
CREATE TABLE IF NOT EXISTS
package_types_v0
( id TEXT PRIMARY KEY -- UUID stored as text
, owner TEXT NOT NULL  -- e.g. Darklang
, modules TEXT NOT NULL -- e.g. Twitter.Other
, name TEXT NOT NULL -- e.g. TextMetadata
, definition BLOB NOT NULL -- see [1]
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
);


CREATE TABLE IF NOT EXISTS
package_constants_v0
( id TEXT PRIMARY KEY
, owner TEXT NOT NULL -- e.g. Darklang
, modules TEXT NOT NULL -- e.g. Math.Geometry
, name TEXT NOT NULL -- e.g. pi
, definition BLOB NOT NULL -- see [1]
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS
package_functions_v0
( id TEXT PRIMARY KEY
, owner TEXT NOT NULL   -- e.g. Darklang
, modules TEXT NOT NULL -- e.g. Twitter.Other
, name TEXT NOT NULL   -- e.g. sendText
, definition BLOB NOT NULL -- see [1]
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

--------------------
-- Stuff that belongs in "user space"
--------------------
CREATE TABLE IF NOT EXISTS
canvases_v0
( id TEXT PRIMARY KEY
, account_id TEXT NOT NULL
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
, FOREIGN KEY(account_id) REFERENCES accounts_v0(id)
);

-- User K/V DBs
CREATE TABLE IF NOT EXISTS
user_data_v0
( id TEXT PRIMARY KEY
, canvas_id TEXT NOT NULL
, table_tlid INTEGER NOT NULL
, user_version INTEGER NOT NULL
, dark_version INTEGER NOT NULL
, data TEXT NOT NULL -- JSON stored as text
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
, updated_at TEXT NOT NULL DEFAULT (datetime('now'))
, key TEXT NOT NULL
, UNIQUE (canvas_id, table_tlid, dark_version, user_version, key)
);

CREATE INDEX IF NOT EXISTS
idx_user_data_fetch
ON user_data_v0
(canvas_id, table_tlid, user_version, dark_version);

CREATE INDEX IF NOT EXISTS
idx_user_data_current_data_for_tlid
ON user_data_v0
(user_version, dark_version, canvas_id, table_tlid);

-- No GIN index equivalent in SQLite
CREATE INDEX IF NOT EXISTS
idx_user_data_json
ON user_data_v0
(data);


-- HTTP Handlers
CREATE TABLE IF NOT EXISTS
domains_v0
( domain TEXT PRIMARY KEY
, canvas_id TEXT NOT NULL
, created_at TEXT NOT NULL DEFAULT (datetime('now')));
-- TODO: extract out table of http handlers from toplevels_v0


-- CRONs
CREATE TABLE IF NOT EXISTS
cron_records_v0
( id TEXT PRIMARY KEY
, tlid INTEGER NOT NULL
, canvas_id TEXT NOT NULL
, ran_at TEXT NOT NULL DEFAULT (datetime('now')) -- default as it's cheap
);

CREATE INDEX IF NOT EXISTS
idx_cron_records_tlid_canvas_id_id
ON cron_records_v0
(tlid, canvas_id, id DESC);

-- Queues/Workers
CREATE TABLE IF NOT EXISTS
scheduling_rules_v0
( id TEXT PRIMARY KEY
, rule_type TEXT NOT NULL CHECK (rule_type IN ('pause', 'block'))
, canvas_id TEXT NOT NULL
, handler_name TEXT NOT NULL
, event_space TEXT NOT NULL
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS
queue_events_v0
( id TEXT PRIMARY KEY
, canvas_id TEXT NOT NULL
, module TEXT NOT NULL
, name TEXT NOT NULL
, modifier TEXT NOT NULL
, locked_at TEXT -- nullable
, enqueued_at TEXT NOT NULL
, value TEXT NOT NULL
);


-- We want to use this index to:
-- 1) count the number of items in this queue, so it's important that the entire
-- search term is in the index or it will need to hit disk. This is true even though
-- the module rarely changes
-- 2) fetch the indexes for all items we're unpausing. This is rare so it's fine to
CREATE INDEX IF NOT EXISTS
idx_queue_events_count
ON queue_events_v0
(canvas_id, module, name);


-- Secrets
CREATE TABLE IF NOT EXISTS
secrets_v0
( canvas_id TEXT NOT NULL
, name TEXT NOT NULL
, value TEXT NOT NULL
, version INTEGER NOT NULL
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
, PRIMARY KEY (canvas_id, name, version) -- TODO: simplfy PK
);


-- Top-levels
-- TODO split this into a few tables (dbs, handlers, etc)
-- rebrand 'canvas' to 'app'
CREATE TABLE IF NOT EXISTS
toplevels_v0
( canvas_id TEXT NOT NULL
, tlid INTEGER NOT NULL
, digest CHAR(32) NOT NULL
, tipe TEXT NOT NULL CHECK (tipe IN ('db', 'handler'))
, name TEXT /* handlers only - used for http lookups */
, module TEXT /* handlers only */
, modifier TEXT /* handlers only */
, updated_at TEXT NOT NULL DEFAULT (datetime('now'))
, created_at TEXT NOT NULL DEFAULT (datetime('now'))
, deleted INTEGER NOT NULL CHECK (deleted IN (0,1))
, data BLOB NOT NULL
, PRIMARY KEY (canvas_id, tlid)
);

-- Traces
CREATE TABLE IF NOT EXISTS
traces_v0
( id TEXT PRIMARY KEY
, trace_id TEXT NOT NULL -- why do we need this _and_ `id`?
, canvas_id TEXT NOT NULL
-- the handler's (or for a function's default trace, the function's) TLID
--   (used to store the trace data in Cloud Storage)
-- TODO consider using a different mechanism here - fns might not have tlids...
--   why wouldn't we use the `id` instead? length?
, root_tlid INTEGER NOT NULL
, callgraph_tlids TEXT NOT NULL -- functions called during the trace
);