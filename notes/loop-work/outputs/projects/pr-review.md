# pr-review

**Goal:** Read `git diff main...HEAD` and produce an LLM code-review critique.

**Kind:** greenfield

## Acceptance criteria
- [ ] Summarizes and critiques the branch diff as review comments.
- [ ] With temperature 0.0 and a fixed model, responses are reproducible (snapshot).
- [ ] The transcript shows the expected tool calls firing.
- [ ] `withMaxTokens` / `withMaxTurns` cost guards are respected.
- [ ] A missing API key produces a crisp error and exits 1.
- [ ] An offline fixture mode returns a canned response for reproducible testing.
- [ ] `--help` prints usage and exits 0.
