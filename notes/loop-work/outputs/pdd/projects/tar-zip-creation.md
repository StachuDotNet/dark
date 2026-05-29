# tar-zip-creation

**Goal:** Create standard gzipped-tar and/or ZIP archives of a directory that stock `tar`/`unzip` can extract byte-faithfully.

**Kind:** greenfield

## Acceptance criteria
- [ ] `tar-zip create out.tar.gz src/` recursively archives all files from `src/` and exits 0.
- [ ] `tar -xzf out.tar.gz -C <dir>` (stock GNU tar) extracts files that byte-match the originals in `src/`.
- [ ] File modes are preserved (at least the executable bit).
- [ ] Symlinks are preserved as symlinks (not silently dereferenced).
- [ ] Empty directories are preserved.
- [ ] `tar-zip create-zip out.zip src/` produces a ZIP extractable by stock `unzip` with identical contents (if implemented).
- [ ] `tar-zip create out.tar.gz /no/such/dir` exits non-zero with an error mentioning the missing directory.
- [ ] `tar-zip create existing.tar.gz src/` overwrites the existing archive without prompting.
- [ ] A large directory of many small files packs in linear time (seconds, not minutes).
- [ ] `file out.tar.gz` reports `gzip compressed data`.
- [ ] `tar-zip --help` exits 0.
