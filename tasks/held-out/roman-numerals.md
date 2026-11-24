Build a small reusable library for Roman numerals: one function converts a whole number from 1 to 3999 to a Roman numeral, and another converts a Roman numeral back, rejecting anything that isn't a valid, canonical numeral (such as `IIII`, `VX` or `MMMM`) with an error rather than a guess. Other code should be able to call both by name. Include tests, runnable with one command.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** tiny
- **Kind:** library with tests; Dark strength: building in the store (fn, module and test add, run by name)
- **Areas:** strings, lists, tests, store/CLI authoring
- **Network sites:** none
- **Seed:** none

## Acceptance checks

1. 1994 gives `MCMXCIV`; 3999 gives `MMMCMXCIX`; 4 gives `IV`.
2. `MCMXCIV` parses back to 1994, and every number from 1 to 3999 round-trips (the checker runs this loop by calling the library by name).
3. 0, 4000 and -1 are refused with an error.
4. `IIII`, `VX`, `MMMM`, `IC`, the empty string and `abc` are refused with an error.
5. The tests run with one command and pass. Dark: they are package tests that `test run` finds; Python: `python3 -m unittest` or `pytest` finds them.
