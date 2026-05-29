# id-generator

**Goal:** Generate N UUIDs / ULIDs / nanoids with format flags.

**Kind:** greenfield

## Acceptance criteria
- [ ] Supports UUID, ULID, and nanoid output formats via a flag.
- [ ] Generates exactly N identifiers when a count is requested.
- [ ] Produces correct output for at least 5 canonical inputs.
- [ ] Bad input exits non-zero with a readable error on stderr.
- [ ] Handles empty, very long, and non-ASCII input without crashing.
- [ ] `--help` / `-h` prints usage and exits 0.
