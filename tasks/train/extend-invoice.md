The project folder contains an existing invoice library and a command-line tool, `receipt`, that uses it. README.md describes how they work and where the code is. Extend them:

1. **Bulk pricing.** A line with a quantity of 10 or more costs 90% of its normal line total, rounded down to the cent. Its receipt line ends with ` (bulk)`, and it shows the reduced line total. Everything after the line totals (subtotal, discount, shipping, tax) uses the reduced amount.
2. **JSON output.** `receipt --json ORDER.json` prints one JSON object with the fields `subtotal_cents`, `discount_cents`, `shipping_cents`, `tax_cents` and `total_cents`, instead of the text receipt.

Keep the library usable on its own, and keep every existing receipt exactly as it is today. Update README.md.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** working on existing code: extend a package
- **Areas:** JSON, strings, lists, files, store/CLI authoring (Dark: the library is a package in the store)
- **Network sites:** none
- **Seed:** `seeds/invoice` (see `seeds/README.md`)

## Acceptance checks

Hidden inputs are in `seeds/invoice/checks/`.

1. For each `fixtures/order-N.json`, the text receipt's stdout is byte-identical to `order-N.expected`, and the exit codes are unchanged (order-5 still exits 2).
2. `checks/order-bulk.json` prints exactly `checks/order-bulk.expected`.
3. `checks/order-6.json` prints exactly `checks/order-6.expected`.
4. `receipt --json fixtures/order-1.json` prints JSON equal to `{"subtotal_cents": 3399, "discount_cents": 339, "shipping_cents": 599, "tax_cents": 245, "total_cents": 3904}`.
5. `receipt --json checks/order-bulk.json` gives subtotal 4120, discount 0, shipping 599, tax 330, total 5049.
6. Dark: the bulk rule lives in the `Bench.Invoice` package (the script still calls it), not copied into the script. Python: it lives in `invoice.py`.
