#!/bin/sh
# Seed setup, run in the project folder before timing starts.
# DARK is the attempt's own dark binary; HOME (or the runner's wrapper) points it at the attempt's private store.
# The read grant stands in for "this tool already worked here before you arrived".
set -e
"$DARK" module /Bench.Invoice - < invoice.dark
"$DARK" commit "Seed: Bench.Invoice" -y
"$DARK" permissions allow file read "$PWD"
