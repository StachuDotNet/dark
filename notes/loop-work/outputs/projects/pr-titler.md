# pr-titler

**Goal:** Read a git diff and produce a conventional-commit-style title via a single bounded LLM call, with an offline fixture mode for reproducible testing.

**Kind:** greenfield

## Acceptance criteria
- [ ] `pr-titler --diff <path>` reads a unified diff and prints a single line of the form `<type>(<scope>): <description>` (or `<type>: <description>` when scope isn't obvious).
- [ ] `cat diff.txt | pr-titler` reads the diff from stdin.
- [ ] The output line is ≤72 characters.
- [ ] `<type>` is one of `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `chore`, chosen from the diff (no `unknown:` fallback).
- [ ] A README-only diff yields `docs:`; a diff adding a function in a non-test file yields `feat:`; a test-only diff yields `test:`.
- [ ] `<scope>` is present for single-module diffs and omitted for cross-cutting diffs.
- [ ] `pr-titler --fixture <path>` returns the fixture's `expected_output` verbatim, bypassing the LLM (used for reproducible scoring).
- [ ] The LLM call is bounded (max tokens / max turns) and is a single one-shot translation, not a multi-turn loop.
- [ ] An LLM call failure logs to stderr and exits non-zero; `--diff /nonexistent` exits non-zero.
- [ ] `pr-titler --help` exits 0.
