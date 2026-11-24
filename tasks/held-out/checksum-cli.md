Build a command-line tool like `sha256sum`. Given file paths, it prints the SHA-256 of each file in hex followed by two spaces and the path. With `--check LIST`, it reads a list in that same format and prints `path: OK` or `path: FAILED` for each line, then exits 1 if any failed or were missing, and 0 otherwise.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** tiny
- **Kind:** CLI tool
- **Areas:** strings, files, permissions; also bytes, hashing and exit codes
- **Network sites:** none
- **Seed:** none

## Acceptance checks

1. For `printf 'hello\n' > h.txt`, `<tool> h.txt` prints `5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03  h.txt`.
2. For several files, its output matches `sha256sum` line for line (including a 0-byte file and a 2 MB binary file).
3. `<tool> h.txt > sums; <tool> --check sums` prints `h.txt: OK` and exits 0.
4. After changing `h.txt`, the check prints `FAILED` and exits 1; after deleting it, it reports the file missing and exits 1.
