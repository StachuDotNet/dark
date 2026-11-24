# quote

Prices an order. Each argument is one order line, `SKU:QTY:UNIT_CENTS`, and the tool prints the total in cents.

- A line costs its quantity times its unit price.
- **Bulk discount:** a line of 10 units or more gets 10% off that line, with the discount rounded down to the cent.
- The **subtotal** is the sum of the lines after their discounts.
- **Shipping** is 500 cents, or free when the subtotal is 5000 cents or more.
- The **total** is the subtotal plus shipping.

`quote A-1:2:1250` prints `3000`. A bad line prints an error and exits 1; no arguments prints the usage and exits 2.

## Where the code is

The package module `Bench.Quote` in this Dark instance's package store, run as `Bench.Quote.main`. `quote.dark` is a copy of the source it was loaded from.
