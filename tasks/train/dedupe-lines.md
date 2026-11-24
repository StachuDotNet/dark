Build a command-line filter that reads lines from standard input and prints each distinct line once, in the order it first appeared. With a `--count` flag, it prints each distinct line prefixed by how many times it appeared, still in first-seen order.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** tiny
- **Kind:** CLI tool, data processing
- **Areas:** strings, lists, dicts; also stdin
- **Network sites:** none
- **Seed:** none

## Acceptance checks

1. `printf 'b\na\nb\nc\na\n' | <tool>` prints exactly `b`, `a`, `c` on three lines.
2. `printf 'b\na\nb\nc\na\n' | <tool> --count` prints `b` with 2, `a` with 2, `c` with 1, in that order. The count and the line are clearly separated.
3. Empty input prints nothing and exits 0.
4. Lines that differ only in trailing spaces are kept apart (`printf 'x\nx \n' | <tool>` prints two lines).
5. A 100,000-line input (`seq 1 100000 | <tool> | wc -l` gives 100000) finishes in under 10 seconds.
