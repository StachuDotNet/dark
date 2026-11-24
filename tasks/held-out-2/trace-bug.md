The project folder holds `quote`, a tool that prices orders. README.md says how prices are worked out and where the code is. The tool was run five times yesterday, and this instance kept a record of those runs. One customer says their total was wrong, but nobody wrote down which order was theirs.

Find which run gave a wrong total and what it should have been. Then fix the bug and add a test that would have caught it.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** small to medium
- **Kind:** working on existing code: find a bug from recorded runs (Dark only)
- **Areas:** traces, reading a run back, existing package code, tests
- **Network sites:** none
- **Seed:** `seeds/quote`: the bulk discount starts at more than 10 units where README says 10 or more. `setup.sh` turns trace recording on and runs the tool five times; the fourth run has a line of exactly 10 units.

## Acceptance checks

1. The summary names the fourth run (`C-3:10:450 D-4:1:1999`) as the wrong one, says it printed 6499 and should have printed 6049.
2. `Bench.Quote.bulkPrice` (or wherever the fix lands) gives 10% off a line of exactly 10 units.
3. A package test that `test run` finds fails on the old code and passes on the fixed code.
4. The other four runs' totals are unchanged.
