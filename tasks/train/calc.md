Build a calculator for arithmetic expressions. It reads one expression per line from standard input and prints the result of each. It supports `+ - * /`, parentheses, unary minus and decimal numbers, with the usual precedence (`*` and `/` before `+` and `-`, left to right). Division by zero and malformed input print an error that says where in the line the problem is, and the calculator carries on with the next line. Include tests for the parser and the evaluator.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** text parsing, small interpreter
- **Areas:** strings, lists, tests; also enums or similar for the syntax tree
- **Network sites:** none
- **Seed:** none

## Acceptance checks

1. `1 + 2 * 3` gives 7; `(1 + 2) * 3` gives 9; `10 - 4 - 3` gives 3; `2 * -3` gives -6; `-(2 + 3)` gives -5.
2. `7 / 2` gives 3.5; `0.1 + 0.2` gives 0.3 (a printed `0.30000000000000004` passes but is noted).
3. `1 / 0` prints an error and the next line still evaluates.
4. `2 + * 3` and `(1 + 2` print an error with a column or a caret pointing near the problem.
5. The tests run with one command and pass, and cover at least precedence, parentheses, unary minus and one error case.
