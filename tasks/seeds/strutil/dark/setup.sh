#!/bin/sh
# Seed setup, run in the project folder before timing starts.
# DARK is the attempt's own dark binary; HOME (or the runner's wrapper) points it at the attempt's private store.
set -e
"$DARK" import strutil.dark /Bench.Strutil
"$DARK" commit "Seed: Bench.Strutil" -y
