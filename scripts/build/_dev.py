#!/usr/bin/env python3.12
"""The implementation behind scripts/dev/{build,plan,status}.

One file so that the three commands can't disagree about what a build is. The thin
bash wrappers in scripts/dev/ exist to hop into the container; the rest is here.
"""

import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _buildindex
import _buildplan
import _buildstate

BUILD_LOG = "rundir/logs/build.log"
WATCH_PID = "rundir/build-watch.pid"
TEST_LOCK = "rundir/test.lock"

# Past this many changed files, working out what each one implies costs more than
# just building everything, which is what the plan would come to anyway.
FULL_BUILD_THRESHOLD = 200

# Routes to fsharp_tool_restore, which cascades to a full build of everything.
FULL_BUILD_FILES = ["backend/global.json"]


def _live_pid(pidfile):
  """The pid in `pidfile` if that process is still alive, else None."""
  try:
    with open(pidfile) as f:
      pid = int(f.read().strip())
  except (FileNotFoundError, ValueError):
    return None
  try:
    os.kill(pid, 0)
  except OSError:
    return None
  return pid


def watcher_pid():
  return _live_pid(WATCH_PID)


def testrun_pid():
  return _live_pid(TEST_LOCK)


def choose_files(paths):
  """(files to build, one line saying why).

  With no paths, build whatever the last successful build didn't account for. That
  set is taken over the whole repo rather than just the build roots, so a changed
  shell script still gets linted, and it's compared by content rather than
  timestamp, so switching branches and back costs nothing.
  """
  if paths:
    return paths, f"{len(paths)} path(s) given"

  changed = _buildindex.changed()
  if changed is None:
    return FULL_BUILD_FILES, "no successful build on record, so building everything"
  if not changed:
    return [], "nothing has changed since the last successful build"
  if len(changed) > FULL_BUILD_THRESHOLD:
    return FULL_BUILD_FILES, (
      f"{len(changed)} files changed, past the {FULL_BUILD_THRESHOLD}-file"
      " threshold, so building everything")
  return changed, f"{len(changed)} file(s) changed since the last successful build"


class Plan:

  def __init__(self, why, kept, ignored, actions, unrouted):
    self.why = why
    self.kept = kept          # files the build was handed
    self.ignored = ignored    # filtered out before mark() saw them
    self.actions = actions    # what will run, if each step succeeds
    self.unrouted = unrouted  # handed over, but map to no action

  def build_files(self):
    """The files that actually drive an action."""
    return [f for f in self.kept if f not in set(self.unrouted)]


def make_plan(files, why, run_tests=False):
  kept, ignored = [], []
  for f in files:
    rel = _buildplan.relative(f) if os.path.isabs(f) else f
    (ignored if _buildplan.spec().match_file(rel) else kept).append(rel)

  should = _buildplan.Should()
  for f in kept:
    _buildplan.mark(should, f)

  actions = _buildplan.expand(should, run_tests=run_tests).chosen()
  return Plan(why, kept, ignored, actions, should.unrouted)


def print_plan(plan):
  print(f"why:     {plan.why}")
  if plan.ignored:
    print(f"ignored: {len(plan.ignored)} file(s) the build filters out")

  driving = plan.build_files()
  print(f"files:   {len(driving)}" +
        (f" (+{len(plan.unrouted)} that need no action)" if plan.unrouted else ""))
  for f in driving[:10]:
    print(f"           {f}")
  if len(driving) > 10:
    print(f"           ... and {len(driving) - 10} more")

  if plan.actions:
    print("actions:")
    for a in plan.actions:
      print(f"           {a}")
  else:
    print("actions: (none)")


def cmd_plan(args):
  paths = [a for a in args if not a.startswith("-")]
  run_tests = "--test" in args
  files, why = choose_files(paths)
  print_plan(make_plan(files, why, run_tests))
  return 0


