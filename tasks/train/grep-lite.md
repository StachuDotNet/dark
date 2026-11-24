Build a command-line search tool like a small `grep`. `search PATTERN PATH...` prints every matching line as `file:line-number:text`. A path that is a folder is searched recursively. `-i` ignores case, `-r` treats the pattern as a regular expression (otherwise it is a plain substring), and `-c` prints only a count per file. Exit status is 0 if anything matched, 1 if nothing did, and 2 on an error such as a missing path or an invalid regular expression.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** CLI tool, text processing
- **Areas:** strings, lists, files, permissions; also regular expressions, arguments and exit codes
- **Network sites:** none
- **Seed:** none

## Acceptance checks

Make `d/a.txt` with `printf 'Apple pie\nbanana\napple tart\n'` and `d/sub/b.txt` with `printf 'grape\nPineapple\n'`.

1. `<tool> apple d` prints `d/a.txt:3:apple tart` and `d/sub/b.txt:2:Pineapple`, and nothing else, and exits 0. (Paths may be printed relative or absolute, consistently.)
2. `<tool> -i apple d` also prints `d/a.txt:1:Apple pie`.
3. `<tool> -r '^[ab]' d` prints the `banana` and `apple tart` lines only.
4. `<tool> -c -i apple d` prints a count of 2 for `a.txt` and 1 for `b.txt`.
5. `<tool> kiwi d` prints nothing and exits 1; `<tool> x nowhere` exits 2; `<tool> -r '(' d` exits 2 with a message.
