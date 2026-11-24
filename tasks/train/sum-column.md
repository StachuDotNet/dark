Build a command-line tool that takes a CSV file and a column name, and prints the sum of the numbers in that column. The first line of the file is the header. Cells that are empty are skipped. If a non-empty cell isn't a number, the tool prints its line number to stderr and exits 1. If the column doesn't exist, it lists the column names and exits 2. Plain CSV only: no quoted fields.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** tiny
- **Kind:** data processing, CLI tool
- **Areas:** strings, lists, files, permissions
- **Network sites:** none
- **Seed:** none

## Acceptance checks

Make `t.csv` with `printf 'name,amount,qty\na,1.5,2\nb,,3\nc,-0.25,4\n'`.

1. `<tool> t.csv amount` prints 1.25 (`1.25` or `1.250`; a float rounding tail such as `1.2500000001` fails) and exits 0.
2. `<tool> t.csv qty` prints 9.
3. `<tool> t.csv price` lists `name`, `amount` and `qty` and exits 2.
4. With a line `d,abc,1` added as line 5, `<tool> t.csv amount` names line 5 and exits 1.
