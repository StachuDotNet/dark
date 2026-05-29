# gh-pr-digest

**Goal:** Fetch GitHub PRs and tabulate them by author and age.

**Kind:** greenfield

## Acceptance criteria
- [ ] Produces a table of open PRs grouped/sorted by author and age.
- [ ] Matches golden-file output for fixture inputs.
- [ ] Unicode/emoji content renders without column misalignment.
- [ ] Empty input produces empty output and exits 0.
- [ ] Sorting is stable for equal keys.
- [ ] `--help` / `-h` prints usage and exits 0.
