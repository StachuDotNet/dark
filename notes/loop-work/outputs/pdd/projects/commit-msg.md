# commit-msg

**Goal:** Generate a commit message from staged changes via an LLM call.

**Kind:** greenfield

## Acceptance criteria
- [ ] Reads the staged diff and produces a sensible commit message.
- [ ] With temperature 0.0 and a fixed model, responses are reproducible (snapshot).
- [ ] The transcript shows the expected tool calls firing.
- [ ] `withMaxTokens` / `withMaxTurns` cost guards are respected.
- [ ] A missing API key produces a crisp error and exits 1.
- [ ] An offline fixture mode returns a canned response for reproducible testing.
- [ ] `--help` prints usage and exits 0.
