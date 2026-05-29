---
title: hello-cli
tier: T
class: app
modules: [Stdlib.Cli, Stdlib.String]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: false
---

# Description

The smallest possible end-to-end harness check. The program reads a single positional argument (a name), and prints `Hello, <name>!` followed by a newline.

This project exists to validate the bench's end-to-end pipeline — agent gets a spec, produces an artifact, rubric runs, metric lands in `results.jsonl`. If `hello-cli` fails, the harness is broken. Almost nothing about this project tests Dark vs TS vs Py; it tests the harness itself.

The program reads no stdin and writes no files. Stdout receives exactly the greeting line; stderr receives errors.

# Behaviours

- `hello-cli World` prints exactly `Hello, World!\n` to stdout and exits 0.
- `hello-cli "Jane Doe"` (quoted argument with a space) prints `Hello, Jane Doe!\n`.
- `hello-cli` (no argument) exits non-zero with a usage line on stderr (e.g. `Usage: hello-cli <name>`).
- `hello-cli foo bar` (two arguments) — behaviour is implementation-defined; either error or accept first only is fine. The rubric does *not* test this.
- `hello-cli --help` (or `-h`) prints usage and exits 0.
- `hello-cli ""` (empty string argument) prints `Hello, !\n` — empty name is accepted, not an error.
- `hello-cli "<unicode>"` with non-ASCII (e.g. `hello-cli "Älice"`) prints the name byte-correctly, exits 0.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. `hello-cli World` — eyeball the output is exactly `Hello, World!`.
2. `hello-cli` (no args) — should error and exit non-zero, not crash silently.
3. `echo $?` after the no-args invocation — confirms a non-zero exit code.
4. `hello-cli "$(printf 'Crème\\nbrûlée')"` — the agent should treat the literal newline as part of the name, producing two-line output, not crash.
5. The artifact should be tiny — under ~50 lines of source for any of the languages. If the solution is much larger, something's wrong (or the language doesn't have a clean stdout primitive). Note this in `SUMMARY.md`.
6. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- hello-cli World
- hello-cli "Jane Doe"
- hello-cli
- hello-cli --help

---

**Smoke-test core role** *(per iter-43 footnote: hello-cli is the harness smoke test, not a core)*: this is the simplest possible artifact. If it fails on the bench, the bench infrastructure is the problem, not the agent or the language. Run this first in any new sweep; refuse to score anything else if `hello-cli` doesn't pass for at least one language.
