Build a command-line tool like `wc`. Given one or more file paths, it prints the number of lines, words and characters in each file, followed by a total line when there is more than one file. With no paths it reads standard input instead. If a file can't be read, it prints an error for that file to stderr, still reports the others, and exits with status 1.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** tiny (under 30 lines)
- **Kind:** CLI tool
- **Areas:** strings, lists, files, permissions; also arguments, stdin and exit codes
- **Network sites:** none
- **Seed:** none

## Acceptance checks

`<tool>` is the command the agent's summary says runs the program. Make `a.txt` with `printf 'one two\nthree\n'` and `b.txt` with `printf 'four\n'`.

1. `<tool> a.txt` shows 2 lines, 3 words, 14 characters for `a.txt`, and exits 0.
2. `<tool> a.txt b.txt` shows a row for each file and a total of 3 lines, 4 words, 19 characters.
3. `printf 'x y z' | <tool>` shows 0 lines, 3 words, 5 characters.
4. `<tool> a.txt missing.txt` still reports `a.txt`, prints an error naming `missing.txt` on stderr, and exits 1.
5. The column order (lines, words, characters) is the same in every row.
