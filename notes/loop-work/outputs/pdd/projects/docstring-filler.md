# docstring-filler

**Goal:** Rewrite a file's functions with doc comments and emit a diff.

**Kind:** greenfield

## Acceptance criteria
- [ ] Produces a diff adding docstrings to the file's functions without altering logic.
- [ ] With temperature 0.0 and a fixed model, responses are reproducible (snapshot).
- [ ] The transcript shows the expected tool calls firing.
- [ ] `withMaxTokens` / `withMaxTurns` cost guards are respected.
- [ ] A missing API key produces a crisp error and exits 1.
- [ ] An offline fixture mode returns a canned response for reproducible testing.
- [ ] `--help` prints usage and exits 0.
