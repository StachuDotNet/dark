#!/usr/bin/env python3.12
"""Assert that the paths our docs point at still exist.

  scripts/testing/test-docs-drift.py

Docs go stale silently. Everything this session turned up was of one shape: a
document naming a script, a directory or a flag that had been renamed or deleted, and
nothing anywhere noticing. `psql -d devdb` outlived Postgres, `docs/dnsmasq.md`
outlived BwdServer, `scripts/migrations/new` wrote into a directory the runtime
stopped reading, and three docs recommended a `--test` flag that assigned the wrong
variable.

This is the cheap half of the fix: every repo-relative path a doc mentions has to
resolve. It can't tell you a sentence is wrong, only that the thing it names is gone,
which is most of it.
"""

import os
import re
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

# Prose that looks like a path but isn't ours to check.
SKIP_PREFIXES = (
  "http://", "https://", "mailto:", "//", "~/",
  "/etc/", "/usr/", "/var/", "/home/dark/", "/proc/", "/tmp/",
  "backend/Build/",       # a docker volume, absent on the host
  "rundir/",              # generated
  "node_modules/",
)

# Names that read as paths but are illustrative rather than real.
SKIP_EXACT = {
  "scripts/", "docs/", "packages/", "backend/", "config/", "clis/",
  "backend/src/", "backend/tests/", "packages/darklang/",
}

# Three ways a doc names a file: a backticked span, a markdown link target, and the
# first word of a backticked command. Fenced blocks are skipped, since they're mostly
# output and shell fragments. Deliberately conservative: a check that cries wolf gets
# deleted, and then it catches nothing at all.
INLINE_CODE = re.compile(r"`([^`\n]+)`")
MARKDOWN_LINK = re.compile(r"\[[^\]]*\]\(([^)\s]+)\)")

# A backticked span counts as a path if it looks like one: has a slash, no spaces,
# and either names a directory we own or ends in a known extension.
OWNED_ROOTS = ("scripts/", "docs/", "backend/", "packages/", "config/",
               ".devcontainer/", ".circleci/", "vscode-extension/")
EXTENSIONS = (".md", ".fs", ".fsi", ".fsproj", ".sln", ".dark", ".py", ".sh",
              ".json", ".yml", ".yaml", ".sql", ".txt")


def looks_like_a_path(text):
  if " " in text or text.startswith(SKIP_PREFIXES) or text in SKIP_EXACT:
    return False
  if not text.startswith(OWNED_ROOTS):
    return False
  # Trailing punctuation from prose, and shell noise.
  if any(c in text for c in "*$<>|"):
    return False
  return "/" in text or text.endswith(EXTENSIONS)


def docs_to_check():
  found = []
  for name in ["AGENTS.md", "CLAUDE.md", "CONTRIBUTING.md", "README.md",
               "CODING-GUIDE.md"]:
    path = os.path.join(ROOT, name)
    if os.path.exists(path):
      found.append(path)
  for dirpath, dirnames, filenames in os.walk(os.path.join(ROOT, "docs")):
    dirnames[:] = [d for d in dirnames if d != "node_modules"]
    found += [os.path.join(dirpath, f) for f in filenames if f.endswith(".md")]
  return found


def _clean(text):
  return text.strip().rstrip(".,;:)")


def referenced_paths(doc):
  with open(doc, encoding="utf-8") as f:
    text = f.read()

  out = set()

  for match in INLINE_CODE.findall(text):
    candidate = _clean(match)
    if looks_like_a_path(candidate):
      out.add(candidate)
      continue
    # A command: `scripts/run-in-docker psql -d devdb`. The script it invokes is
    # still ours to keep working, even if the rest is arguments.
    first = _clean(candidate.split()[0]) if candidate.split() else ""
    if looks_like_a_path(first):
      out.add(first)

  for target in MARKDOWN_LINK.findall(text):
    candidate = _clean(target.split("#")[0])
    if not candidate or candidate.startswith(SKIP_PREFIXES):
      continue
    # Doc links are usually relative to the doc, and occasionally root-absolute.
    if candidate.startswith("/"):
      resolved = candidate.lstrip("/")
    else:
      resolved = os.path.relpath(
        os.path.join(os.path.dirname(doc), candidate), ROOT)
    if looks_like_a_path(resolved) or resolved.endswith(EXTENSIONS):
      out.add(resolved)

  return out


class TestDocsDrift(unittest.TestCase):

  def test_every_path_our_docs_name_still_exists(self):
    missing = []
    for doc in sorted(docs_to_check()):
      rel_doc = os.path.relpath(doc, ROOT)
      for path in sorted(referenced_paths(doc)):
        if not os.path.exists(os.path.join(ROOT, path)):
          missing.append(f"{rel_doc}: {path}")

    self.assertEqual(
      missing, [],
      "These docs name paths that don't exist:\n  " + "\n  ".join(missing))

  def test_the_check_can_actually_fail(self):
    # A drift check that silently matches nothing is worse than none, so prove it
    # recognises a path and notices when one is missing.
    self.assertTrue(looks_like_a_path("scripts/dev/build"))
    self.assertTrue(looks_like_a_path("backend/src/LibExecution/ProgramTypes.fs"))
    self.assertFalse(looks_like_a_path("https://example.com/a"))
    self.assertFalse(looks_like_a_path("some prose here"))
    self.assertFalse(looks_like_a_path("--filter"))
    self.assertFalse(os.path.exists(os.path.join(ROOT, "scripts/dev/nonexistent")))


if __name__ == "__main__":
  unittest.main(verbosity=2)
