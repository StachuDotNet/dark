#!/usr/bin/env python3.12
"""rundir/build-state.json: whether the tree is built, and how far behind it is.

Everything downstream used to guess. run-cli grepped packages.log for "Exception",
run-backend-server spun a sleep loop, the docs said to poll a log for "errored in".
dark-multi died outright when the log line its readiness check grepped for was
deleted: nothing was wrong with its code, the evidence just moved.

A file recording what happened survives refactors a log grep cannot, so this is
the one place build state is written and the one place it is read.

Written by every build path, watcher included. Read by run-cli, run-backend-server
and scripts/dev/status. Paths in it are repo-relative, because it is read both from
inside the container and from the host, where the repo root differs.
"""

import json
import os
import subprocess
import sys
from datetime import datetime, timezone

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _buildindex  # noqa: E402

# Directories whose contents change what gets built. Editing a shell script or a doc
# doesn't make the binaries stale, so staleness is measured over these only.
BUILD_ROOTS = ["backend", "packages"]

RUNNING = "running"
OK = "ok"
FAILED = "failed"

# Actions that change what's built. Linting a shell script succeeds without making
# the binaries any newer, so a lint-only build must not move the last-success mark:
# that would declare a .fs edited in the same second already built.
BUILD_ACTIONS = {
  "fsharp_tool_restore",
  "fsharp_paket_restore",
  "fsharp_paket_install",
  "backend_full_build",
  "backend_quick_build",
  "reload_backend_server",
  "run_migrations",
  "reload_all_packages",
}


def rundir():
  return os.getenv("DARK_CONFIG_RUNDIR", "rundir")


def path():
  return os.path.join(rundir(), "build-state.json")


def now():
  return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def _epoch(iso):
  if not iso:
    return None
  return datetime.fromisoformat(iso.replace("Z", "+00:00")).timestamp()


def read():
  try:
    with open(path()) as f:
      return json.load(f)
  except (FileNotFoundError, json.JSONDecodeError):
    return None


def _write(state):
  os.makedirs(rundir(), exist_ok=True)
  tmp = path() + ".tmp"
  with open(tmp, "w") as f:
    json.dump(state, f, indent=2)
    f.write("\n")
  # Rename rather than write in place: a reader that catches us mid-write would
  # otherwise see truncated JSON and conclude the build failed.
  os.replace(tmp, path())
  return state


def _commit():
  try:
    return subprocess.check_output(["git", "rev-parse", "--short", "HEAD"],
                                   encoding="utf-8",
                                   stderr=subprocess.DEVNULL).strip()
  except (subprocess.CalledProcessError, FileNotFoundError):
    return None


def begin(actions, trigger, source, log=None):
  """Record that a build is starting. Returns the new state."""
  previous = read() or {}
  return _write({
    "status": RUNNING,
    "startedAt": now(),
    "finishedAt": None,
    "lastSuccessStartedAt": previous.get("lastSuccessStartedAt"),
    "lastSuccessFinishedAt": previous.get("lastSuccessFinishedAt"),
    "commit": _commit(),
    "source": source,
    "trigger": trigger,
    "plannedActions": actions,
    "actions": [],
    "failedAction": None,
    "exitCode": None,
    "log": log,
    "pid": os.getpid(),
  })


def built_successfully(actions, failed_action=None, skipped=None):
  """Did the build chain itself get all the way through?

  A different question from "did everything succeed": a shellcheck failure doesn't
  make the binaries any older, and shouldn't leave the tree looking permanently
  behind.
  """
  skipped = skipped or []
  return bool(BUILD_ACTIONS.intersection(actions)
              and failed_action not in BUILD_ACTIONS
              and not BUILD_ACTIONS.intersection(skipped))


def finish(ok, actions, failed_action=None, exit_code=None, skipped=None):
  """Record how the build ended, keeping whatever begin() wrote."""
  state = read() or {}
  skipped = skipped or []
  finished = now()
  state.update({
    "status": OK if ok else FAILED,
    "finishedAt": finished,
    "actions": actions,
    "skipped": skipped,
    "failedAction": failed_action,
    "exitCode": exit_code if exit_code is not None else (0 if ok else 1),
  })

  if built_successfully(actions, failed_action, skipped):
    state["lastSuccessStartedAt"] = state.get("startedAt")
    state["lastSuccessFinishedAt"] = finished

  return _write(state)


