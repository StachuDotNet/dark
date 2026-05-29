# json2table

**Goal:** Stream JSON records into an aligned text table.

**Kind:** greenfield

## Acceptance criteria
- [ ] Renders an array of JSON objects as a column-aligned table.
- [ ] Matches golden-file output for fixture inputs.
- [ ] Unicode/emoji content renders without column misalignment.
- [ ] Empty input produces empty output and exits 0.
- [ ] Sorting is stable for equal keys.
- [ ] `--help` / `-h` prints usage and exits 0.
