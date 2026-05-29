# release-notes

**Goal:** Group commits between two refs by conventional-commit type into release notes.

**Kind:** greenfield

## Acceptance criteria
- [ ] Groups commits by conventional-commit type into a formatted notes block.
- [ ] Matches golden-file output for fixture inputs.
- [ ] Unicode/emoji content renders without column misalignment.
- [ ] Empty input produces empty output and exits 0.
- [ ] Sorting is stable for equal keys.
- [ ] `--help` / `-h` prints usage and exits 0.
