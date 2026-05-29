# unit-convert

**Goal:** Convert quantities between units (e.g. `10 mi to km`, `72 F to C`, `1 GiB to MB`).

**Kind:** greenfield

## Acceptance criteria
- [ ] Supports length, temperature, and data-size unit families at minimum.
- [ ] Produces correct output for at least 5 canonical inputs.
- [ ] Bad input exits non-zero with a readable error on stderr.
- [ ] Handles empty, very long, and non-ASCII input without crashing.
- [ ] `--help` / `-h` prints usage and exits 0.
