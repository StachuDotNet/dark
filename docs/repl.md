# The repl: batches, replies and the report

A repl is a named working environment that outlives one `dark` call: a branch, a place in
the package tree, `let` bindings and watched entries. You send it a batch of lines on stdin and
get one numbered reply per line, then a short report on what the batch changed. Anything that
drives Dark from a shell (a person, a script, Claude Code, the Dark agent) uses the same
contract, and the Dark agent's own Dark tools are clients of it.

This page is the contract. Once agents parse these replies the framing cannot change, so a
change here is a breaking change and goes in this file first.

    $ dark repl new
    r1
    $ dark @r1 <<'EOF'
    let xs = [1, 2, 3]
    Stdlib.List.map(xs, fun x -> x * 2)
    fn Acme.double '(a: Int64): Int64 = a * 2'
    Acme.double(21)
    watch Acme.double(4)
    EOF
    1 ok xs = [1, 2, 3]
    2 [2, 4, 6]
    3 ok Created function Acme.double
    4 42
    5 ok watching Acme.double(4)
    -- 5 ok, 0 failed, 0 skipped, 212 ms
    -- typecheck Acme: 1 checked, 0 failed
    -- watch Acme.double(4) = 8

## The commands

    dark repl new [--branch <name>]   make a repl, print its id (r1, r2, ...), make it current
    dark repl list                    one line per repl: id, branch, location, bindings, watches
    dark repl use <id>                make <id> the current repl
    dark repl fork <id>               a new repl on a new branch off <id>'s, with its bindings
    dark repl kill <id>               forget a repl (its branch and its code stay)
    dark repl show <id> [<n>]         the last batch's replies in full, or reply <n> in full
    dark @<id> [--continue] [--json]  run the batch on stdin in <id>
    dark @ [--continue] [--json]      the same, in the current repl

`--branch` takes an existing branch; without it the repl starts on the branch `dark` is on. A
new branch starts from its parent's commits, so `fork` first commits the parent's draft on the
parent's branch ("fork point for r2"). A repl on main is never committed for you: its fork starts
from main's last commit, and `fork` says so.

## Lines

A line that starts in column 0 starts a statement. A line that starts with a space or tab
continues the statement above it. A blank line ends the statement. Each statement is one of:

- `let <name> = <expression>`: a binding, in scope for every later statement and batch.
- A CLI command, the same words you would type after `dark` (`fn`, `module`, `view`, `search`,
  `test run`, `typecheck`, `status`, `commit -m ... -y`, `nav`, `switch`, ...). Words split the
  way a POSIX shell splits them: single quotes, double quotes and backslash. A command whose last
  word is `-` reads its continuation lines, with their common indent removed, as its stdin:

      module /Acme.Greet -
        let greeting(name: String): String = "Hello, " + name

- `watch <expression>` / `unwatch <expression>`: report the expression's value after every
  batch. A watch is evaluated with the bindings in scope.
- Anything else is a Dark expression, evaluated with the bindings in scope on the repl's branch.

A leading `dark ` is dropped, so a line copied from a shell (`dark fn ...`) means `fn ...`.

Commands that need a terminal (a `y/N` confirm, a secret, `$EDITOR`, a full-screen view) are
refused with a pointer to their `-y` form or to running them outside the repl. `repl` and
`@<id>` are refused inside a batch.

## Replies

One reply per statement, numbered from 1 in the order the statements appear. The first line of a
reply starts with its number and a space; every further line of the same reply starts with two
spaces. Nothing else ever starts with a digit in column 0, so a reader splits replies on
`^[0-9]+ ` and continuation on `^  `.

    <n> ok                      a command that succeeded and printed nothing
    <n> ok <first line>         a command that succeeded; further lines follow, indented
    <n> ok <name> = <value>     a binding; the value is a Dark literal
    <n> ok <name> ~ <expr>      a binding whose value has no literal; it re-runs each batch
    <n> <value>                 an expression's value, as a Dark literal you can paste back
    <n> ok                      an expression that returned `()`
    <n> error: <message>        a failure, one line, plus indented detail
    <n> skipped                 not run, because an earlier statement failed

