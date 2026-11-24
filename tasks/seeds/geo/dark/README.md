# geo

Prints the area of each shape given on the command line, then the total. A shape is `WxH` (a rectangle) or `circle:R`.

    geo 3x4 2x5 circle:2

## Where the code is

Package modules in this Dark instance's package store:

- `Bench.Geo.Shapes`: the `Rect` and `Shape` types, `area`, `isSquare` and `parse`.
- `Bench.Geo.Report`: one line per shape and the total.
- `Bench.Geo.Cli`: `main`, run as `Bench.Geo.Cli.main`.
- `Bench.Geo.Tests`: the tests.

The `.dark` files here are copies of the sources they were loaded from.
