#!/bin/sh
# Seed setup, run in the project folder before timing starts.
# The read grant stands in for "this tool already worked here before you arrived".
set -e
"$DARK" permissions allow file read "$PWD"
