# ciphers

**Goal:** Apply classic string ciphers (leet, rot13, caesar, vigenère) to input text.

**Kind:** greenfield

## Acceptance criteria
- [ ] Supports leet, rot13, caesar, and vigenère transforms via subcommand or flag.
- [ ] Caesar and vigenère round-trip with the correct key.
- [ ] Produces correct output for at least 5 canonical inputs.
- [ ] Bad input exits non-zero with a readable error on stderr.
- [ ] Handles empty, very long, and non-ASCII input without crashing.
- [ ] `--help` / `-h` prints usage and exits 0.
