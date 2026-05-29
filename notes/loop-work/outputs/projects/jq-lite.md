# jq-lite

**Goal:** Read JSON from stdin and apply a jq-subset filter expression, printing the result.

**Kind:** greenfield

## Acceptance criteria
- [ ] Supports filters `.foo`, `.foo.bar`, `.foo[0]`, `.foo | select(.x > 5)`, and `.foo[] | .name`.
- [ ] Output matches `jq`'s output (modulo whitespace) for a set of filter expressions over an array fixture.
- [ ] An invalid filter exits non-zero with a helpful error.
- [ ] Parses a large (e.g. 10 MB) JSON input without running out of memory.
- [ ] Empty input is handled cleanly.
- [ ] `jq-lite --help` prints usage and exits 0.
