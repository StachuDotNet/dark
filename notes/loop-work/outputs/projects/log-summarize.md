# log-summarize

**Goal:** Group log lines by a regex bucket and print the top-N buckets.

**Kind:** greenfield

## Acceptance criteria
- [ ] Buckets log lines by a regex and prints the most frequent buckets.
- [ ] Matches golden-file output for fixture inputs.
- [ ] Unicode/emoji content renders without column misalignment.
- [ ] Empty input produces empty output and exits 0.
- [ ] Sorting is stable for equal keys.
- [ ] `--help` / `-h` prints usage and exits 0.
