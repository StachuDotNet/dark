# morse

**Goal:** Translate text to and from Morse code, with optional timing output.

**Kind:** greenfield

## Acceptance criteria
- [ ] Encodes text to Morse and decodes Morse back to text.
- [ ] Round-trips text through encode then decode.
- [ ] Produces correct output for at least 5 canonical inputs.
- [ ] Bad input exits non-zero with a readable error on stderr.
- [ ] Handles empty, very long, and non-ASCII input without crashing.
- [ ] `--help` / `-h` prints usage and exits 0.
