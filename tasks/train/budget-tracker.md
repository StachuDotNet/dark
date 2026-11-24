Build a command-line personal budget tracker that keeps its data in a file between runs.

- `add AMOUNT CATEGORY [--date YYYY-MM-DD] [--note TEXT]` records an expense (today by default). Amounts have at most two decimal places and are kept exactly, never as floating point.
- `budget CATEGORY AMOUNT` sets a monthly limit for a category.
- `list [--month YYYY-MM] [--category C]` shows matching expenses, oldest first.
- `report [--month YYYY-MM]` shows, for each category, the amount spent, the limit and what is left, flags categories over their limit, and prints a total.
- `import FILE.csv` adds expenses from a CSV with the columns `date,amount,category,note`, and reports how many rows were added and which line numbers were rejected and why.
- `export FILE.json` writes every expense and budget as JSON.

Split the code into parts for storage, money arithmetic, commands and reports, and include tests for the money arithmetic and the report.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** larger (a few modules, about 200+ lines)
- **Kind:** CLI tool with persistence; multi-module project
- **Areas:** strings, lists, dicts, JSON, files, permissions, tests, store/CLI authoring
- **Network sites:** none
- **Seed:** none

## Acceptance checks

Run every command from the same empty folder.

1. `add 12.50 food --date 2026-03-02`, `add 0.10 food --date 2026-03-03`, `add 0.20 food --date 2026-03-03`, `add 40 rent --date 2026-03-01`, `budget food 12.75`. Then `report --month 2026-03` shows food spent 12.80, limit 12.75, over by 0.05 and flagged; rent 40.00 with no limit; total 52.80.
2. `list --month 2026-03 --category food` shows the three food rows, oldest first.
3. `import` a CSV with 3 good rows and 2 bad ones (an amount `1.234` and a date `2026-13-01`) adds 3 and names lines and reasons for the 2.
4. `export out.json` writes valid JSON containing all six expenses and the budget, with amounts that round-trip exactly.
5. The data survives between runs (each command above is a separate run).
6. Separate parts for storage, money, commands and reports; the tests run with one command and pass.
