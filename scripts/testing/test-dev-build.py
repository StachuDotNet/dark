#!/usr/bin/env python3.12
"""Tests for the build's planning and readiness logic.

  scripts/testing/test-dev-build.py

These modules decide whether the tree is built, which is the thing everything else
now trusts instead of grepping logs. They're worth testing directly, because the
alternative is finding out during a 75-second build.

No dev container needed and nothing is compiled: each test builds a small fake tree
in a temp directory and asks the modules about it.
"""

import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "build"))

import _buildindex  # noqa: E402
import _buildplan  # noqa: E402
import _buildstate  # noqa: E402


class TempTree:
  """A throwaway repo root, with the modules pointed at it."""

  def __enter__(self):
    self.dir = tempfile.mkdtemp(prefix="dev-build-test-")
    self.prev_cwd = os.getcwd()
    self.prev_rundir = os.environ.get("DARK_CONFIG_RUNDIR")

    os.makedirs(os.path.join(self.dir, "rundir"))
    self.write(".gitignore", "rundir/\nbackend/Build/\n")
    os.chdir(self.dir)
    os.environ["DARK_CONFIG_RUNDIR"] = os.path.join(self.dir, "rundir")

    # The ignore spec is cached per process and reads .gitignore from the cwd.
    _buildplan._spec = None
    return self

  def __exit__(self, *_):
    os.chdir(self.prev_cwd)
    if self.prev_rundir is None:
      os.environ.pop("DARK_CONFIG_RUNDIR", None)
    else:
      os.environ["DARK_CONFIG_RUNDIR"] = self.prev_rundir
    _buildplan._spec = None
    shutil.rmtree(self.dir, ignore_errors=True)

  def write(self, rel, contents):
    full = os.path.join(self.dir, rel)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w") as f:
      f.write(contents)
    return full


ALL = lambda _: True  # noqa: E731 - every file was covered


class TestMark(unittest.TestCase):
  """The file-to-action mapping."""

  def actions(self, path):
    should = _buildplan.Should()
    _buildplan.mark(should, path)
    return should

  def test_fsharp_source_takes_a_quick_build(self):
    self.assertTrue(self.actions("backend/src/LibDB/Queries.fs").backend_quick_build)

  def test_fsproj_takes_a_full_build(self):
    self.assertTrue(self.actions("backend/src/LibDB/LibDB.fsproj").backend_full_build)

  def test_dark_package_reloads_packages(self):
    should = self.actions("packages/darklang/stdlib/list.dark")
    self.assertTrue(should.reload_all_packages)
    self.assertTrue(should.reload_backend_server)

  def test_migration_runs_migrations(self):
    self.assertTrue(self.actions("backend/migrations/001-init.sql").run_migrations)

  def test_unrecognised_file_routes_nowhere(self):
    should = self.actions("backend/testfiles/execution/foo.dark")
    self.assertEqual(should.chosen(), [])
    self.assertEqual(should.unrouted, ["backend/testfiles/execution/foo.dark"])


class TestExpand(unittest.TestCase):
  """What a plan cascades to, assuming each step works."""

  def expand(self, path, **kw):
    should = _buildplan.Should()
    _buildplan.mark(should, path)
    return _buildplan.expand(should, **kw).chosen()

  def test_fsharp_change_reaches_the_package_reload(self):
    self.assertEqual(
      self.expand("backend/src/LibDB/Queries.fs"),
      ["backend_quick_build", "reload_backend_server", "run_migrations",
       "reload_all_packages"])

  def test_full_build_replaces_the_quick_one(self):
    actions = self.expand("backend/src/LibDB/LibDB.fsproj")
    self.assertIn("backend_full_build", actions)
    self.assertNotIn("backend_quick_build", actions)

  def test_tests_are_not_planned_unless_asked_for(self):
    self.assertNotIn("backend_test", self.expand("backend/src/LibDB/Queries.fs"))
    self.assertIn("backend_test",
                  self.expand("backend/src/LibDB/Queries.fs", run_tests=True))


class TestSatisfiedBy(unittest.TestCase):

  def test_a_full_build_satisfies_a_file_wanting_a_quick_one(self):
    # Otherwise every .fs in the tree looks unbuilt after a full build, because
    # none of them asked for the action that ran.
    needed = _buildplan.actions_for("backend/src/LibDB/Queries.fs")
    self.assertTrue(needed.issubset(_buildplan.satisfied_by({"backend_full_build"})))

  def test_a_quick_build_does_not_satisfy_a_file_wanting_a_full_one(self):
    needed = _buildplan.actions_for("backend/src/LibDB/LibDB.fsproj")
    self.assertFalse(needed.issubset(_buildplan.satisfied_by({"backend_quick_build"})))


class TestIgnores(unittest.TestCase):

  def test_editor_temp_files_are_ignored(self):
    # Two missing commas in this list used to concatenate the neighbouring patterns,
    # so these three reached the build.
    with TempTree():
      spec = _buildplan.spec()
      self.assertTrue(spec.match_file("packages/x.dark.tmp.123.abc"))
      self.assertTrue(spec.match_file("scripts/thing~"))
      self.assertTrue(spec.match_file(".devcontainer/devcontainer.json"))

  def test_gitignored_paths_are_ignored(self):
    with TempTree():
      self.assertTrue(_buildplan.spec().match_file("rundir/logs/build.log"))


