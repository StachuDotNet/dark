# Seeds for the "existing code" scenarios

Each seed is a small project that already works (or, for `invoice-buggy`, has one planted bug). The runner has to put it in place before timing starts; today it only makes an empty folder.

For an attempt in language L:

1. Copy `<seed>/<L>/` and `<seed>/fixtures/` into the empty project folder.
2. Dark only: run `<seed>/dark/setup.sh` in the project folder with `DARK` set to the attempt's binary and the attempt's private store as its HOME. It loads the library into the store, commits it, and grants file reads where the tool needs them, so the seed works as it would for someone who had been using it.
3. Never copy `checks/`. Those are the hidden inputs the acceptance checks use.

| Seed | Python | Dark | Used by |
|---|---|---|---|
| invoice | `invoice.py`, `receipt.py` | package `Bench.Invoice` + `receipt.dark` script | extend-invoice |
| invoice-buggy | same, with one planted bug | same, with the same bug | fix-invoice-bug |
| strutil | `strutil.py` | package `Bench.Strutil` | test-strutil |
| salesreport | `salesreport.py`, one long function | `salesreport.dark`, one long function | refactor-salesreport |

Both versions of each seed were run against every fixture and give byte-identical stdout (Dark: release v0.0.110, in a throwaway HOME).
