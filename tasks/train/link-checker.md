Build a link checker. It reads a text file with one URL per line (blank lines and lines starting with `#` are ignored), checks each URL with an HTTP request, at most 4 at a time, and prints each URL with its status code or the error, in the order of the file. It exits 1 if any URL answered 400 or above or failed, and 0 otherwise. Also write a short README section with the exact steps a new user follows to run it on their own file, starting from a fresh install of the language: anything they must install, configure or allow first.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** HTTP client, CLI tool; Dark strength: permissions for a tool that reads files and the network
- **Areas:** HTTP client, files, concurrency, permissions
- **Network sites:** `github.com`
- **Seed:** none

## Acceptance checks

Make `urls.txt` with:

    # some links
    https://github.com/darklang/dark

    https://github.com/darklang/no-such-repo-bench-404

1. `<tool> urls.txt` prints the first URL with 200 and the second with 404, in file order, and exits 1.
2. With only the first URL, it exits 0.
3. A URL with an unresolvable host (`https://no-such-host.invalid/`) is reported as an error, not a crash.
4. **The README steps work as written** in a fresh environment (a fresh HOME for Dark, a fresh virtualenv for Python): a person who follows them exactly, with their own `urls.txt` in a different folder, gets the output without hitting a permission or setup error the README didn't mention.
5. The README does not tell the user to allow more than the tool needs (for Dark: no blanket allow-all file or HTTP rule).