def last_success(state=None):
  """When the last successful build started, in epoch seconds, or None."""
  state = state if state is not None else read()
  if not state:
    return None
  return _epoch(state.get("lastSuccessStartedAt"))


def stale(state=None, root="."):
  """Source files whose content differs from what the last good build consumed.

  By content, not timestamp: a branch switch and back should cost nothing. None
  means there's nothing to compare against yet.
  """
  return _buildindex.changed(roots=BUILD_ROOTS, root=root)


def duration(state):
  start, end = _epoch(state.get("startedAt")), _epoch(state.get("finishedAt"))
  if start is None or end is None:
    return None
  return end - start


def _pid_alive(pid):
  if not pid:
    return False
  try:
    os.kill(pid, 0)
    return True
  except (OSError, ProcessLookupError):
    return False


def check(root="."):
  """Is it safe to run something that depends on the build?

  Returns (ready, fatal, lines). `fatal` means don't run at all; a stale tree is
  reported but not fatal, since editing and then running is a normal thing to do.
  """
  state = read()
  if state is None:
    return (False, False, ["Build state unknown (no rundir/build-state.json)."
                           " Run: scripts/dev/build"])

  status = state.get("status")

  if status == RUNNING:
    if _pid_alive(state.get("pid")):
      started = state.get("startedAt")
      return (False, True, [
        f"A build is in flight (started {started}, pid {state.get('pid')}).",
        f"Watch it: {state.get('log') or 'rundir/logs/build.log'}",
      ])
    return (False, True, [
      "The last build didn't finish: its process is gone and it never recorded an"
      " outcome.",
      "Run: scripts/dev/build",
    ])

  if status == FAILED:
    lines = ["The last build failed."]
    if state.get("failedAction"):
      lines.append(f"Failed at: {state['failedAction']}")
    if state.get("log"):
      lines.append(f"Log: {state['log']}")
    lines.append("Run: scripts/dev/build")
    return (False, True, lines)

  changed = stale(state, root=root)
  if changed:
    shown = ", ".join(changed[:3])
    more = f" (+{len(changed) - 3} more)" if len(changed) > 3 else ""
    return (False, False, [
      f"{len(changed)} file(s) changed since the last build: {shown}{more}",
      "The build is behind the tree. Run: scripts/dev/build",
    ])

  return (True, False, [])


def describe(root="."):
  """A human-readable status report."""
  state = read()
  if state is None:
    return ["No build recorded. Run: scripts/dev/build"]

  status = state.get("status")
  lines = []

  if status == RUNNING and not _pid_alive(state.get("pid")):
    lines.append(f"status:  interrupted (started {state.get('startedAt')})")
  else:
    lines.append(f"status:  {status}")

  if state.get("failedAction"):
    lines.append(f"failed:  {state['failedAction']}")

  d = duration(state)
  when = state.get("finishedAt") or state.get("startedAt")
  lines.append(f"when:    {when}" + (f" ({d:.1f}s)" if d is not None else ""))

  if state.get("commit"):
    lines.append(f"commit:  {state['commit']}")
  lines.append(f"source:  {state.get('source')}")

  actions = state.get("actions") or state.get("plannedActions") or []
  lines.append(f"actions: {', '.join(actions) if actions else '(none)'}")
  if state.get("skipped"):
    lines.append(f"skipped: {', '.join(state['skipped'])}")

  if state.get("log"):
    lines.append(f"log:     {state['log']}")

  changed = stale(state, root=root)
  if changed is None:
    lines.append("tree:    no successful build to compare against")
  elif changed:
    lines.append(f"tree:    {len(changed)} file(s) newer than the last build")
    for f in changed[:10]:
      lines.append(f"           {f}")
    if len(changed) > 10:
      lines.append(f"           ... and {len(changed) - 10} more")
  else:
    lines.append("tree:    up to date")

  return lines


