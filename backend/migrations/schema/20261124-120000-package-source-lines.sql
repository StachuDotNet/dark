-- Where each item loaded from `packages/` is declared, for `--where`: lines count from 1, `text` is
-- the declaration's first line. A reload or an applied edit rewrites the rows of each file whose
-- text changed (`file_sha`). An item saved only in the store has no row.
CREATE TABLE IF NOT EXISTS package_source_lines (
  name TEXT NOT NULL,
  file TEXT NOT NULL,
  line INTEGER NOT NULL,
  end_line INTEGER NOT NULL,
  text TEXT NOT NULL,
  file_sha TEXT NOT NULL,
  PRIMARY KEY (name, file)
);
