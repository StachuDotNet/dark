Build a command-line number-guessing game. The program picks a secret whole number from 1 to 100 and reads guesses from standard input, one per line. After each guess it says "higher", "lower" or "correct". When the guess is correct it prints how many guesses it took and exits 0. Input that isn't a number in range gets a short message and doesn't count as a guess. If input ends before the number is found, it reveals the number and exits 1. A `--secret N` option fixes the secret, so the game can be tested.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** tiny
- **Kind:** small game, CLI tool
- **Areas:** strings; also stdin, randomness and exit codes
- **Network sites:** none
- **Seed:** none

## Acceptance checks

1. `printf '50\n25\n37\n' | <tool> --secret 37` says lower (or "the number is lower") after 50, higher after 25, correct after 37, reports 3 guesses, and exits 0.
2. `printf 'abc\n0\n101\n37\n' | <tool> --secret 37` complains about the first three lines and reports 1 guess.
3. `printf '10\n' | <tool> --secret 37` reveals 37 and exits 1.
4. Running `printf '%s\n' $(seq 1 100) | <tool>` five times ends with "correct" each time, and the guess counts are not all the same (the secret is random).
