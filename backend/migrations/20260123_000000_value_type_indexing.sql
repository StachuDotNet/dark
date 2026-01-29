-- Add type indexing to package values for efficient type-based discovery
-- This enables finding all values of a specific type (e.g., all HttpHandler values)
-- Stores a binary-serialized ValueType for each package value.

-- Add column to store the serialized ValueType of each value
ALTER TABLE package_values ADD COLUMN value_type BLOB;

-- Create index for efficient type-based queries (exact binary match)
CREATE INDEX IF NOT EXISTS idx_package_values_type
ON package_values(value_type);
