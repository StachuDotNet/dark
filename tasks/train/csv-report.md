Build a command-line tool that turns a CSV of sales into a report. The CSV has a header line and the columns `date,region,product,units,unit_price`; fields may be quoted with double quotes, and a quoted field can contain commas and doubled quotes (`""`). The report groups sales by region and, within each region, by month (`YYYY-MM`), showing units and revenue for each group, a subtotal per region and a grand total, with money to 2 decimal places and the columns aligned. It also names the best-selling product by revenue overall. `--json` prints the same numbers as JSON instead. Rows with a bad date or a non-numeric number are skipped and counted, and the count is printed to stderr with their line numbers.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** small to medium
- **Kind:** data processing, aggregation, CLI tool
- **Areas:** CSV parsing with quotes, strings, dicts, sorting, number formatting, JSON, files
- **Network sites:** none
- **Seed:** none

## Acceptance checks

Use this file:

```
date,region,product,units,unit_price
2026-01-05,North,Widget,3,2.50
2026-01-20,North,"Gadget, large",1,10.00
2026-02-02,North,Widget,2,2.50
2026-01-11,South,"The ""Best"" Thing",4,1.25
2026-01-12,South,Widget,x,2.50
2026-13-01,South,Widget,1,2.50
```

1. North 2026-01: 4 units, 17.50; North 2026-02: 2 units, 5.00; North subtotal 6 units, 22.50.
2. South 2026-01: 4 units, 5.00; grand total 10 units, 27.50.
3. The best seller is Widget (12.50).
4. Lines 6 and 7 are reported as skipped (2 rows) on stderr.
5. `The "Best" Thing` and `Gadget, large` are read as single product names.
6. `--json` carries the same numbers and parses with `python3 -m json.tool`.
7. Money never shows a float tail such as `22.499999`.
