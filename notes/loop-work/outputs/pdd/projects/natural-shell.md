# natural-shell

**Goal:** Translate a natural-language request into a shell command.

**Kind:** greenfield

## Acceptance criteria
- [ ] Turns a plain-English request into a runnable shell command and shows it before running.
- [ ] With temperature 0.0 and a fixed model, responses are reproducible (snapshot).
- [ ] The transcript shows the expected tool calls firing.
- [ ] `withMaxTokens` / `withMaxTurns` cost guards are respected.
- [ ] A missing API key produces a crisp error and exits 1.
- [ ] An offline fixture mode returns a canned response for reproducible testing.
- [ ] `--help` prints usage and exits 0.