Values print as Dark literals (`"hi"`, `Some(3)`, `Acme.Point { x = 1, y = 2 }`), not pretty
renderings, so a value can be pasted into the next batch. A value with no literal form (a
function, a stream) prints as the CLI's `eval` prints it.

No colour, no banners and no prose framing, whether or not stdout is a terminal.

A `run` reply ends with the trace it left, `trace 13b52c04: dark traces inspect 13b52c04`, and a
failed command with no output says what it exited with (``error: `run` exited 2``).

A command fails when it exits non-zero. An expression or binding fails when evaluating it
raises or is refused. On the first failure the batch stops and every later statement replies
`skipped`; with `--continue` every statement runs and each replies for itself.

## Size caps

A reply keeps its first 20 lines and at most 2,000 characters. Past either, its last line is

      ... <k> more lines: dark repl show <id> <n>

The full text of every reply of the last batch is kept, so nothing is lost, only deferred. The
report is at most 12 lines; past that it ends in a pointer to `dark repl show <id>`.

## The summary and the report

After the replies, every line starts with `-- `. The first is always the summary:

    -- <ok> ok, <failed> failed, <skipped> skipped, <ms> ms

Then the report, which is what saves a turn: what you would otherwise have to ask for next.

    -- typecheck <module>: <checked> checked, <failed> failed
    --   <name>: <first issue>                      one line per failing item
    -- watch <expression> = <value>               a value that changed, or a first value
    -- watch <expression> unchanged
    -- watch <expression> error: <message>
    -- branch <name>, at <location>               only when the batch moved either

`typecheck` lines cover every module a statement in the batch wrote to (`fn`, `module`, `val`,
`type`, `edit`, `delete`, `rename`, ...), so a caller in that module that no longer typechecks is
named here. A write command also names the callers it broke anywhere, in its own reply.

## `--json`

The same content and the same numbering, as one JSON object on stdout:

    { "repl": "r1",
      "branch": "main",
      "location": "Acme",
      "replies": [
        { "n": 1, "kind": "binding", "status": "ok", "text": "xs = [1, 2, 3]", "more": 0 },
        { "n": 2, "kind": "expression", "status": "ok", "text": "[2, 4, 6]", "more": 0 },
        { "n": 3, "kind": "command", "status": "ok", "text": "Created function Acme.double",
          "more": 0 } ],
      "summary": { "ok": 3, "failed": 0, "skipped": 0, "ms": 212 },
      "typecheck": [ { "module": "Acme", "checked": 1, "failed": [] } ],
      "watches": [ { "expression": "Acme.double(4)", "status": "ok", "value": "8",
                     "changed": true } ] }

`kind` is `binding`, `expression`, `command` or `watch`. `status` is `ok`, `error` or
`skipped`. `text` is the reply without its number, its lines joined by `\n`, capped as above;
`more` is how many lines the cap dropped. `failed` lists `{ "name", "issues" }`.

## Exit codes

0 when every statement succeeded, 1 when one failed, 2 when the batch could not start (no such
repl, stdin not UTF-8).

## Where it lives, and the trade-off

A repl is a record in this instance's store config (`repl.<id>`): branch, location, bindings,
watches and the last batch's full replies. `dark @r1` is an ordinary `dark` process that loads
that record, runs the batch, and saves it back. There is no resident process yet. What that costs:
each batch pays the CLI's start (about 0.4 s) and the package caches start cold; bindings with no
literal form re-run every batch. What it buys: nothing to start, stop or leak, a crash loses at
most the batch in flight, and two terminals on one repl see the same state.

Branch and location are sticky: `switch` and `nav` inside a batch move the repl, not the rest of
the instance, and every batch runs where the last one left off.
