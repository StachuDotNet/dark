#!/usr/bin/env python3.12
"""Rebuild on save. The implementation behind scripts/dev/watch.

The human loop: edit, glance at the pane, see it go green. Opt-in rather than
always-on, because the same behaviour serves anything scripted badly: five saves in a
row become five builds, four of them on states nobody asked for, each producing
real-looking failures midway.

Two things make the human version bearable, and both are about not building states
nobody wanted. Saves are debounced, so a formatter touching thirty files is one build
rather than thirty. And events arriving while a build runs are held until it
finishes, so the next build sees them all at once instead of queueing behind each
other.
"""

import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _buildplan
import _buildstate

ROOT = "/home/dark/app"

# How long to wait for a burst of saves to settle. Long enough to absorb a
# format-on-save across several files, short enough that a single save still feels
# immediate.
DEBOUNCE_MS = 1000
STEP_MS = 100


def describe(files):
  """One line naming what changed and what it implies."""
  should = _buildplan.Should()
  for f in files:
    _buildplan.mark(should, f)
  actions = _buildplan.expand(should).chosen()

  driving = [f for f in files if f not in set(should.unrouted)]
  if not actions:
    return f"{len(files)} file(s) changed, nothing to build"

  shown = ", ".join(driving[:3])
  more = f" (+{len(driving) - 3} more)" if len(driving) > 3 else ""
  return f"{shown}{more} -> {', '.join(actions)}"


def main():
  import watchfiles

  ignored = set(os.path.join(ROOT, f) for f in [".git", "backend/Build"])
  file_filter = watchfiles.DefaultFilter(ignore_paths=ignored)

  print(f"Watching {ROOT}. Ctrl+C to stop.", flush=True)
  print("Builds are debounced; scripts/dev/status has the current state.", flush=True)

  for changes in watchfiles.watch(ROOT, watch_filter=file_filter,
                                  debounce=DEBOUNCE_MS, step=STEP_MS):
    # Dedupe: one save arrives as several events, and an editor's write-rename dance
    # arrives as several more.
    files = sorted({f for (_, f) in changes})
    kept = _buildplan.keep(files, ROOT)
    if not kept:
      continue

    print(f"\n== {describe(kept)}", flush=True)
    cmd = ["scripts/build/compile", "--source=watch"] + sys.argv[1:] + files
    try:
      subprocess.run(cmd)
    except OSError as e:
      # A branch switch or a repo-wide format can produce more paths than the
      # argument list holds. Build everything instead of dropping the change.
      print(f"Too many paths ({e}); building everything.", flush=True)
      subprocess.run(["scripts/build/compile", "--source=watch",
                      "backend/global.json"])

    state = _buildstate.read() or {}
    if state.get("status") == _buildstate.FAILED:
      print(f"== FAILED at {state.get('failedAction')}", flush=True)
    else:
      print("== ok", flush=True)


if __name__ == "__main__":
  main()
