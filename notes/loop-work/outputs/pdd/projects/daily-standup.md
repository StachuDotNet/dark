# daily-standup

**Goal:** Draft standup bullets from git log and calendar data.

**Kind:** greenfield

## Acceptance criteria
- [ ] Produces yesterday/today standup bullets from recent commits and calendar entries.
- [ ] With temperature 0.0 and a fixed model, responses are reproducible (snapshot).
- [ ] The transcript shows the expected tool calls firing.
- [ ] `withMaxTokens` / `withMaxTurns` cost guards are respected.
- [ ] A missing API key produces a crisp error and exits 1.
- [ ] An offline fixture mode returns a canned response for reproducible testing.
- [ ] `--help` prints usage and exits 0.
