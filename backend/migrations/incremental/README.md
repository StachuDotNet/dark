# Incremental migrations

This directory holds per-file additive migrations, run in lexical
order on top of `../schema.sql` and name-dedup'd via
`system_migrations_v0`. Empty by default.

When to add a file here vs editing `schema.sql`:

- **Edit `schema.sql`**: structural redesigns, adding a new table or
  column. The file is hashed; on a change the runtime does a
  preserve-and-refold — it drops only the regenerable projection
  tables and re-folds them from the op log, so the canonical op
  log / blobs / branch+commit state survive. (A canonical-table
  *shape* change still needs a Release migration — CREATE IF NOT
  EXISTS can't alter an existing table.)
- **Add a file here**: data backfills, transforms, additive
  alterations on populated dev/test DBs you don't want to nuke.

File naming: `YYYYMMDD_HHMMSS_<short-tag>.sql` so lexical sort gives
chronological order. First line may be the literal `--#[no_tx]` to
skip the wrapping transaction (rare; for DDL SQLite refuses inside
one).
