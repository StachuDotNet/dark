# bug-triage

**Goal:** Given an error trace and a repo, suggest the likely file and line.

**Kind:** greenfield

## Acceptance criteria
- [ ] Maps an error trace to a probable file and line with reasoning.
- [ ] With temperature 0.0 and a fixed model, responses are reproducible (snapshot).
- [ ] The transcript shows the expected tool calls firing.
- [ ] `withMaxTokens` / `withMaxTurns` cost guards are respected.
- [ ] A missing API key produces a crisp error and exits 1.
- [ ] An offline fixture mode returns a canned response for reproducible testing.
- [ ] `--help` prints usage and exits 0.
