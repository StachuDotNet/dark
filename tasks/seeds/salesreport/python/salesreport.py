"""Command line: python3 salesreport.py SALES.csv

Prints revenue by region and by month, and the best-selling product.
Everything is in this one function; it grew that way.
"""
import sys


def main(argv):
    if len(argv) != 1:
        print("Usage: salesreport SALES.csv", file=sys.stderr)
        return 2
    try:
        with open(argv[0]) as f:
            text = f.read()
    except OSError:
        print(f"Cannot read {argv[0]}", file=sys.stderr)
        return 1
    lines = [l for l in text.split("\n") if l.strip() != ""]
    if not lines:
        print(f"{argv[0]} is empty", file=sys.stderr)
        return 1
    read = 0
    skipped = 0
    by_region = {}
    by_month = {}
    units_by_product = {}
    for line in lines[1:]:
        read += 1
        # split one CSV line, honouring double quotes
        fields = []
        cur = ""
        in_quotes = False
        i = 0
        while i < len(line):
            ch = line[i]
            if in_quotes:
                if ch == '"':
                    if i + 1 < len(line) and line[i + 1] == '"':
                        cur += '"'
                        i += 1
                    else:
                        in_quotes = False
                else:
                    cur += ch
            else:
                if ch == '"':
                    in_quotes = True
                elif ch == ",":
                    fields.append(cur)
                    cur = ""
                else:
                    cur += ch
            i += 1
        fields.append(cur)
        if len(fields) != 5:
            skipped += 1
            continue
        date, region, product, units_s, price_s = [x.strip() for x in fields]
        if len(date) != 10 or date[4] != "-" or date[7] != "-":
            skipped += 1
            continue
        if not units_s.isdigit():
            skipped += 1
            continue
        units = int(units_s)
        # price like 12, 12.5 or 12.50 -> cents
        parts = price_s.split(".")
        if len(parts) == 1 and parts[0].isdigit():
            price = int(parts[0]) * 100
        elif len(parts) == 2 and parts[0].isdigit() and parts[1].isdigit() and 1 <= len(parts[1]) <= 2:
            price = int(parts[0]) * 100 + int(parts[1].ljust(2, "0"))
        else:
            skipped += 1
            continue
        revenue = units * price
        by_region[region] = by_region.get(region, 0) + revenue
        month = date[:7]
        by_month[month] = by_month.get(month, 0) + revenue
        units_by_product[product] = units_by_product.get(product, 0) + units
    print(f"Rows: {read} read, {skipped} skipped")
    print("By region:")
    for region in sorted(by_region):
        c = by_region[region]
        print(f"  {region.ljust(10)}{(str(c // 100) + '.' + str(c % 100).rjust(2, '0')).rjust(12)}")
    if units_by_product:
        best = sorted(units_by_product.items(), key=lambda kv: (-kv[1], kv[0]))[0]
        print(f"Top product: {best[0]} ({best[1]} units)")
    else:
        print("Top product: none")
    print("By month:")
    for month in sorted(by_month):
        c = by_month[month]
        print(f"  {month.ljust(10)}{(str(c // 100) + '.' + str(c % 100).rjust(2, '0')).rjust(12)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
