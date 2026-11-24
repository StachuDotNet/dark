The project folder contains `salesreport`, a command-line tool that reads a CSV of sales and prints a report. README.md describes it. It works, and people depend on its exact output, but all of it is one long function.

Restructure it into separate parts with clear jobs: reading and splitting CSV, validating and converting rows, aggregating, and formatting the report. Each part must be usable and testable on its own. Add tests for the CSV splitting and for the aggregation. The tool's output and exit codes must not change at all, for any input.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** larger (about 200+ lines once split, with tests)
- **Kind:** working on existing code: refactor without changing behaviour; multi-module project
- **Areas:** strings, lists, dicts, files, tests, store/CLI authoring (Dark: modules in the store or nested modules)
- **Network sites:** none
- **Seed:** `seeds/salesreport`

## Acceptance checks

1. For each `fixtures/sales-N.csv`, stdout is byte-identical to `sales-N.expected`, with exit 0.
2. The hidden `checks/sales-4.csv` gives exactly `checks/sales-4.expected` (ties, trimming, a zero-unit row, a malformed date).
3. A missing file exits 1; no argument exits 2.
4. The code has at least four separately named parts (modules, files or namespaces) matching the jobs in the prompt, and the old long function is gone.
5. The tests run with one command and pass; they include a quoted field with a comma and a doubled quote, and an aggregation over at least two regions.
6. Each part can be called on its own (Python: imported; Dark: called by name with `eval` or from a test).
