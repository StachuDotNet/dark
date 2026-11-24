#!/bin/sh
# Seed setup, run in the project folder before timing starts.
set -e
"$DARK" module /Bench.Geo.Shapes - < shapes.dark
"$DARK" module /Bench.Geo.Report - < report.dark
"$DARK" module /Bench.Geo.Cli - < cli.dark
"$DARK" module /Bench.Geo.Tests - < tests.dark
"$DARK" commit "Seed: Bench.Geo" -y
