# renamer

**Goal:** Bulk-rename files by regex, with a dry-run mode and an undo log.

**Kind:** greenfield

## Acceptance criteria
- [ ] A dry run reports planned renames without touching disk.
- [ ] An undo log allows reverting a completed rename batch.
- [ ] Operates over a scratch directory of known contents with correct results.
- [ ] A 10 MB+ file does not exhaust memory.
- [ ] Symlinks are not followed into infinite loops.
- [ ] UTF-8 in filenames and file bodies round-trips correctly.
- [ ] Exit codes follow convention (0 success, non-zero on error).
- [ ] `--help` / `-h` prints usage and exits 0.
