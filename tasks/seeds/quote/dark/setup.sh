#!/bin/sh
# Seed setup, run in the project folder before timing starts.
# DARK is the attempt's own dark binary; HOME (or the runner's wrapper) points it at the attempt's private store.
# The five runs stand in for yesterday's customers; the instance records them as traces.
set -e
"$DARK" import quote.dark /Bench.Quote
"$DARK" commit "Seed: Bench.Quote" -y
"$DARK" traces record on
"$DARK" run Bench.Quote.main A-1:2:1250 B-7:1:899 > /dev/null
"$DARK" run Bench.Quote.main E-5:12:300 > /dev/null
"$DARK" run Bench.Quote.main A-1:1:1250 > /dev/null
"$DARK" run Bench.Quote.main C-3:10:450 D-4:1:1999 > /dev/null
"$DARK" run Bench.Quote.main B-7:3:899 F-6:2:2500 > /dev/null