class TestIndex(unittest.TestCase):
  """Content-addressed change detection."""

  def test_no_index_means_build_everything(self):
    with TempTree():
      self.assertIsNone(_buildindex.changed())

  def test_a_touched_file_with_the_same_contents_is_not_changed(self):
    # The whole reason the index hashes rather than stats: git checkout, rebase and
    # stash pop all move mtimes without changing a byte.
    with TempTree() as t:
      path = t.write("backend/src/A.fs", "let a = 1\n")
      _buildindex.save(_buildindex.snapshot())
      os.utime(path, (0, 0))
      self.assertEqual(_buildindex.changed(), [])

  def test_an_edited_file_is_changed(self):
    with TempTree() as t:
      t.write("backend/src/A.fs", "let a = 1\n")
      _buildindex.save(_buildindex.snapshot())
      t.write("backend/src/A.fs", "let a = 2\n")
      self.assertEqual(_buildindex.changed(), ["backend/src/A.fs"])

  def test_a_deleted_file_is_changed(self):
    with TempTree() as t:
      path = t.write("packages/x.dark", "let x = 1\n")
      _buildindex.save(_buildindex.snapshot())
      os.remove(path)
      self.assertEqual(_buildindex.changed(), ["packages/x.dark"])

  def test_roots_narrow_the_comparison(self):
    with TempTree() as t:
      t.write("backend/src/A.fs", "1")
      t.write("scripts/thing.sh", "#!/bin/bash\n")
      _buildindex.save(_buildindex.snapshot())
      t.write("scripts/thing.sh", "#!/bin/bash\necho hi\n")
      self.assertEqual(_buildindex.changed(roots=["backend"]), [])
      self.assertEqual(_buildindex.changed(), ["scripts/thing.sh"])

  def test_merge_leaves_uncovered_files_looking_changed(self):
    # A build of one .dark file mustn't claim to have built a changed .fs.
    with TempTree() as t:
      t.write("backend/src/A.fs", "let a = 1\n")
      t.write("packages/x.dark", "let x = 1\n")
      _buildindex.save(_buildindex.snapshot())

      t.write("backend/src/A.fs", "let a = 2\n")
      t.write("packages/x.dark", "let x = 2\n")

      previous = _buildindex.load()
      snapshot = _buildindex.snapshot(previous)
      only_packages = lambda rel: rel.startswith("packages/")  # noqa: E731
      _buildindex.save(_buildindex.merge(previous, snapshot, only_packages))

      self.assertEqual(_buildindex.changed(), ["backend/src/A.fs"])


class TestState(unittest.TestCase):

  def test_a_finished_build_is_ok_and_the_tree_is_clean(self):
    with TempTree() as t:
      t.write("backend/src/A.fs", "let a = 1\n")
      _buildstate.begin(["backend_quick_build"], ["backend/src/A.fs"], "build")
      _buildstate.finish(True, ["backend_quick_build"])
      _buildindex.save(_buildindex.snapshot())

      ready, fatal, _ = _buildstate.check()
      self.assertTrue(ready)
      self.assertFalse(fatal)

  def test_a_failed_build_is_fatal_for_anything_downstream(self):
    with TempTree():
      _buildstate.begin(["backend_quick_build"], [], "build")
      _buildstate.finish(False, ["backend_quick_build"], "backend_quick_build")

      ready, fatal, lines = _buildstate.check()
      self.assertFalse(ready)
      self.assertTrue(fatal)
      self.assertIn("backend_quick_build", " ".join(lines))

  def test_an_edit_after_a_build_warns_but_is_not_fatal(self):
    # Editing and then running is normal, so this must not block the CLI.
    with TempTree() as t:
      t.write("backend/src/A.fs", "let a = 1\n")
      _buildstate.begin(["backend_quick_build"], [], "build")
      _buildstate.finish(True, ["backend_quick_build"])
      _buildindex.save(_buildindex.snapshot())

      t.write("backend/src/A.fs", "let a = 2\n")
      ready, fatal, lines = _buildstate.check()
      self.assertFalse(ready)
      self.assertFalse(fatal)
      self.assertIn("behind the tree", " ".join(lines))

  def test_a_lint_only_build_does_not_claim_the_tree_is_built(self):
    # shellcheck passing doesn't make the binaries any newer. If it moved the mark,
    # a .fs edited in the same second would look built.
    self.assertFalse(_buildstate.built_successfully(["shellcheck"]))
    self.assertTrue(_buildstate.built_successfully(["backend_quick_build"]))

  def test_a_skipped_build_step_does_not_count_as_built(self):
    self.assertFalse(
      _buildstate.built_successfully(["backend_quick_build"],
                                     failed_action="backend_quick_build",
                                     skipped=["reload_all_packages"]))

  def test_an_interrupted_build_is_reported_rather_than_believed(self):
    with TempTree():
      _buildstate.begin(["backend_quick_build"], [], "build")
      state = _buildstate.read()
      state["pid"] = 2 ** 22  # a pid that cannot be running
      _buildstate._write(state)

      ready, fatal, lines = _buildstate.check()
      self.assertFalse(ready)
      self.assertTrue(fatal)
      self.assertIn("didn't finish", " ".join(lines))


if __name__ == "__main__":
  unittest.main(verbosity=2)
