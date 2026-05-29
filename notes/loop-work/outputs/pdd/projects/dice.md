# dice

**Goal:** Roll dice expressions such as `3d6+2` and `4d6 drop lowest`, with an optional stats mode.

**Kind:** greenfield

## Acceptance criteria
- [ ] Parses standard dice notation including modifiers and drop/keep clauses.
- [ ] Seeded mode produces deterministic rolls for testing.
- [ ] Produces correct output for at least 5 canonical inputs.
- [ ] Bad input exits non-zero with a readable error on stderr.
- [ ] Handles empty, very long, and non-ASCII input without crashing.
- [ ] `--help` / `-h` prints usage and exits 0.
