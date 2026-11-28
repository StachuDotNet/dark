#!/bin/sh
# Seed setup, run in the project folder before timing starts.
set -e
"$DARK" import shapes.dark /Bench.Geo.Shapes
"$DARK" import report.dark /Bench.Geo.Report
"$DARK" import cli.dark /Bench.Geo.Cli
"$DARK" import tests.dark /Bench.Geo.Tests
"$DARK" commit "Seed: Bench.Geo" -y
