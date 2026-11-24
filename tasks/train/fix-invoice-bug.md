The project folder contains an invoice library and a command-line tool, `receipt`, that uses it. README.md describes the pricing rules and where the code is.

A customer reports that `receipt fixtures/order-3.json` prints a total of 52.97, but by the rules in README.md it should be 58.96. Find the cause, fix it, and make sure no other receipt changes unless it was wrong too. Explain what the cause was.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium (a one-line fix; the work is finding it)
- **Kind:** working on existing code: fix a bug; Dark strength: traces to debug a wrong answer
- **Areas:** traces, store/CLI authoring, JSON, files
- **Network sites:** none
- **Seed:** `seeds/invoice-buggy`: shipping is computed from the subtotal before the discount instead of after it

## Acceptance checks

Hidden inputs are in `seeds/invoice-buggy/checks/`.

1. `receipt fixtures/order-3.json` prints exactly `checks/order-3.expected` (shipping 5.99, total 58.96).
2. Every other fixture with an `.expected` file still matches it byte for byte, with the same exit code.
3. `checks/order-6.json` prints exactly `checks/order-6.expected`.
4. The explanation names the cause: free shipping was judged on the subtotal before the discount.
5. The fix is in the library's shipping step, not a special case for this order.
6. Recorded, not judged: whether the agent used traces (Dark) or a debugger, prints, or reading alone.
