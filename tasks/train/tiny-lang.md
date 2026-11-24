Build an interpreter for a tiny imperative language, using a hand-written recursive-descent parser. A program is a file of statements: `let x = <expr>;`, `x = <expr>;`, `print <expr>;`, `if (<expr>) { ... } else { ... }` (the else is optional) and `while (<expr>) { ... }`. Expressions have integers, strings in double quotes, `true`/`false`, variables, parentheses, unary `-` and `!`, `* / %`, `+ -` (`+` also joins strings), comparisons `< <= > >= == !=`, and `&&` `||`, with the usual precedence and left associativity. `//` starts a comment. A syntax error prints the line and column and what was expected, and runs nothing. A runtime error (an undefined variable, division by zero, adding a number to a boolean) prints the line and stops. Organise it as a lexer, a parser that builds a syntax tree, and an evaluator, with tests for the parser and the evaluator.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** larger (about 250+ lines across lexer, parser, evaluator and tests)
- **Kind:** parsing, interpreter; library with tests
- **Areas:** strings, chars, recursive data types, pattern matching, dicts, tests
- **Network sites:** none
- **Seed:** none

## Acceptance checks

1. `print 1 + 2 * 3 - 4 / 2;` prints 5; `print (1 + 2) * 3;` prints 9; `print 10 - 3 - 2;` prints 5; `print -2 * 3;` prints -6.
2. A loop summing 1 to 100 prints 5050; FizzBuzz to 15 with `if`/`else` and `%` prints the usual lines.
3. `print "a" + "b";` prints `ab`; `print 1 < 2 && !(3 == 4);` prints `true`.
4. `let x = ;` reports line 1 and a column near 9, and an earlier `print 1;` in the same file does not run.
5. `print y;` and `print 1 / 0;` print runtime errors with their line numbers.
6. Lexer, parser and evaluator are separate parts, and the tests run with one command and pass.
