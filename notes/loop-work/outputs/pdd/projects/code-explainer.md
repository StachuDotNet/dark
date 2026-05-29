# code-explainer

**Goal:** Summarize each file in a globbed path via an LLM.

**Kind:** greenfield

## Acceptance criteria
- [ ] Emits a per-file summary for the matched files.
- [ ] With temperature 0.0 and a fixed model, responses are reproducible (snapshot).
- [ ] The transcript shows the expected tool calls firing.
- [ ] `withMaxTokens` / `withMaxTurns` cost guards are respected.
- [ ] A missing API key produces a crisp error and exits 1.
- [ ] An offline fixture mode returns a canned response for reproducible testing.
- [ ] `--help` prints usage and exits 0.
