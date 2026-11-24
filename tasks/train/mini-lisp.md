Build an interpreter for a small Lisp. It reads a program from a file (or a REPL from standard input when no file is given) and prints the value of each top-level expression.

The language has integers, booleans `#t` and `#f`, symbols, and lists; `(define name value)`, `(if c a b)`, `(lambda (args...) body)` with closures, `(let ((x 1) (y 2)) body)`, `(quote x)` and `'x`; the built-ins `+ - * < = car cdr cons list null?` and `display`. Functions can be recursive. Errors (an unbound symbol, a wrong number of arguments, calling a non-function, unbalanced parentheses) print a message naming the problem and don't crash the REPL. Organise it as a reader, an evaluator and a printer, with tests for each.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** larger (about 200+ lines across reader, evaluator, printer and tests)
- **Kind:** text parsing, interpreter; library with tests
- **Areas:** strings, lists, dicts (environments), tests; also recursive data types
- **Network sites:** none
- **Seed:** none

## Acceptance checks

1. `(+ 1 (* 2 3))` prints 7; `(if (< 1 2) 'yes 'no)` prints `yes`.
2. `(define fact (lambda (n) (if (= n 0) 1 (* n (fact (- n 1))))))` then `(fact 10)` prints 3628800.
3. A closure: `(define make-adder (lambda (n) (lambda (x) (+ x n))))`, `((make-adder 3) 4)` prints 7.
4. `(let ((x 2) (y 3)) (* x y))` prints 6; `(car (cdr (list 1 2 3)))` prints 2; `(null? (quote ()))` prints `#t`.
5. `(fact 1000)` doesn't crash with a stack overflow, or the agent's summary says plainly that deep recursion is a known limit. Note which.
6. `(foo 1)`, `((lambda (x) x))`, `(1 2)` and `(+ 1` each print an error, and the REPL carries on.
7. Reader, evaluator and printer are separate parts, and the tests run with one command and pass.
