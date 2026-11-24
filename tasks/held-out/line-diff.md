Build a command-line tool that compares two text files line by line and prints the differences in unified diff format, like `diff -u`: a `---`/`+++` header naming the two files, then hunks with `@@ -a,b +c,d @@` ranges and 3 lines of context, lines prefixed by a space, `-` or `+`. The diff should be minimal (based on a longest common subsequence). It exits 0 when the files are the same (printing nothing), 1 when they differ, and 2 on an error such as a missing file. A `-U N` option changes the number of context lines.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** text diffing, algorithm, CLI tool
- **Areas:** strings, lists, arrays/dynamic programming, files, exit codes
- **Network sites:** none
- **Seed:** none

## Acceptance checks

Compare against GNU `diff -u`, ignoring the timestamps in the two header lines.

1. Identical files: no output, exit 0.
2. `seq 1 20 > a`, `seq 1 20 | sed 's/^5$/five/; /^15$/d' > b`: the hunks and ranges match `diff -u a b` exactly, exit 1.
3. One change in the middle of a 100-line file gives one hunk with 3 lines of context on each side; with `-U 0` it has none.
4. One file empty, the other with 3 lines: ranges `@@ -0,0 +1,3 @@`.
5. Two 2,000-line files with 50 scattered changes finish in under 10 seconds and match `diff -u`.
6. A missing file exits 2 with a message.
