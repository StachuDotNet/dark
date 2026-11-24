# salesreport

A command-line tool that reads a CSV of sales and prints revenue by region and by month, and the best-selling product. It works, and people rely on its exact output. All of it is one long function.

## Input

A CSV file with a header line, then `date,region,product,units,unit_price`. Fields may be double-quoted, and a quoted field may contain commas and doubled quotes (`""`). Blank lines are ignored. Spaces around a field are trimmed.

A row is skipped (and counted as skipped) when it doesn't have exactly five fields, its date isn't shaped `YYYY-MM-DD`, its units aren't a whole number, or its price isn't `12`, `12.5` or `12.50` style.

## Output

    Rows: 4 read, 0 skipped
    By region:
      East             44.99
      ...
    Top product: Widget (14 units)
    By month:
      2026-01          84.97
      ...

Regions and months are sorted. The top product has the most units; a tie goes to the name that sorts first. Exit codes: 0 printed a report; 1 the file can't be read or is empty; 2 wrong usage.

`fixtures/` holds sample inputs and the exact output for each (`.expected`).

## Where the code is

`salesreport.dark`, a script. Run it with `dark run salesreport.dark fixtures/sales-1.csv`. This instance already allows reading files in the project folder.
