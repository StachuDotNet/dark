# Invoice

A small library that prices an order and prints a receipt, plus a command-line tool, `receipt`, that reads an order from a JSON file.

## Orders

    {"region": "CA", "code": "SAVE10", "items": [{"sku": "MUG", "qty": 2, "price_cents": 1250}]}

All money is in whole cents.

## Pricing rules

1. **Line total** is `qty × price_cents`. **Subtotal** is the sum of the line totals.
2. **Discount code:** `""` means none. `SAVE10` takes 10% off the subtotal, rounded down to the cent. `FLAT5` takes 500 cents off, but never more than the subtotal. Any other code is refused: the tool prints `Unknown discount code: <code>` to stderr and exits 2.
3. **Shipping** is free when the subtotal *after the discount* is 5000 cents or more; otherwise it is 599 cents.
4. **Tax** is charged on the subtotal after the discount, at a rate set by region: CA 8.00%, NY 9.00%, OR 0.00%, anywhere else 5.00%. It is rounded to the nearest cent, halves up.
5. **Total** = subtotal − discount + shipping + tax.

## Receipt format

    MUG x2 @ 12.50 = 25.00
    TEA x1 @ 8.99 = 8.99
    Subtotal: 33.99
    Discount (SAVE10): -3.39
    Shipping: 5.99
    Tax (CA 8.00%): 2.45
    Total: 39.04

The Discount line is left out when there is no code.

## Exit codes

0 printed a receipt; 1 the file could not be read or is not a valid order; 2 wrong usage or an unknown discount code.

## Examples

`fixtures/` holds sample orders, and for each one the receipt the tool prints (`.expected`).

## Where the code is

- The library is the package module `Bench.Invoice` in this Dark instance's package store. `invoice.dark` is a copy of the source it was loaded from.
- `receipt.dark`: the command-line tool, a script that calls `Bench.Invoice`. Run it with `dark run receipt.dark fixtures/order-1.json`.
- This instance already allows reading files in the project folder.
