# ical-next

**Goal:** Parse an iCal file and show the next several events.

**Kind:** greenfield

## Acceptance criteria
- [ ] Parses iCal input and lists the next N upcoming events in order.
- [ ] Matches golden-file output for fixture inputs.
- [ ] Unicode/emoji content renders without column misalignment.
- [ ] Empty input produces empty output and exits 0.
- [ ] Sorting is stable for equal keys.
- [ ] `--help` / `-h` prints usage and exits 0.
