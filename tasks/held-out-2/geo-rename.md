The project folder holds `geo`, a small library and command-line tool in four parts: shapes, a report, the command line and tests. README.md says where the code is.

Rename the rectangle's fields `w` and `h` to `width` and `height`, and rename the function `area` to `surfaceArea`, everywhere they are used. The tool's output must stay exactly the same and the existing tests must still pass.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** small to medium
- **Kind:** working on existing code: a refactor across modules (Dark only)
- **Areas:** store edits across modules, rename, type changes and their dependents, tests
- **Network sites:** none
- **Seed:** `seeds/geo`: modules `Bench.Geo.Shapes`, `Bench.Geo.Report`, `Bench.Geo.Cli`, `Bench.Geo.Tests`

## Acceptance checks

1. No item in `Bench.Geo` mentions a field `w` or `h` or a function `area` (`view --raw` of each module).
2. `Bench.Geo.Shapes.surfaceArea` exists and `Bench.Geo.Shapes.area` does not, or is deprecated in favour of it.
3. `run Bench.Geo.Cli.main 3x4 2x5 circle:2` prints the same bytes as before (the checker keeps the seed's output).
4. `test run Bench.Geo` passes, with at least the seed's tests.