def cmd_build(args):
  paths = [a for a in args if not a.startswith("-")]
  run_tests = "--test" in args
  force = "--force" in args

  # Anything else writing backend/Build while we do corrupts both. The watcher is the
  # obvious one; a test run is the one that bites, because it's executing the very
  # binaries we'd overwrite. _dotnet-wrapper's `killall Tests` exists to unstick that
  # collision, which is to say the build's answer to it was to destroy the test run.
  pid = watcher_pid()
  if pid and not force:
    for line in [
      f"The file watcher is running (pid {pid}), and two builds writing the",
      "same output directory corrupt each other. Stop it with",
      "scripts/dev/watch --stop, or pass --force if you're sure it's idle.",
    ]:
      print(line, file=sys.stderr)
    return 1

  pid = testrun_pid()
  if pid and not force:
    for line in [
      f"Tests are running in this clone (pid {pid}). Building now would overwrite",
      "the binaries they're executing, and the build would kill them to get the",
      "file lock. Wait for them, or pass --force.",
    ]:
      print(line, file=sys.stderr)
    return 1

  files, why = choose_files(paths)
  if not files:
    print(why)
    return 0

  plan = make_plan(files, why, run_tests)
  if not plan.actions:
    print(f"Nothing to build: {why}, but none of it changes what gets built.")
    _buildindex.record_covered(plan.unrouted)
    return 0

  print_plan(plan)
  print()

  os.makedirs(os.path.dirname(BUILD_LOG), exist_ok=True)
  cmd = ["scripts/build/compile", "--source=build", f"--log={BUILD_LOG}"]
  if run_tests:
    cmd.append("--test")
  if "--optimize" in args:
    cmd.append("--optimize")
  cmd += plan.kept

  # Tee in-process rather than through a pipeline, so the exit code stays compile's.
  with open(BUILD_LOG, "w") as log:
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, bufsize=1)
    for line in proc.stdout:
      sys.stdout.write(line)
      sys.stdout.flush()
      log.write(line)
    proc.wait()

  if proc.returncode != 0:
    print(f"\nBuild failed. Full output: {BUILD_LOG}", file=sys.stderr)
  return proc.returncode


def cmd_status(args):
  if "--json" in args:
    print(json.dumps(_buildstate.read(), indent=2))
    return 0
  print("\n".join(_buildstate.describe()))
  pid = watcher_pid()
  print(f"watcher: {'running, pid ' + str(pid) if pid else 'not running'}")
  return 0


def cmd_check(args):
  """Gate for scripts depending on the build. Silent when there's nothing to say."""
  ready, fatal, lines = _buildstate.check()
  for line in lines:
    print(line, file=sys.stderr)
  return 1 if fatal else 0


def cmd_stale(args):
  changed = _buildstate.stale()
  if changed:
    print("\n".join(changed))
  return 0


COMMANDS = {
  "build": cmd_build,
  "plan": cmd_plan,
  "status": cmd_status,
  "check": cmd_check,
  "stale": cmd_stale,
}


# Second line of defence: the scripts/dev wrappers reject unknown flags before the
# container hop, but this is callable directly, and silently treating `--hepl` as
# "build everything" is the failure mode worth ruling out twice.
KNOWN_FLAGS = {
  "build": {"--test", "--force", "--optimize"},
  "plan": {"--test"},
  "status": {"--json"},
  "check": set(),
  "stale": set(),
}


def main():
  args = sys.argv[1:]
  if not args or args[0] not in COMMANDS:
    print(f"usage: {sys.argv[0]} {{{'|'.join(COMMANDS)}}} [args]", file=sys.stderr)
    sys.exit(1)

  command, rest = args[0], args[1:]
  unknown = [a for a in rest if a.startswith("-") and a not in KNOWN_FLAGS[command]]
  if unknown:
    print(f"Unknown flag(s) for {command}: {' '.join(unknown)}", file=sys.stderr)
    known = KNOWN_FLAGS[command]
    print(f"Known: {' '.join(sorted(known)) if known else '(none)'}", file=sys.stderr)
    sys.exit(1)

  sys.exit(COMMANDS[command](rest))


if __name__ == "__main__":
  main()
