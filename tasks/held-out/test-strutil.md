The project folder contains `strutil`, a small library of text helpers. README.md says what each function promises and where the code is. Nothing tests it yet.

Write tests for every function, covering the examples in README.md and the edge cases it describes. The tests must run with one command. If a test shows the code disagreeing with README.md, fix the code, not the test, and say what you fixed.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** working on existing code: add tests to untested code (with one planted bug)
- **Areas:** tests, strings, lists, store/CLI authoring, traces (useful for the bug)
- **Network sites:** none
- **Seed:** `seeds/strutil`: `truncate` shortens a string whose length equals `n` (`<` where README says "at most")

## Acceptance checks

1. The tests run with one command and pass. Dark: package tests that `test run` finds; Python: `unittest` or `pytest`.
2. There is at least one test per function (five functions), and the README examples are among them.
3. `truncate` is fixed: a string of exactly `n` characters comes back unchanged, and the summary says so.
4. **Mutation check.** The checker makes each of these changes in turn, runs the tests, and puts the code back. The tests must fail for every one:
   - slugify keeps a leading `-` (for `"!a"` it gives `"-a"`);
   - truncate returns `n` characters plus the ellipsis;
   - wrap uses `<` instead of `<=` for the width (so a line of exactly `width` is split);
   - title-casing lowercases the first word when it is a small word;
   - word counting splits on single spaces only (so `"a  b"` gives 3).
5. No function's behaviour changed except `truncate`.
