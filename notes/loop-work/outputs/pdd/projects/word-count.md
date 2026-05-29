# word-count

**Goal:** Report `<lines> <words> <bytes>` for stdin, matching `wc`.

**Kind:** greenfield

## Acceptance criteria
- [ ] Reads from stdin and reports line count, word count, and byte count.
- [ ] Output matches `wc` for an ASCII fixture.
- [ ] Output matches `wc` for a UTF-8 fixture, with the byte count distinct from the codepoint count.
- [ ] Streams over the input rather than requiring the whole file in memory where feasible.
- [ ] Empty input produces zero counts and exits 0.
- [ ] `word-count --help` (or `-h`) prints usage and exits 0.
