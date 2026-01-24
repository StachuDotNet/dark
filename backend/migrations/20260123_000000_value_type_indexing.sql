-- Add type indexing to package values for efficient type-based discovery
-- This enables finding all values of a specific type (e.g., all HttpHandler values)

-- Add column to store the type ID of each value
-- This is the UUID of the type (e.g., the ID for Stdlib.Http.HttpHandler)
ALTER TABLE package_values ADD COLUMN value_type_id TEXT;

-- Create index for efficient type-based queries
CREATE INDEX IF NOT EXISTS idx_package_values_type
ON package_values(value_type_id);

-- Add a view that joins locations with type info for easy querying
CREATE VIEW IF NOT EXISTS values_by_type AS
SELECT
  l.owner,
  l.modules,
  l.name,
  l.item_id,
  l.branch_id,
  l.approval_status,
  l.deprecated_at,
  l.created_by,
  pv.value_type_id,
  pv.pt_def
FROM locations l
JOIN package_values pv ON l.item_id = pv.id
WHERE l.item_type = 'value';
